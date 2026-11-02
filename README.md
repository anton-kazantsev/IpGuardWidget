# IP Guard — виджет запуска Claude Code для Windows 10/11

Плавающий виджет 360×277 px (340 px в состоянии блокировки): показывает текущий IP и страну,
проверяет доступность Claude Code и запускает его в Windows Terminal. Если страна — Россия,
кнопка блокируется с подсказкой «Включи VPN»; после смены IP виджет сам перепроверяет страну
и разблокирует кнопку.

## Стек

- **C# / .NET 10 (LTS), WPF** — один самодостаточный `.exe`, без установленного .NET и без WebView2.
- Без NuGet-зависимостей: HTTP — `HttpClient`, JSON — `System.Text.Json`, трей — Win32 `Shell_NotifyIcon`,
  меню — нативное `TrackPopupMenu`, автозапуск — реестр.

## Сборка

Нужен только [.NET SDK 10](https://dotnet.microsoft.com/download) (на машине разработчика ставится
командой `winget install Microsoft.DotNet.SDK.10`).

```powershell
cd Documents\IpGuardWidget
dotnet build -c Debug                 # отладочная сборка
dotnet run -c Debug                   # запуск
```

Публикация одним exe (лежит в `bin\Release\net10.0-windows\win-x64\publish\`):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:ReadyToRun=true `
  -p:EnableCompressionInSingleFile=true
```

Готовый файл — `IpGuardWidget.exe` (~50–70 МБ)

## Запуск

| Команда | Что делает |
|---|---|
| `IpGuardWidget.exe` | показывает виджет в правом нижнем углу |
| `IpGuardWidget.exe --tray` | стартует свёрнутым в трей (используется автозапуском) |
| `IpGuardWidget.exe --simulate NL` | имитация страны (NL / RU / DE / US / IPV6 / SLOW / OFFLINE) |

Повторный запуск exe не открывает второе окно: первый экземпляр поднимается на передний план.

## Поведение

- **Проверка IP**: при старте и каждые 5 минут; в состоянии «Россия» — каждые 20 секунд,
  чтобы кнопка разблокировалась сразу после включения VPN. При отсутствии сети — каждые 30 секунд.
- **Источники данных** (без API-ключей): `api.ipify.org` → `api64.ipify.org` → `ifconfig.me`
  для IP; `ipapi.co` → `ipwho.is` для страны и города. Результат кешируется, при сбое
  показывается последний известный IP и пометка «Нет сети».
- **Перед запуском** IP перепроверяется, если последняя проверка старше 45 секунд.
- **Запуск Claude**: `wt.exe new-tab --title "Claude Code" -d "<папка>" cmd.exe /k claude`.
  Вкладка остаётся открытой после выхода Claude. Если Windows Terminal не установлен —
  обычное окно `cmd /k claude`. Поддерживаются `claude.exe` (нативный установщик) и
  `claude.cmd` (npm). Если Claude не найден, виджет показывает подсказку с командой установки.
- **Окно**: без рамки, always-on-top, перетаскивание за карточку, закрытие/Esc — в трей,
  позиция запоминается по нижнему правому углу карточки, второй экземпляр поднимает окно.
- **Трей**: левый клик — показать/скрыть, правый клик — меню: обновить IP, всегда сверху,
  автозапуск, оболочка запуска (cmd / PowerShell / напрямую), папка запуска, сбросить позицию, выход.
- **Автозапуск** (по желанию): `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `"...\IpGuardWidget.exe" --tray`.

## Где лежат данные

- Настройки: `%APPDATA%\IpGuardWidget\settings.json`
- Лог: `%APPDATA%\IpGuardWidget\ipguard.log`

Позиция хранится в физических пикселях и проверяется по реальным рабочим областям мониторов,
поэтому виджет не теряется при смене конфигурации экранов и на мониторах с масштабом 125/150%.
Если окно всё же оказалось вне экрана: правый клик по иконке в трее → «Сбросить позицию»
(или удалить `settings.json`).

## Структура

```
IpGuardWidget.csproj      публикация одним exe, манифест DPI/Win10-11
App.xaml(.cs)             старт, single instance, аргументы, тёмные меню
MainWindow.xaml(.cs)      карточка, анимация высоты, drag, трей, запуск
ViewModels/               состояние карточки и расписание проверок
Services/                 IP-геокеер, лаунчер, настройки, автозапуск, трей, лог
Controls/                 флаг из ISO-кода, скруглённый клип
Theme/Tokens.xaml         цвета, шрифты, стили кнопок
tools/make-icon.ps1       генерация Assets/app.ico из бренд-марки
```

## Ограничения

- Неподписанный exe может ловить предупреждение SmartScreen — для раздачи нужен
  Authenticode-сертификат (или установщик с подписью).
