using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Monochrome.AppServices
{
    /// <summary>
    /// Единое хранилище состояния: настройки, история, закладки, ярлыки.
    /// Сериализуется в LocalSettings как JSON. Потокобезопасность не требуется
    /// (доступ только с UI-потока).
    /// </summary>
    public static class Store
    {
        private const string Key = "mono:state";

        public static bool Gateway { get; set; } = false;
        public static string GatewayUrl { get; set; } = "https://mono-gw.darkzenver.workers.dev";
        public static string Engine { get; set; } = "ddg";
        public static bool SaveHistory { get; set; } = true;

        public static List<string> History = new List<string>();
        public static List<Bookmark> Bookmarks = new List<Bookmark>();
        public static List<Shortcut> Shortcuts = new List<Shortcut>
        {
            new Shortcut("Wikipedia", "https://ru.wikipedia.org"),
            new Shortcut("YouTube", "https://www.youtube.com"),
            new Shortcut("GitHub", "https://github.com"),
            new Shortcut("Reddit", "https://www.reddit.com"),
            new Shortcut("Maps", "https://www.openstreetmap.org"),
            new Shortcut("Hacker News", "https://news.ycombinator.com"),
        };

        public sealed class Bookmark { public string Title { get; set; } public string Url { get; set; } }
        public sealed class Shortcut { public string Title { get; set; } public string Url { get; set; } }
        public sealed class HistoryItem { public string Title { get; set; } public string Url { get; set; } public double At { get; set; } }

        public sealed class State
        {
            public bool Gateway { get; set; }
            public string GatewayUrl { get; set; }
            public string Engine { get; set; }
            public bool SaveHistory { get; set; }
            public HistoryItem[] History { get; set; } = new HistoryItem[0];
            public Bookmark[] Bookmarks { get; set; } = new Bookmark[0];
            public Shortcut[] Shortcuts { get; set; } = new Shortcut[0];
        }

        public static void Save()
        {
            try
            {
                var st = new State
                {
                    Gateway = Gateway,
                    GatewayUrl = GatewayUrl,
                    Engine = Engine,
                    SaveHistory = SaveHistory,
                    History = History.Take(200).Select(u => new HistoryItem
                    {
                        Url = u,
                        Title = DomainOf(u),
                        At = 0
                    }).ToArray(),
                    Bookmarks = Bookmarks.ToArray(),
                    Shortcuts = Shortcuts.ToArray(),
                };

                var json = ToJson(st);
                var local = ApplicationData.Current.LocalSettings;
                local.Values[Key] = json;
            }
            catch (Exception) { /* При выключении приложения ошибки записи игнорируем. */ }
        }

        public static void Load()
        {
            try
            {
                var local = ApplicationData.Current.LocalSettings;
                if (!(local.Values[Key] is string json)) return;
                var st = FromJson<State>(json);
                if (st == null) return;

                Gateway = st.Gateway;
                GatewayUrl = string.IsNullOrWhiteSpace(st.GatewayUrl) ? GatewayUrl : st.GatewayUrl;
                Engine = string.IsNullOrWhiteSpace(st.Engine) ? "ddg" : st.Engine;
                SaveHistory = st.SaveHistory;

                History = (st.History ?? new HistoryItem[0]).Select(h => h.Url).ToList();
                Bookmarks = new List<Bookmark>(st.Bookmarks ?? new Bookmark[0]);

                if (st.Shortcuts != null && st.Shortcuts.Length > 0)
                    Shortcuts = new List<Shortcut>(st.Shortcuts);
            }
            catch (Exception) { /* повреждённое состояние не критично. */ }
        }

        public static void AddHistory(string url)
        {
            if (!SaveHistory || string.IsNullOrEmpty(url)) return;
            History.Remove(url);
            History.Insert(0, url);
            if (History.Count > 200) History.RemoveRange(200, History.Count - 200);
        }

        public static string DomainOf(string url)
        {
            try
            {
                var u = new Uri(url);
                return u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host;
            }
            catch { return url ?? ""; }
        }

        public static string SearchUrl(string query)
        {
            var q = Uri.EscapeDataString(query ?? "");
            switch (Engine)
            {
                case "ddg": return "https://duckduckgo.com/?q=" + q;
                case "bing": return "https://www.bing.com/search?q=" + q;
                case "wiki": return "https://ru.wikipedia.org/w/index.php?search=" + q;
                case "google": return "https://www.google.com/search?q=" + q;
                default: return "https://duckduckgo.com/?q=" + q;
            }
        }

        /// <summary>Прокси-режим: URL открывается через Gateway-воркер (?orig=). Статика внутри страницы идёт по абсолютным ссылкам воркера.</summary>
        public static string Route(string url)
        {
            if (!Gateway) return url;
            if (string.IsNullOrWhiteSpace(url)) return url;
            try
            {
                var u = new Uri(url);
                if (u.Scheme != "http" && u.Scheme != "https") return url;
            }
            catch { return url; }

            var gw = (GatewayUrl ?? "").Trim().TrimEnd('/');
            if (gw.Length == 0) return url;

            return gw + "/?orig=" + System.Net.WebUtility.UrlEncode(url);
        }

        // --- JSON-хелперы на DataContractJsonSerializer (UWP без внешних пакетов) ---

        private static string ToJson<T>(T value)
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new System.IO.MemoryStream())
            {
                ser.WriteObject(ms, value);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static T FromJson<T>(string json)
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)ser.ReadObject(ms);
            }
        }

        public static SolidColorBrush Br(string key) => Application.Current.Resources[key] as SolidColorBrush;

        public static Color ColorOf(string key)
        {
            var c = (Color)Application.Current.Resources[key];
            return c;
        }

        public static Color FromColor(string key) => ColorOf(key);

        /// <summary>Белый цвет статусбара (не зависит от ресурсов темы).</summary>
        public static Color White => Colors.White;
    }
}
