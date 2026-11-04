# ClickUp Widget — пам'ятка для Claude

.NET 8 програма: спокійний дашборд задач ClickUp. Windows — WPF (`src/`), Mac — Avalonia (`mac/`), спільна логіка — `core/`. Автор — Діма (пише українською, коротко; інтерфейс і коментарі в коді — українською).

## Структура
- `core/` (ClickUpWidget.Core, net8.0, без вікон) — спільне для Windows і Mac:
  - `ClickUpClient.cs` — мінімальний клієнт ClickUp API v2; `Model.cs`, `Inbox.cs`, `CommentText.cs` — чиста логіка (покрита тестами);
  - `Settings.cs`, `Secret.cs` (токен: DPAPI на Windows, Keychain через `/usr/bin/security` на Mac), `Log.cs`, `Updater.cs` (Velopack);
  - `Shared/WidgetController.cs` — мозок без прив'язки до вікон (порт `src/AppController.cs`), `Shared/ViewModels.cs` (кольори — рядки з `Palette`), `Shared/Shell.cs` (`IWidgetShell` — що потрібно від платформи, `MenuNode` — меню).
- `src/` — Windows (WPF): `AppController.cs` — мозок Windows-версії (поки свій, не `WidgetController`), `PanelWindow` (панель на столі), `FlyoutWindow` (картка з трею), `InboxWindow` (тихий інбокс), `TasksView`, `CommentBox` (поле коментаря з @згадками), `TrayController` (меню), `DesktopPin.cs` (Progman + HWND_BOTTOM), `Autostart.cs` (реєстр Run).
- `mac/` — Mac (Avalonia 11.3): `MacShell.cs` (вікна, значок у рядку меню, меню), `MacNative.cs` (рівень вікна над іконками столу, активація, LaunchAgent), `Views/` (ті самі вікна, що в WPF), `Demo.cs` (`--demo`, `--screenshot`, `--selftest`), `build-mac.sh`, `Info.plist`. Інструкція — `docs/macos.md`.
- `tests/ClickUpWidget.Tests` — xunit, net8.0 (лише `core/`), ганяються на Windows і Mac.
- Нова функція в логіці: поки що в обидва мозки — `src/AppController.cs` і `core/Shared/WidgetController.cs`.

## Збірка й реліз
- Локально: `dotnet build src/ClickUpWidget.csproj`, тести: `dotnet test tests/ClickUpWidget.Tests`. Mac-версія збирається й на Windows: `dotnet build mac/ClickUpWidget.Mac.csproj`; вигляд без ClickUp — `dotnet run --project mac -- --demo`.
- Push у `main` → GitHub Actions; нова `<Version>` у `src/ClickUpWidget.csproj` → реліз з інсталятором (Velopack), встановлені копії оновлюються самі. Версія Mac береться звідти ж.
- Mac: `.github/workflows/macos.yml` на `macos-latest` (push у гілку `macos` або вручну) — пакети в артефактах; до релізу додаються лише вручну (Run workflow → publish).
- Налаштування, стан інбоксу й журнал: `%AppData%\ClickUpWidget`, на Mac `~/Library/Application Support/ClickUpWidget` (`log.txt` — перше, що дивитись при багах).

## Побажання користувача
- Без перевантаження і без «червоних» акцентів; дедлайни не підсвічувати.
- Беклог і скасовані за замовчуванням сховані; усе налаштовується в меню «Що показувати».
- Інтерфейс не повинен стрибати при наведенні; меню не вилазять на сусідній монітор.
- Призначення в інбоксі за замовчуванням вимкнені.

## Стан: 0.4.0 (зібрано, тести зелені, перевірено наживо 03.11.2026)
Що в цій версії:
1. Інбокс — усі типи подій: коментарі, згадки мене (@), мої коментарі, статуси, закриті/видалені, дедлайн, пріоритет, перейменування, призначення, мене прибрали, інші виконавці. Кожен тип вмикається в «Що показувати → Віконце оновлень». Старий `inbox.json` мігрує тихо (`SeenTask.Full`).
2. Коментарі: кнопка при наведенні на задачу (панель/трей) і «Відповісти»/«Коментар» в інбоксі; поле відкривається в тому ж вікні під рядком; `@` показує список людей (учасники задачі першими), Enter — надіслати, Shift+Enter — новий рядок, Esc — закрити. Поки поле відкрите, списки не перебудовуються.
3. Тихе автооновлення двічі на день (`WaitExitThenApplyUpdates(silent: true)`), відкладається, якщо людина пише коментар.
4. У рядку задачі опційно: мітка проєкту, дедлайн, прапорець пріоритету. Кольори пріоритету спокійні (`TaskText.PriorityColor`), кольори ClickUp не беремо — «Терміновий» там червоний.
5. Задача зникла зі списку, а ClickUp на неї відповідає 401/403 (забрали доступ) — тихо забуваємо, а не перепитуємо щоразу.
6. (0.4.1) Задача з таймером показується один раз — зверху, у картці таймера (`DashboardViewModel.RunningRow`): у «Сьогодні» й «Усіх відкритих» її немає (`TaskText.WithoutRunning`). У картці при наведенні — коментар і статус, клік відкриває задачу. Якщо картку таймера сховано в «Що показувати», задача стоїть першим рядком (`RunningHost`).

Перевірено наживо: збірка запускається, фокус клавіатури в полі коментаря на панелі працює, список @ (учасники задачі першими), Popup закривається, коли вікно втрачає фокус, `inbox.json` мігрує тихо (без зайвих подій).
Ще не перевірено: справжнє надсилання коментаря (`PostCommentAsync`, масив `comment` з `tag`) і тихе оновлення — його можна побачити лише на наступному релізі (0.4.0 → 0.4.x).
