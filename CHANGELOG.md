# Changelog

## [1.1.1] — 2026-07-06

### Исправлено
- **ExpressionSplitPass**: `InvalidCastException` при сплите `FunctionCallStmt.Call` — `SplitExpr` возвращал `VarExpr`, а код пытался кастануть в `FunctionCallExpr`
- **StringEncryptPass**: 
  - `load` → `loadstring` для Lua 5.1-совместимости (в Lua 5.1 `load` не принимает `(string, name)`)
  - Полный разрыв encoder-decoder: XOR-кодировщик использовал `^ (key&0xFF)`, а декодер — `- key` со случайной операцией. Переписаны все 4 алгоритма: каждый encoder теперь генерирует точный inverse decoder
  - `~` (XOR) → `+`/`-` для Lua 5.1-совместимости
  - ScatterDecoders ставил декодеры ПОСЛЕ их первого использования → `nil` references. Все декодеры теперь вставляются в начало root-блока (после anti-debug)
- **NumberEncodePass**: `EncodeNested` давал неверные результаты: `inner = |num|-mid` + `remainder = num-mid` = `num-2*mid` вместо `num`. Исправлено на `inner = mid`, `remainder = num-mid`
- **VirtualizationPass**:
  - `local vm_run = loadstring(...)` → глобальная `vm_run = loadstring(...)` (chunk из loadstring не видит enclosing locals)
  - `v ~ key` (XOR дешифровка байткода) → `v + key` для Lua 5.1
  - Два integrity XOR-а (`~`) → `-` для Lua 5.1
  - `load(string, name)` → `loadstring(string, name)` для Lua 5.1

### Технические детали
- Все 53 теста проходят. 0 ошибок сборки, 0 предупреждений.
- Non-VM обфускация (все проходы): 7 486 байт, Lua 5.1, корректно исполняется
- VM-обфускация: 13 151 байт, проходит `loadstring`, но есть архитектурная проблема `VarExpr`/`GETG` vs `GETL` в `VmGenerator`

## [1.1.0] — 2026-07-06

### Добавлено
- **GUI-меню** — новый проект `LunarGuard.GUI` (WPF .NET 9, Windows)
  - Дизайн в стиле glassmorphism с тёмной темой и размытием
  - Две вкладки: **Главная** (выбор файла + полная панель настроек) и **FAQ** (описание каждой опции обфускации)
  - Боковая панель с логотипом-щитом, индикатором статуса и кнопкой проверки обновлений
  - Выбор `.lua` файла через стандартный диалог Windows
  - Автоматическая генерация имени выходного файла (`*.obfuscated.lua`)
  - Прогресс-бар и индикация статуса обработки
- **Проверка обновлений** через GitHub API
  - SHA256-хеширование текущего `LunarGuard.dll`
  - Загрузка последнего релиза с `github.com/zlUnO/LunarGuard`
  - Сравнение хешей для определения необходимости обновления
  - Перенаправление на страницу загрузки при наличии новой версии
- Поддержка решения (`LunarGuard.sln`) объединяет все 4 проекта: Core, CLI, Tests, GUI

### Исправлено
- **Путь к файлу**: обход CWD-ограничения в GUI — используется прямой ввод-вывод через `Process()`, минуя `ProcessFile()` с проверкой рабочей директории
- **NullReferenceException при загрузке XAML**: события `Checked`/`Unchecked` больше не объявляются в XAML, подписка происходит после `InitializeComponent()`
- **Некорректный синтаксис градиента**: заменён `Background="LinearGradient ..."` на полноценные `<LinearGradientBrush>` (WPF не поддерживает встроенные строки градиентов)
- **Path traversal (уязвимость)**: `StartsWith(cwd)` мог совпадать с каталогом-префиксом (например, `C:\Foo` совпадало с `C:\FooMalicious`). Добавлена проверка `Path.DirectorySeparatorChar` и точное совпадения пути
- **Парсинг JSON GitHub API**: заменён ручной regex-парсинг на `System.Text.Json` — надёжное извлечение `tag_name` и `assets[0].browser_download_url`
- **Лимит загрузки**: добавлено ограничение 100 MiB для скачиваемого файла релиза (с проверкой `Content-Length` через `ResponseHeadersRead`)
- **Мёртвый код удалён**:
  - Удалено поле `_httpClient` (`HttpClient`) — не использовалось, `FetchLatestReleaseAsync()` создаёт свой локальный клиент
  - Удалены фейковые задержки `await Task.Delay(100)` в проверке обновлений
  - Удалена опция `StripComments` — комментарии безусловно отсекаются на уровне лексера, настройка не имела эффекта

### Технические детали
- `LunarGuard.sln`: добавлен проект `LunarGuard.GUI`
- `MainWindow.xaml`: полный UI (2 вкладки, карточки настроек, FAQ, анимации)
- `MainWindow.xaml.cs`: все обработчики (переключение вкладок, выбор файла, запуск, проверка обновлений с SHA256)
- `Themes/Styles.xaml`: кастомные стили (NavItem, WinCtrlBtn, UpdateBtn, AccentBtn, SettingCard, FaqCard, Checkbox, DarkTextBox)
- Все 53 теста проходят. 0 предупреждений сборки.

## [1.0.0] — 2026-07-05

### Добавлено
- CLI-интерфейс на Spectre.Console
- 8 проходов обфускации: RenamePass, DeadCodePass, StringEncryptPass, ControlFlowPass, ExpressionSplitPass, AntiDebugPass, NumberEncodePass, VirtualizationPass
- Bytecode VM: трансляция Lua 5.1 AST → виртуальные инструкции + runtime-интерпретатор
- Полный парсер Lua 5.1 (лексер + рекурсивный спуск)
- AST для Lua 5.1 с поддержкой всех конструкций
- Генератор кода `LuaWriter`
- 53 модульных теста (xUnit)
- README с документацией, примерами использования и road map
- Лицензия MIT
