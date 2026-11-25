#if LINUX
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно создания/редактирования сценария запуска скрипта (Avalonia/Linux, issue #308).
/// Параметры задаются строками (каждая строка — отдельный параметр); двойной клик
/// по токену подстановки вставляет его в позицию курсора поля параметров.
/// </summary>
public sealed class ScriptScenarioEditWindow : ModalWindowBase
{
    private readonly ScriptScenarioEditViewModel _vm;
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly System.Collections.Generic.List<Infobase> _infobases = new();

    /// <summary>Контекст запуска платформы для «Базы для примера» (issue #67, производительность):
    /// вычисляется один раз на выбранную базу и переиспользуется в пересчётах превью —
    /// резолвер платформ больше не вызывается на каждое нажатие клавиши.</summary>
    private ScriptLaunchContext? _exampleLaunchContext;

    /// <summary>Дебаунс пересчёта превью (issue #67): короткая пауза после ввода в поле —
    /// сборка командной строки не выполняется на каждый символ.</summary>
    private readonly DispatcherTimer _previewDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    /// <summary>
    /// Редактируемый сценарий (issue #308): при подтверждении поля применяются к нему же,
    /// чтобы Store.Save по тому же Id перезаписал файл, а не создавал копию. null — новый.
    /// </summary>
    private readonly ScriptScenario? _sourceScenario;

    /// <summary>База, из которой запущен сценарий (issue #67): становится «Базой для примера
    /// подстановок» в редакторе; null — первая в списке (запуск из «Утилит»/без выделения).</summary>
    private readonly Infobase? _initialBase;

    private readonly TextBox _nameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _filePathBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _workingDirectoryBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _parametersBox = new TextBox
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 90,
        MaxHeight = 150
    }.Styled(ControlThemes.ModernTextBox);
    private readonly ListBox _tokensList = new();
    private readonly ComboBox _shellCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly ComboBox _exampleBaseCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly TextBox _previewBox = new TextBox { IsReadOnly = true }.Styled(ControlThemes.ModernTextBox);
    private readonly CheckBox _hideWindowCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    // Issue #308: «Уведомлять о запуске» (окно «скрипт запущен» + системные уведомления)
    // и «Не закрывать» (хвост pause/Read-Host/read) — новые галки редактора сценария.
    private readonly CheckBox _notifyOnStartCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly CheckBox _keepOpenCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    /// <summary>Корневой контейнер окна — для измерения при подгонке высоты (issue #308).</summary>
    private StackPanel? _rootPanel;

    /// <summary>Готовый сценарий при подтверждении, иначе <c>null</c>.</summary>
    public ScriptScenario? Result { get; private set; }

    /// <param name="scenario">Редактируемый сценарий или <c>null</c> для нового.</param>
    /// <param name="initialBase">База запуска сценария — «База для примера подстановок»
    /// по умолчанию (issue #67, п.4); <c>null</c> — первая в списке.</param>
    public ScriptScenarioEditWindow(ScriptScenario? scenario = null, Infobase? initialBase = null)
    {
        _previewDebounceTimer.Tick += (_, _) =>
        {
            _previewDebounceTimer.Stop();
            UpdatePreview();
        };
        _sourceScenario = scenario;
        _initialBase = initialBase;
        _vm = new ScriptScenarioEditViewModel(scenario);
        Title = T(scenario is null ? "Script.AddTitle" : "Script.EditTitle");
        // Issue #119 (T3): целевой стартовый размер из таблицы 1.4 (паритет с WPF).
        Width = 660;
        Height = 680;
        MinWidth = 560;
        MinHeight = 560;
        // Явная высота вместо SizeToContent.Height: авторазмер мог схлопывать окно в пустой
        // прямоугольник при модальном показе (регресс «пустого незакрываемого окна», issue #308).
        // Окно чуть выше (issue #308): не всё влезало без скролла; после показа высота
        // подгоняется под содержимое и ограничивается рамками экрана через WindowSizeMath
        // (FitHeightToContent). Контент при необходимости прокручивается внутренним
        // ScrollViewer; MaxHeight ограничивает рост при ручном изменении размера,
        // MinHeight — слишком сильное сжатие.
        MaxHeight = 800;
        CanResize = true;
        FontSize = 13;
        Content = BuildRoot();

        // Фокус в поле «Наименование» (issue #299): отложенно после показа окна —
        // синхронная установка в Opened слетает до активации модального диалога.
        Opened += (_, _) =>
        {
            // Переустановка выбора первой базы после показа окна (issue #308, п.14:37):
            // повторный SelectedIndex пересоздаёт контейнер с применённым ItemTemplate,
            // исключая отрисовку ToString() (имя типа Infobase) в закрытом комбобоксе.
            if (_exampleBaseCombo.SelectedItem is not Infobase && _infobases.Count > 0)
            {
                // Issue #67 (п.4): «База для примера» по умолчанию — база, из которой
                // запущен сценарий (её Id передан в окно); при запуске из «Утилит» или
                // при отсутствии совпадения — первая в списке (прежнее поведение).
                var preset = _initialBase is { } ib
                    ? _infobases.FirstOrDefault(b => string.Equals(b.Id, ib.Id, StringComparison.OrdinalIgnoreCase))
                    : null;
                _exampleBaseCombo.SelectedIndex = preset is not null ? _infobases.IndexOf(preset) : 0;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _nameBox.Focus();
                if (scenario is null)
                    _nameBox.SelectAll();
            }, Avalonia.Threading.DispatcherPriority.Background);

            // Подгонка высоты окна под содержимое с ограничением рамками экрана
            // (WindowSizeMath, issue #308): отложенно после отрисовки, чтобы
            // измерения контента были актуальны.
            Avalonia.Threading.Dispatcher.UIThread.Post(
                FitHeightToContent, Avalonia.Threading.DispatcherPriority.Background);
        };
    }

    /// <summary>
    /// Подгоняет высоту окна под содержимое (issue #308): измеряет корневой контейнер
    /// при бесконечной высоте, клампит в [MinHeight, MaxHeight] через
    /// <see cref="WindowSizeMath.ClampHeight"/> и поднимает окно, если низ уходит
    /// за нижний край рабочей области (<see cref="WindowSizeMath.FitTop"/>).
    /// Логика расчёта — чистый WindowSizeMath (общий с WPF).
    /// </summary>
    private void FitHeightToContent()
    {
        if (_rootPanel is null || !IsVisible)
            return;

        var availableWidth = _rootPanel.Bounds.Width > 0 ? _rootPanel.Bounds.Width : Math.Max(400, Width);
        _rootPanel.Measure(new Avalonia.Size(availableWidth, double.PositiveInfinity));
        var desired = _rootPanel.DesiredSize.Height;
        if (desired <= 0)
            return;

        // Хром (заголовок + рамки/декор) — разница между полной высотой и клиентской областью.
        var chrome = Math.Max(0, ClientSize.Height - (_rootPanel.Bounds.Height > 0 ? _rootPanel.Bounds.Height : desired));
        Height = WindowSizeMath.ClampHeight(desired + chrome, MinHeight, MaxHeight);

        // Окно стояло у нижнего края экрана и выросло — поднимаем его, чтобы нижняя
        // часть не уходила за экран (issue #308). Координаты в физических пикселях.
        if (Screens.ScreenFromWindow(this) is { } screen)
        {
            var wa = screen.WorkingArea;
            var heightPx = (int)(Height * screen.Scaling);
            var newTopPx = (int)WindowSizeMath.FitTop(Position.Y, heightPx, wa.Y, wa.Bottom);
            if (newTopPx != Position.Y)
                Position = new PixelPoint(Position.X, newTopPx);
        }
    }

    /// <summary>Показывает окно модально (синхронно).</summary>
    public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

    private static string T(string key) => LocalizationManager.T(key);

    private static Control Label(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        // Issue #61: подпись поля формы — единая кисть FormLabelBrush (не вторичная
        // и не фиксированный тёмный цвет), читаема в обеих темах и схемах.
        ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "FormLabelBrush");
        return label;
    }

    private static Control HintLabel(string text)
    {
        var hint = new TextBlock { Text = text, FontSize = 11 };
        ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        return hint;
    }

    /// <summary>Локализованная подпись интерпретатора для выпадающего списка
    /// (issue #308, п.9): «Авто» локализуется, имена инструментов фиксированы.</summary>
    private static string ShellDisplayName(ScriptShell shell) => shell switch
    {
        ScriptShell.Cmd => "cmd",
        ScriptShell.PowerShell => "PowerShell",
        ScriptShell.Sh => "sh",
        _ => T("Script.Shell.Auto")
    };

    private Control BuildRoot()
    {
        var panel = new StackPanel { Margin = new Avalonia.Thickness(14), Spacing = 8 };
        // Ссылка на корневой контейнер для подгонки высоты окна под содержимое
        // (WindowSizeMath, issue #308).
        _rootPanel = panel;

        panel.Children.Add(Label(T("Script.Name")));
        _nameBox.Text = _vm.Name;
        panel.Children.Add(_nameBox);

        panel.Children.Add(Label(T("Script.FilePath")));
        var pathRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(8) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        _filePathBox.Text = _vm.FilePath;
        _filePathBox.TextChanged += (_, _) => SchedulePreview();
        Grid.SetColumn(_filePathBox, 0);
        pathRow.Children.Add(_filePathBox);
        var browse = new Button { Content = T("Script.Browse"), Width = 96 }.Styled(ControlThemes.SecondaryButton);
        browse.Click += (_, _) => BrowseFile();
        Grid.SetColumn(browse, 2);
        pathRow.Children.Add(browse);
        panel.Children.Add(pathRow);

        // Issue #308, п.7: «Папка запуска» — рабочий каталог процесса скрипта
        // (WorkingDirectory); пусто — наследуется каталог приложения.
        panel.Children.Add(Label(T("Script.WorkingDirectory")));
        var workingDirectoryRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(8) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        _workingDirectoryBox.Text = _vm.WorkingDirectory;
        _workingDirectoryBox.TextChanged += (_, _) => SchedulePreview();
        Grid.SetColumn(_workingDirectoryBox, 0);
        workingDirectoryRow.Children.Add(_workingDirectoryBox);
        var browseWorkingDirectory = new Button { Content = T("Script.Browse"), Width = 96 }.Styled(ControlThemes.SecondaryButton);
        browseWorkingDirectory.Click += (_, _) => BrowseWorkingDirectory();
        Grid.SetColumn(browseWorkingDirectory, 2);
        workingDirectoryRow.Children.Add(browseWorkingDirectory);
        panel.Children.Add(workingDirectoryRow);

        // Issue #308, п.9: «Интерпретатор» — выбор shell запуска сценария
        // (Авто / cmd / PowerShell / sh). Значения enum отображаются локализованными
        // подписями через KeyValuePair и ItemTemplate (как у _exampleBaseCombo).
        panel.Children.Add(Label(T("Script.Shell")));
        var shellItems = ScriptScenarioEditViewModel.ShellOptions
            .Select(s => new KeyValuePair<ScriptShell, string>(s, ShellDisplayName(s)))
            .ToList();
        _shellCombo.ItemsSource = shellItems;
        _shellCombo.ItemTemplate = new FuncDataTemplate<KeyValuePair<ScriptShell, string>>((pair, _) =>
            new TextBlock { Text = pair.Value });
        _shellCombo.SelectedItem = shellItems.FirstOrDefault(p => p.Key == _vm.Shell);
        _shellCombo.HorizontalAlignment = HorizontalAlignment.Left;
        _shellCombo.MinWidth = 220;
        _shellCombo.VerticalAlignment = VerticalAlignment.Center;
        _shellCombo.SelectionChanged += (_, _) => UpdatePreview();
        // Issue #308, п.14:39: галки «Скрывать окно скрипта» и «Уведомлять о запуске» —
        // в одной строке с «Интерпретатор», чтобы не терялись внизу окна. При выключенной
        // «Уведомлять о запуске» не показывается окно «скрипт запущен» и не шлются
        // системные уведомления (issue #308).
        _hideWindowCheck.Content = T("Script.HideWindow");
        _hideWindowCheck.IsChecked = _vm.HideWindow;
        _hideWindowCheck.VerticalAlignment = VerticalAlignment.Center;
        _notifyOnStartCheck.Content = T("Script.NotifyOnStart");
        _notifyOnStartCheck.IsChecked = _vm.NotifyOnStart;
        _notifyOnStartCheck.VerticalAlignment = VerticalAlignment.Center;
        var shellRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(12) },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(16) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        Grid.SetColumn(_shellCombo, 0);
        Grid.SetColumn(_hideWindowCheck, 2);
        Grid.SetColumn(_notifyOnStartCheck, 4);
        shellRow.Children.Add(_shellCombo);
        shellRow.Children.Add(_hideWindowCheck);
        shellRow.Children.Add(_notifyOnStartCheck);
        panel.Children.Add(shellRow);

        panel.Children.Add(Label(T("Script.Parameters")));
        _parametersBox.Text = _vm.ParametersText;
        _parametersBox.TextChanged += (_, _) => SchedulePreview();
        panel.Children.Add(_parametersBox);
        panel.Children.Add(HintLabel(T("Script.ParametersHint")));

        panel.Children.Add(Label(T("Script.Tokens")));
        // Токены подстановок: объекты ScriptTokenHint (issue #308) — в шаблоне списка
        // отображается «%token% — описание», двойной клик вставляет ТОЛЬКО токен.
        _tokensList.ItemsSource = ScriptScenarioEditViewModel.AvailableTokens
            .Select(t => new ScriptTokenHint(t.Token, t.LocalizationKey, T(t.LocalizationKey)))
            .ToList();
        _tokensList.ItemTemplate = new FuncDataTemplate<ScriptTokenHint>((hint, _) =>
        {
            var description = new TextBlock { Text = hint.Description ?? "" };
            ThemeBrushes.Bind(description, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = hint.Token, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "—" },
                    description
                }
            };
        });
        _tokensList.Height = 130;
        _tokensList.DoubleTapped += (_, _) => InsertTokenAtCaret();
        panel.Children.Add(_tokensList);
        // Issue #67: кнопка «Выбрать…» — слева от поясняющей надписи; открывает диалог
        // со ВСЕМИ переменными базы (имя — значение, поиск) и вставляет токен.
        var tokenRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        };
        var pickButton = new Button { Content = T("Script.PickButton"), Padding = new Avalonia.Thickness(10, 3) };
        pickButton.Styled(ControlThemes.SecondaryButton);
        pickButton.Click += (_, _) => OpenTokenPicker();
        tokenRow.Children.Add(pickButton);
        tokenRow.Children.Add(HintLabel(T("Script.TokensHint")));
        panel.Children.Add(tokenRow);

        panel.Children.Add(Label(T("Script.ExampleBase")));
        try
        {
            _infobases.AddRange(AppServices.GetRequiredService<IInfobaseRepository>().Load());
        }
        catch
        {
            // Окно редактирования работает и без списка баз — пример просто не покажется.
        }
        _exampleBaseCombo.ItemsSource = _infobases;
        // Отображение имени базы вместо имени типа (issue #308): Infobase не переопределяет
        // ToString(), поэтому имя выводим через ItemTemplate. Простой TextBlock без ручной
        // привязки кисти (цвет наследуется из темы окна) — по образцу MetadataExplorerWindow:
        // шаблон с ThemeBrushes.Bind мог не применяться к закрытому комбобоксу, и вместо
        // имени показывался ToString() элемента (регресс после 0.3.9.119).
        _exampleBaseCombo.ItemTemplate = new FuncDataTemplate<Infobase>((ib, _) =>
            new TextBlock { Text = ib.Name });
        // Выбор первой базы по индексу, как в WPF-версии и ConfigDiffSetupWindow: установка
        // SelectedItem объектом до показа окна не гарантирует отрисовку шаблона.
        if (_infobases.Count > 0)
            _exampleBaseCombo.SelectedIndex = 0;
        _exampleBaseCombo.HorizontalAlignment = HorizontalAlignment.Left;
        _exampleBaseCombo.MinWidth = 260;
        _exampleBaseCombo.SelectionChanged += (_, _) =>
        {
            // Сменилась «База для примера» — контекст платформы устарел (issue #67).
            _exampleLaunchContext = null;
            UpdatePreview();
        };
        // Issue #308: галка «Не закрывать» — рядом с «База для примера подстановок»:
        // добавляет к команде хвост удержания окна (pause/Read-Host/read).
        _keepOpenCheck.Content = T("Script.KeepOpen");
        _keepOpenCheck.IsChecked = _vm.KeepOpen;
        _keepOpenCheck.VerticalAlignment = VerticalAlignment.Center;
        // Хвост удержания виден в превью командной строки — пересчёт по клику (issue #308).
        _keepOpenCheck.IsCheckedChanged += (_, _) => UpdatePreview();
        var exampleRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(16) },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        Grid.SetColumn(_exampleBaseCombo, 0);
        Grid.SetColumn(_keepOpenCheck, 2);
        exampleRow.Children.Add(_exampleBaseCombo);
        exampleRow.Children.Add(_keepOpenCheck);
        panel.Children.Add(exampleRow);

        panel.Children.Add(HintLabel(T("Script.CommandLinePreview")));
        _previewBox.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_previewBox);

        // Issue #119 (T3): кнопки — вне прокрутки (футер Dock.Bottom), прокручивается
        // только длинная форма; горизонтальная полоса — fallback (issue #55).
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(14, 0, 14, 12)
        };
        var ok = new Button { Content = T("Common.Save"), Width = 90 }.Styled(ControlThemes.DialogConfirmButton);
        ok.Click += (_, _) => OkClicked();
        var cancel = new Button { Content = T("Common.Cancel"), Width = 90 }.Styled(ControlThemes.DialogCancelButton);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        bottom.Children.Add(ok);
        bottom.Children.Add(cancel);

        UpdatePreview();
        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        });
        return root;
    }

    private void BrowseFile()
    {
        var path = _dialogs.OpenFileDialog(T("Script.FilePath"));
        if (!string.IsNullOrWhiteSpace(path))
        {
            _filePathBox.Text = path;
            UpdatePreview();
        }
    }

    /// <summary>
    /// Выбор «Папки запуска» сценария (issue #308, п.7): открывает диалог выбора
    /// каталога; пустое значение — наследовать рабочий каталог приложения.
    /// </summary>
    private void BrowseWorkingDirectory()
    {
        var path = _dialogs.OpenFolderDialog(T("Script.WorkingDirectory"));
        if (!string.IsNullOrWhiteSpace(path))
        {
            _workingDirectoryBox.Text = path;
            UpdatePreview();
        }
    }

    private void InsertTokenAtCaret()
    {
        // Двойной клик вставляет ТОЛЬКО токен (issue #308), а не строку «%token% — описание».
        if (_tokensList.SelectedItem is not ScriptTokenHint hint || string.IsNullOrEmpty(hint.Token))
            return;
        var token = hint.Token;
        // Вставка токена в позицию курсора поля параметров (план #308: «двойной клик
        // по параметру вставляет его в позицию курсора поля параметров»).
        var text = _parametersBox.Text ?? "";
        var caret = _parametersBox.CaretIndex;
        if (caret > text.Length)
            caret = text.Length;
        _parametersBox.Text = text.Insert(caret, token);
        _parametersBox.CaretIndex = caret + token.Length;
        _parametersBox.Focus();
        UpdatePreview();
    }

    /// <summary>
    /// «Выбрать…» (issue #67): диалог со ВСЕМИ переменными базы (имя — значение, поиск);
    /// по «Ок»/двойному клику вставка выбранного токена в поле параметров.
    /// </summary>
    private void OpenTokenPicker()
    {
        var dialog = new ScriptTokenPickerWindow(SelectedExampleBase);
        if (!dialog.ShowDialogSync(this))
            return;
        var token = dialog.SelectedToken;
        if (string.IsNullOrWhiteSpace(token))
            return;

        var text = _parametersBox.Text ?? "";
        var caret = _parametersBox.CaretIndex;
        if (caret > text.Length)
            caret = text.Length;
        _parametersBox.Text = text.Insert(caret, token);
        _parametersBox.CaretIndex = caret + token.Length;
        _parametersBox.Focus();
        UpdatePreview();
    }

    private Infobase? SelectedExampleBase =>
        _exampleBaseCombo.SelectedItem is Infobase ib ? ib : _infobases.FirstOrDefault();

    /// <summary>Дебаунс пересчёта превью (issue #67): планирует обновление с задержкой
    /// 300 мс — сборка командной строки (резолвер платформы, экранирование) не выполняется
    /// на каждый символ ввода.</summary>
    private void SchedulePreview()
    {
        _previewDebounceTimer.Stop();
        _previewDebounceTimer.Start();
    }

    private void UpdatePreview()
    {
        _vm.Name = _nameBox.Text ?? "";
        _vm.FilePath = _filePathBox.Text ?? "";
        _vm.WorkingDirectory = _workingDirectoryBox.Text ?? "";
        _vm.ParametersText = _parametersBox.Text ?? "";
        // Выбранный интерпретатор (issue #308, п.9) попадает и в VM, и в превью.
        _vm.Shell = _shellCombo.SelectedItem is KeyValuePair<ScriptShell, string> pair ? pair.Key : _vm.Shell;
        // «Не закрывать» (issue #308) попадает и в VM, и в превью: хвост
        // pause/Read-Host/read виден в командной строке окна редактора.
        _vm.KeepOpen = _keepOpenCheck.IsChecked ?? false;
        var draft = new ScriptScenario
        {
            Name = _vm.Name,
            FilePath = _vm.FilePath,
            WorkingDirectory = _vm.WorkingDirectory,
            Parameters = _vm.NonEmptyParameters,
            Shell = _vm.Shell,
            KeepOpen = _vm.KeepOpen
        };
        // Issue #67: контекст платформы для выбранной «Базы для примера» вычисляется один
        // раз и переиспользуется во всех пересчётах превью (инвалидация — при смене базы).
        _exampleLaunchContext ??= ScriptLaunchContextResolver.Resolve(SelectedExampleBase);
        var preview = ScriptScenarioEditViewModel.BuildExampleCommandLine(draft, SelectedExampleBase, null, _exampleLaunchContext);
        _previewBox.Text = string.IsNullOrWhiteSpace(preview) ? "—" : preview;
    }

    private void OkClicked()
    {
        _vm.Name = _nameBox.Text ?? "";
        _vm.FilePath = _filePathBox.Text ?? "";
        _vm.WorkingDirectory = _workingDirectoryBox.Text ?? "";
        _vm.ParametersText = _parametersBox.Text ?? "";
        _vm.HideWindow = _hideWindowCheck.IsChecked ?? true;
        _vm.NotifyOnStart = _notifyOnStartCheck.IsChecked ?? false;
        _vm.KeepOpen = _keepOpenCheck.IsChecked ?? false;
        _vm.Shell = _shellCombo.SelectedItem is KeyValuePair<ScriptShell, string> pair ? pair.Key : ScriptShell.Auto;

        var errorKey = _vm.Validate();
        if (errorKey is not null)
        {
            _dialogs.ShowWarning(T(errorKey), Title ?? "");
            return;
        }

        // Issue #308: при редактировании поля применяются к переданному сценарию (Id сохраняется),
        // иначе создаётся новый — Store.Save по тому же Id перезапишет файл без дублей.
        if (_sourceScenario is not null)
        {
            _vm.ApplyTo(_sourceScenario);
            Result = _sourceScenario;
        }
        else
        {
            var created = new ScriptScenario();
            _vm.ApplyTo(created);
            Result = created;
        }
        DialogResult = true;
        Close();
    }
}
#endif