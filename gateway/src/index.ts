/**
 * Monochrome Gateway — Cloudflare Worker.
 *
 * Даёт старому движку (EdgeHTML 2017, Windows 10 Mobile) открывать современные
 * страницы: проксирует трафик, перезаписывает URL ресурсов, снимает CSP,
 * подменяет User-Agent и инжектит рантайм-шимы (fetch/XHR/EventSource/WS).
 *
 * Формат адреса страницы: https://gw.workers.dev/https://example.com/page
 * либо query-форма:       https://gw.workers.dev/?orig=<urlencoded>
 *
 * Ограничения v0.2 (осознанно, поэтапно):
 *  - cookie-джара нет: куки upstream уходят «как есть» (домен воркера);
 *  - JS бандлы не транспилируются (ES2020+): транспиляция — следующий шаг
 *    для целевых сайтов (chatgpt.com, deepseek.com);
 *  - относительные URL в JS разрешаются через <base> и шим.
 */

const DESKTOP_UA =
	'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36';

const SKIP_RE = /^(data|blob|filesystem|javascript|mailto|tel|about|#)/i;

const HOP_HEADERS = new Set([
	'host', 'connection', 'keep-alive', 'upgrade', 'te', 'trailer', 'transfer-encoding',
	'content-security-policy', 'content-security-policy-report-only',
	'x-frame-options', 'cross-origin-opener-policy', 'cross-origin-embedder-policy',
	'cross-origin-resource-policy', 'permissions-policy', 'report-to',
	'strict-transport-security', 'origin', 'reporting-endpoints', 'nel',
	'cf-connecting-ip', 'cf-ipcountry', 'cf-ray', 'cf-worker', 'cf-visitor',
]);

export interface EnvTyp {}

function parseTarget(request: Request): URL | undefined {
	const u = new URL(request.url);
	const orig = u.searchParams.get('orig');
	if (orig) {
		try { return new URL(orig); } catch { return undefined; }
	}
	const raw = u.pathname.slice(1);
	if (!raw) return undefined;
	try {
		return new URL(raw.startsWith('http') ? raw : decodeURIComponent(raw));
	} catch {
		return undefined;
	}
}

export default {
	async fetch(request, env, ctx): Promise<Response> {
		const target = parseTarget(request);
		if (!target) {
			return new Response(gatewayHome(), { headers: { 'content-type': 'text/html; charset=utf-8' } });
		}
		if (target.protocol !== 'http:' && target.protocol !== 'https:') {
			return new Response('Monochrome Gateway: только http/https', { status: 400 });
		}
		try {
			return await proxy(request, target);
		} catch (e) {
			return new Response(`Monochrome Gateway error: ${(e as Error).message}`, { status: 502 });
		}
	},
} satisfies ExportedHandler;

async function proxy(request: Request, target: URL): Promise<Response> {
	const headers = new Headers();
	headers.set('user-agent', DESKTOP_UA);
	headers.set('accept', request.headers.get('accept') ?? '*/*');
	headers.set('accept-language', request.headers.get('accept-language') ?? 'ru-RU,ru;q=0.9,en;q=0.8');

	for (const [k, v] of request.headers) {
		const key = k.toLowerCase();
		if (key === 'cookie' || key === 'accept-encoding' || key.startsWith('sec-')) continue;
		if (key === 'if-none-match' || key === 'if-modified-since') continue;
		if (HOP_HEADERS.has(key)) continue;
		headers.set(k, v);
	}

	let body: BodyInit | null = null;
	if (request.method !== 'GET' && request.method !== 'HEAD' && request.body) {
		body = await request.arrayBuffer();
	}

	const upstream = await fetch(target.toString(), {
		method: request.method,
		headers,
		body,
		redirect: 'follow',
	});

	if (upstream.status >= 300 && upstream.status < 400) {
		const red = upstream.headers.get('location') ?? target.toString();
		const next = new URL(red, upstream.url).toString();
		return new Response(null, {
			status: 302,
			headers: { location: `/${next}` },
		});
	}

	const ctype = upstream.headers.get('content-type') ?? '';
	if (ctype.includes('text/html')) return rewriteHtml(upstream, new URL(request.url).origin);
	if (ctype.includes('text/css')) return rewriteCss(upstream);
	return passthrough(upstream);
}

// ------------------------------------------------------------------
// Ответы
// ------------------------------------------------------------------

function cleanHeaders(src: Headers): Headers {
	const h = new Headers();
	for (const [k, v] of src) {
		const key = k.toLowerCase();
		if (HOP_HEADERS.has(key)) continue;
		h.set(k, v);
	}
	h.delete('content-security-policy');
	h.delete('content-security-policy-report-only');
	h.delete('x-frame-options');
	return h;
}

function passthrough(upstream: Response): Response {
	return new Response(upstream.body, {
		status: upstream.status,
		statusText: upstream.statusText,
		headers: cleanHeaders(upstream.headers),
	});
}

// ------------------------------------------------------------------
// HTML
// ------------------------------------------------------------------

function remap(url: string | null, pageOrigin: string): string | undefined {
	if (!url) return undefined;
	const t = url.trim();
	if (!t || SKIP_RE.test(t)) return undefined;
	if (/^https?:\/\//i.test(t)) return `${pageOrigin}/${t}`;
	return undefined; // относительные resolves via <base>
}

function remapSrcset(srcset: string, pageOrigin: string): string {
	return srcset.split(',').map((part) => {
		const seg = part.trim();
		if (!seg) return seg;
		const sp = seg.indexOf(' ');
		const url = sp < 0 ? seg : seg.slice(0, sp);
		const mapped = remap(url, pageOrigin);
		return mapped ? mapped + (sp < 0 ? '' : seg.slice(sp)) : seg;
	}).join(', ');
}

const HANDLER_INJECT = `<!-- Monochrome Gateway v0.2 -->`;

function rewriteHtml(upstream: Response, gwOrigin: string): Response {
	// Реальный финальный URL — upstream.url после redirect: follow.
	const pageUrl = upstream.url;
	const page = new URL(pageUrl);
	const pageOrigin = page.origin;

	const dir = (page.pathname.slice(0, page.pathname.lastIndexOf('/') + 1)) || '/';
	const baseHref = `${gwOrigin}/${page.protocol}//${page.host}${dir}`;

	const shim = buildShim(pageUrl);

	const rewriter = new HTMLRewriter()
		.on('meta[http-equiv="content-security-policy"]', { element(e) { e.remove(); } })
		.on('meta[http-equiv="Content-Security-Policy"]', { element(e) { e.remove(); } });

	// base href → gateway-каталог текущей страницы
	rewriter.on('base', { element(e) { e.remove(); } });

	// абсолютные абсолютные атрибуты
	rewriter.on('a', {
		element(e) {
			const v = e.getAttribute('href');
			const m = remap(v, pageOrigin);
			if (m) e.setAttribute('href', m);
		},
	});
	rewriter.on('img', {
		element(e) {
			const m = remap(e.getAttribute('src'), pageOrigin);
			if (m) e.setAttribute('src', m);
			const ss = e.getAttribute('srcset');
			if (ss) e.setAttribute('srcset', remapSrcset(ss, pageOrigin));
		},
	});
	rewriter.on('link', {
		element(e) {
			const m = remap(e.getAttribute('href'), pageOrigin);
			if (m) e.setAttribute('href', m);
		},
	});
	rewriter.on('iframe', {
		element(e) {
			const m = remap(e.getAttribute('src'), pageOrigin);
			if (m) e.setAttribute('src', m);
		},
	});
	rewriter.on('source', {
		element(e) {
			const ss = e.getAttribute('srcset');
			if (ss) e.setAttribute('srcset', remapSrcset(ss, pageOrigin));
		},
	});
	rewriter.on('head', {
		element(head) {
			head.prepend(`<base href="${baseHref.replace(/"/g, '&quot;')}">`, { html: true });
			head.append(`<script>${shim}</script>`, { html: true });
			head.append(HANDLER_INJECT, { html: true });
		},
	});

	return rewriter.transform(new Response(upstream.body, {
		status: upstream.status,
		statusText: upstream.statusText,
		headers: cleanHeaders(upstream.headers),
	}));
}

/**
 * Шим-скрипт (инлайн): fetch/XHR/EventSource/WS направляются через gateway.
 * Абсолютные http(s) адреса → gw/https://...; абсолютные пути (/...) →
 * gw/https://host/путь; относительные — разрешит <base>.
 */
function buildShim(pageUrl: string): string {
	return `(function(){
var MAIL = ${JSON.stringify(pageUrl)};
var BASE=null; try{BASE=new URL(MAIL);}catch(e){}
var GW=location.origin;var VO=BASE?BASE.origin:"https://example.com";
function map(u){
 try{
  if(typeof u!=="string"||!u) return u;
  if(/^(data|blob|javascript|mailto|tel|about|#)/i.test(u)) return u;
  if(/[\\/\\\\]gatewayfs$/i.test(u)) return u;
  if(/^https?:\\/\\//i.test(u)) return GW+"/"+u;
  if(/^wss?:\\/\\//i.test(u)) return GW+"/"+u;
  if(u.charAt(0)==="/") return GW+"/"+VO+u;
  return u;
 }catch(e){return u;}
}
if(self.fetch){
 var of=self.fetch;
 self.fetch=function(i,ini){
  try{
   if(typeof i==="string") i=map(i);
   else if(i&&typeof i.url==="string"){
     var m=map(i.url);
     if(m!==i.url) i=new Request(m,{method:i.method,headers:i.headers,mode:i.mode,credentials:i.credentials,cache:i.cache,redirect:i.redirect,referrer:i.referrer,referrerPolicy:i.referrerPolicy,integrity:i.integrity,body:i.body});
   }
  }catch(e){}
  return of.call(self,i,ini);
 };
}
if(self.XMLHttpRequest){
 var oo=XMLHttpRequest.prototype.open;
 XMLHttpRequest.prototype.open=function(m,u){
  var args=Array.prototype.slice.call(arguments);
  try{ if(typeof args[1]==="string") args[1]=map(args[1]); }catch(e){}
  return oo.apply(this,args);
 };
}
if(self.EventSource){
 var oe=self.EventSource;
 self.EventSource=function(u,c){ try{u=map(u);}catch(e){} return c?new oe(u,c):new oe(u); };
 self.EventSource.prototype=oe.prototype;
}
if(self.WebSocket){
 var ow=self.WebSocket;
 self.WebSocket=function(u,p){ try{u=map(u);}catch(e){} return p?new ow(u,p):new ow(u); };
 self.WebSocket.prototype=ow.prototype;
 self.WebSocket.CONNECTING=0;self.WebSocket.OPEN=1;self.WebSocket.CLOSING=2;self.WebSocket.CLOSED=3;
}
if(navigator && navigator.serviceWorker){ try{ Object.defineProperty(navigator,'serviceWorker',{value:undefined}); }catch(e){} }
})();`;
}

// ------------------------------------------------------------------
// CSS
// ------------------------------------------------------------------

async function rewriteCss(upstream: Response): Promise<Response> {
	const cssUrl = upstream.url;
	const cssOrigin = new URL(cssUrl).origin;
	let text = '';
	const dec = new TextDecoder();
	const reader = upstream.body?.getReader();
	if (reader) {
		for (;;) {
			const r = await reader.read();
			if (r.done) break;
			text += dec.decode(r.value, { stream: true });
		}
		text += dec.decode();
	}

	const out = text.replace(/url\(\s*(['"]?)([^'")]+?)\1\s*\)/gi, (m, q, raw) => {
		if (SKIP_RE.test(raw.trim())) return m;
		if (/^https?:\/\//i.test(raw)) return `url(${cssOrigin}/${raw})`;
		if (raw.startsWith('/')) return `url(${cssOrigin}${raw})`;
		return m;
	});

	return new Response(out, {
		status: upstream.status,
		statusText: upstream.statusText,
		headers: cleanHeaders(upstream.headers),
	});
}

function gatewayHome(): string {
	return `<!doctype html><html><head><meta charset="utf-8"><title>Monochrome Gateway</title></head>
<body style="background:#050505;color:#f2f2f2;font-family:sans-serif;text-align:center;padding-top:80px">
<h1 style="letter-spacing:.4em;font-weight:300">MONOCHROME</h1>
<p style="color:#8a8a8a">Gateway активен.<br/>Формат: https://host/https://example.com/page</p>
</body></html>`;
}
