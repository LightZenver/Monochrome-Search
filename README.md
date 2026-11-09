# Monochrome

Монохромный браузер для Windows 10 Mobile (Lumia 435 и совместимые). Минимальная ОС: 10.0.10240, целевая сборка телефона — 10.0.15254 (1709).

## Состав

- `src/Monochrome.sln` — решение UWP (C# + XAML): `WebView` (EdgeHTML) на Windows 10 Mobile.
- `gateway/` — Cloudflare Worker (Monochrome Gateway): режим совместимости для современных сайтов (фаза 4).
- `signing/monochrome.cer` — публичный сертификат подписи приложения (ставится на телефон один раз).
- `docs/res/` — дизайн-прототип стартового экрана и логотип.
- `.github/workflows/` — CI (ноч. сборка `.appx`) и релизы по тегам `v*`.

## Сборка (GitHub Actions)

1. Секреты подписи заданы в GitHub Actions:
   - `MONO_SIGN_PFX_B64` — .pfx (base64), CN=Monochrome;
   - `MONO_SIGN_PASS` — пароль .pfx.
2. Каждый push в `main` даёт артефакт `monochrome-nightly` (`.appx` + `.cer`).
3. Релизы: тег `v1.2.3` — пакет `.appx` публикуется в GitHub Releases автоматически.

## Установка на Lumia (Windows 10 Mobile)

1. На телефоне: «Настройки → Обновление и безопасность → Для разработчиков» → включить режим разработчика и Device Portal.
2. Подключить телефон к той же Wi-Fi сети ПК, открыть адрес портала устройств, показанный в настройках.
3. В Device Portal → вкладка App Manager → загрузить `.appx` и установить.
4. Если телефон требует доверенный сертификат: перед установкой `.appx` загрузите в портал `monochrome.cer` (раздел Admin/Certificates, Trusted People). После этого обновления приложения ставятся без повторов.

## Состояние фаз

- [x] Ф1: каркас, CI, подпись
- [x] Ф2: ядро браузера (адресная строка, WebView, история, закладки, настройки)
- [ ] Ф3: Reader-режим, per-site правила, UA
- [ ] Ф4: Monochrome Gateway (транспиляция JS, шимы, URL-rewriting)
- [ ] Ф5: тесты chatgpt.com / deepseek.com
- [ ] Ф6: релизная инструкция, иконки, локализация
