#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Мои программы» (issue #91, Avalonia/Linux): список пользовательских программ
/// группы под кнопками запуска конфигуратора — добавление/удаление, перемещение
/// вверх-вниз, имя, исполняемый файл, аргументы, рабочая папка, иконка из файла
/// (<c>.ico</c>/<c>.png</c>) и переключатель отображения «списком / иконками».
/// После закрытия вызывающий код (MainViewModel) применяет режим и перечитывает список.
/// </summary>
public sealed class CustomProgramsWindow : ModalWindowBase
{
    /// <summary>
    /// Issue #158 (1.0.1.123): открытое окно редактора (не более одного). Второй клик
    /// по шестерёнке активирует его, а не создаёт дубль (у 7OH открывались два окна).
    /// </summary>
    private static CustomProgramsWindow? _openInstance;

    private readonly CustomProgramsViewModel _vm;
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly ListBox _list = new();

    /// <summary>
    /// Иконка программы (issue #91): галочка «Иконка из файла» → картинка из файла,
    /// иначе основная иконка исполняемого файла (на Linux — стандартная иконка группы).
    /// </summary>
    private static readonly Converters.CustomProgramIconConverter ProgramIconConverter = new();

    /// <summary>Видимость иконки: есть картинка либо её нет (параметр <c>Invert</c>).</summary>
    private static readonly Converters.CustomProgramIconVisibilityConverter ProgramIconVisibility = new();

    /// <param name="viewMode">Текущий режим отображения группы из настроек.</param>
    public CustomProgramsWindow(string? viewMode)
    {
        Title = T("Launch.CustomPrograms.Title");
        Width = 900;
        // Issue #159 (1.0.1.123): окно растягивается по содержимому формы
        // (SizeToContent.Height) — постоянная внешняя прокрутка на Linux исчезает.
        // Список программ прокручивается своим ListBox. MaxHeight/MinHeight
        // уточняются в ApplyWorkAreaLimits по рабочей области.
        SizeToContent = Avalonia.Controls.SizeToContent.Height;
        MinWidth = 760;
        MinHeight = DialogWorkAreaPolicy.CustomPrograms.MinHeight;
        FontSize = 13;
        _vm = new CustomProgramsViewModel(AppServices.TryGetService<ICustomProgramsStore>(), viewMode);
        DataContext = _vm;
        Content = BuildRoot();
        // issue #146 (1.0.1.106): высота окна не должна выходить за рабочую область.
        ApplyWorkAreaLimits();
    }

    /// <summary>
    /// Issue #146 (1.0.1.118): рекомендованные размеры окна берутся из профиля
    /// <see cref="DialogWorkAreaPolicy.CustomPrograms"/>. Issue #178 (1.0.1.129):
    /// профиль увеличен до 700 DIP — после добавления блока «Запускать от имени
    /// администратора» прежняя высота не вмещала форму и давала прокрутку при открытии.
    /// Окно растягивается по содержимому (<c>SizeToContent.Height</c>), а на маленьком
    /// экране клампится по рабочей области (прокрутка остаётся аварийным вариантом).
    /// </summary>
    private void ApplyWorkAreaLimits()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;
        var dipHeight = screen.WorkingArea.Height / screen.Scaling;
        var size = DialogWorkAreaPolicy.Recommend(
            DialogWorkAreaPolicy.CustomPrograms,
            dipHeight);
        // Issue #159: высота — по содержимому формы (SizeToContent.Height), Height не
        // задаём; MaxHeight клампится по рабочей области, а длинный список программ
        // прокручивается своим ListBox, не растягивая окно за экран.
        MaxHeight = size.MaxHeight;
        MinHeight = size.MinHeight;
        _list.MaxHeight = Math.Max(200, size.MaxHeight - 240);
    }

    /// <summary>Выбранный в окне режим отображения группы (для сохранения в настройках).</summary>
    public string ViewMode => _vm.ViewMode;

    /// <summary>Открыт ли редактор «Мои программы» прямо сейчас (issue #158).</summary>
    public static bool IsOpen => CustomProgramsEditorGuard.IsOpen;

    /// <summary>
    /// Issue #158 (1.0.1.123): единственная точка открытия редактора. Повторный вызов
    /// при уже открытом окне активирует существующее и НЕ создаёт второе. Слот
    /// освобождается в <c>finally</c> и в <see cref="OnClosed"/>, поэтому ошибка
    /// сохранения или удаления не «залипает» навсегда.
    /// </summary>
    /// <param name="owner">Окно-владелец (настройки или главное окно).</param>
    /// <param name="viewMode">Текущий режим отображения группы из настроек.</param>
    /// <returns>Открытое (или уже открытое) окно редактора — для чтения <see cref="ViewMode"/>.</returns>
    public static CustomProgramsWindow ShowEditor(Window? owner, string? viewMode)
    {
        if (CustomProgramsEditorGuard.IsOpen && _openInstance is { } existing)
        {
            ModalCloseTrace.Log(
                "CustomProgramsWindow.ShowEditor: окно уже открыто — активирую существующее");
            try { existing.Activate(); } catch { /* активация не критична */ }
            return existing;
        }

        var win = new CustomProgramsWindow(viewMode);
        if (!CustomProgramsEditorGuard.TryEnter())
        {
            // Гонка: слот заняли между проверкой и созданием. Второе окно не показываем.
            ModalCloseTrace.Log(
                "CustomProgramsWindow.ShowEditor: слот занят — второе окно не создаётся");
            return _openInstance ?? win;
        }

        _openInstance = win;
        try
        {
            win.ShowDialogSync(owner);
        }
        finally
        {
            if (ReferenceEquals(_openInstance, win))
                _openInstance = null;
            CustomProgramsEditorGuard.Exit();
        }
        return win;
    }

    /// <summary>
    /// Issue #158: снятие флага одного окна даже при закрытии окна в обход
    /// <see cref="ShowEditor"/> (например, прямым <c>ShowDialogSync</c> из старого кода).
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        // Слот освобождает только «своё» окно: закрытие постороннего экземпляра
        // не должно открывать дорогу второму редактору.
        if (ReferenceEquals(_openInstance, this))
        {
            _openInstance = null;
            CustomProgramsEditorGuard.Exit();
        }
        base.OnClosed(e);
    }

    /// <summary>Показывает окно модально (синхронно).</summary>
    public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

    private static string T(string key) => LocalizationManager.T(key);

    private Control BuildRoot()
    {
        var dock = new DockPanel { Margin = new Thickness(14) };

        // Нижняя панель: «Сохранить» слева, «Отмена» справа.
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = T("Common.Save"), MinWidth = 130 };
        ok.Click += (_, _) => SaveAndClose();
        // Issue #158 (1.0.1.121): «Отмена» закрывает окно штатно, без сохранения и без
        // установки DialogResult — окно закрывается даже после ошибки сохранения.
        // 1.0.1.123: закрытие идёт через CloseSafely — сбой в цепочке закрытия
        // (в т.ч. при перестройке шаблона списка) не оставляет окно на экране.
        var cancel = new Button { Content = T("Common.Cancel"), MinWidth = 110 };
        cancel.Click += (_, _) => CloseSafely();
        foreach (var b in new Control[] { ok, cancel })
        {
            b.Styled(ControlThemes.ModernButton);
            bottom.Children.Add(b);
        }
        DockPanel.SetDock(bottom, Dock.Bottom);

        // Переключатель режима отображения группы.
        var modePanel = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 10, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = T("Launch.CustomPrograms.ViewModeHeader"),
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    ModeRadio("Launch.CustomPrograms.ViewModeList", "IsListViewMode"),
                    ModeRadio("Launch.CustomPrograms.ViewModeIcons", "IsIconsViewMode")
                }
            }
        };
        ThemeBrushes.Bind(modePanel, Border.BackgroundProperty, "CardBackgroundBrush");
        ThemeBrushes.Bind(modePanel, Border.BorderBrushProperty, "BorderColorBrush");
        DockPanel.SetDock(modePanel, Dock.Bottom);

        dock.Children.Add(bottom);
        dock.Children.Add(modePanel);
        // Issue #146 (1.0.1.115): внешнего ScrollViewer больше нет — прокручивается
        // только правая форма, а список программ прокручивает свой ListBox.
        dock.Children.Add(BuildEditorGrid());
        return dock;
    }

    private RadioButton ModeRadio(string textKey, string property)
    {
        var radio = new RadioButton
        {
            Content = T(textKey),
            GroupName = "CustomProgramsView",
            VerticalAlignment = VerticalAlignment.Center
        };
        radio.Bind(ToggleButton.IsCheckedProperty,
            new Binding(property) { Mode = BindingMode.TwoWay });
        return radio;
    }

    private Control BuildEditorGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Левая колонка: список программ и кнопки операций.
        var left = new DockPanel();
        var ops = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 8, 0, 0)
        };
        ops.Children.Add(IconButton("IconPlus", "Common.Add", () => _vm.AddProgram()));
        // Удаление программы — опасное действие: DangerButton (P2-1).
        // Issue #158 (1.0.1.123): удаление не должно бросать исключение наружу —
        // ошибка хранилища/иконки логируется, окно остаётся рабочим и закрывается.
        ops.Children.Add(IconButton("IconDelete", "Common.Delete", DeleteSelectedSafe,
            ControlThemes.DangerButton, "DangerTextBrush"));
        ops.Children.Add(IconButton("IconArrowUp", "Launch.CustomPrograms.MoveUp", () => _vm.MoveSelected(-1)));
        ops.Children.Add(IconButton("IconArrowDown", "Launch.CustomPrograms.MoveDown", () => _vm.MoveSelected(1)));
        DockPanel.SetDock(ops, Dock.Bottom);
        left.Children.Add(ops);

        // Issue #158: источник списка редактора — то же свойство VM, что и у WPF
        // (Programs). Раньше здесь стояло несуществующее «CustomPrograms», из-за чего
        // строка добавленной программы в редакторе не появлялась.
        _list.Bind(ItemsControl.ItemsSourceProperty, new Binding("Programs"));
        _list.Bind(ListBox.SelectedItemProperty, new Binding("SelectedProgram") { Mode = BindingMode.TwoWay });
        // Issue #159 (1.0.1.128): список растягивается по своей колонке, горизонтальная
        // прокрутка выключена — ширина списка не зависит от длины названий программ.
        _list.HorizontalAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        // Issue #158 (1.0.1.123): ContentPresenter может построить строку и для
        // null-элемента (удаление/пересоздание контейнера). Раньше BuildListItem
        // обращался к program.Name и падал NullReferenceException прямо во время
        // удаления/закрытия окна — из-за этого окно «не закрывалось» вообще.
        _list.ItemTemplate = new FuncDataTemplate<CustomProgram>(
            (program, _) => BuildListItem(program));
        left.Children.Add(_list);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        // Правая колонка: поля выбранной программы.
        var form = new StackPanel { Spacing = 4 };
        form.Bind(Control.IsEnabledProperty, new Binding("HasSelection"));

        form.Children.Add(FieldLabel("Launch.CustomPrograms.Name"));
        form.Children.Add(Field("SelectedProgram.Name"));

        form.Children.Add(FieldLabel("Launch.CustomPrograms.File"));
        form.Children.Add(BrowseRow("SelectedProgram.FilePath", "Common.Browse", BrowseFile));

        form.Children.Add(FieldLabel("Launch.CustomPrograms.Arguments"));
        form.Children.Add(Field("SelectedProgram.Arguments"));

        form.Children.Add(FieldLabel("Launch.CustomPrograms.WorkingDirectory"));
        form.Children.Add(BrowseRow("SelectedProgram.WorkingDirectory", "Common.Browse", BrowseWorkingDirectory));

        form.Children.Add(FieldLabel("Launch.CustomPrograms.Icon"));

        // Галочка «Иконка из файла» (issue #91): без неё показывается основная иконка
        // исполняемого файла (как в проводнике), а выбор/сброс файла недоступны.
        var useCustomIcon = new CheckBox
        {
            Content = T("Launch.CustomPrograms.UseCustomIcon"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        useCustomIcon.Bind(ToggleButton.IsCheckedProperty,
            new Binding("SelectedProgram.UseCustomIcon") { Mode = BindingMode.TwoWay });
        ToolTip.SetTip(useCustomIcon, T("Launch.CustomPrograms.UseCustomIconTooltip"));
        form.Children.Add(useCustomIcon);

        form.Children.Add(BuildIconRow());

        // Issue #178: запуск от имени администратора. Пароль в приложении не хранится —
        // его сохраняет Windows при первом запуске (runas /savecred).
        var runAs = new CheckBox
        {
            Content = T("Launch.CustomPrograms.RunAsAdministrator"),
            Margin = new Thickness(0, 4, 0, 4)
        };
        runAs.Bind(ToggleButton.IsCheckedProperty,
            new Binding("SelectedProgram.RunAsAdministrator") { Mode = BindingMode.TwoWay });
        ToolTip.SetTip(runAs, T("Launch.CustomPrograms.RunAsAdministratorTooltip"));
        form.Children.Add(runAs);

        form.Children.Add(FieldLabel("Launch.CustomPrograms.RunAsUserName"));
        var runAsUser = Field("SelectedProgram.RunAsUserName");
        runAsUser.Bind(Control.IsEnabledProperty,
            new Binding("SelectedProgram.RunAsAdministrator"));
        ToolTip.SetTip(runAsUser, T("Launch.CustomPrograms.RunAsUserNameTooltip"));
        form.Children.Add(runAsUser);

        var enabled = new CheckBox { Content = T("Launch.CustomPrograms.Enabled"), Margin = new Thickness(0, 6, 0, 0) };
        enabled.Bind(ToggleButton.IsCheckedProperty,
            new Binding("SelectedProgram.Enabled") { Mode = BindingMode.TwoWay });
        form.Children.Add(enabled);

        // Правая колонка (issue #146, 1.0.1.118): пояснение — последний элемент
        // прокручиваемого содержимого, сразу за формой. Оно вне IsEnabled-группы полей
        // и не сереет без выбора (не наследует HasSelection).
        var hint = new TextBlock
        {
            Text = T("Launch.CustomPrograms.Hint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = UiMetrics.ScaledFont(12),
            Margin = new Thickness(0, 14, 0, 0)
        };
        ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");

        var scrollContent = new StackPanel();
        scrollContent.Children.Add(form);
        scrollContent.Children.Add(hint);

        var right = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = scrollContent
        };
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>
    /// Строка списка программ. Issue #158 (1.0.1.123): <paramref name="program"/> может
    /// быть <c>null</c> — <c>ContentPresenter</c> вызывает шаблон и с пустым содержимым
    /// (удаление элемента, пересоздание контейнера, закрытие окна). Возвращаем пустой
    /// контрол вместо исключения; любые сбои построения строки тоже не выходят наружу.
    /// </summary>
    private Control BuildListItem(CustomProgram? program)
    {
        if (program is null)
            return new Panel();

        try
        {
            return BuildListItemCore(program);
        }
        catch (Exception ex)
        {
            LogWindowError(
                $"Не удалось построить строку программы «{program.Name}».", ex);
            return new TextBlock
            {
                Text = program.Name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
        }
    }

    private Control BuildListItemCore(CustomProgram program)
    {
        var iconImage = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) };
        iconImage.Bind(Image.SourceProperty, new MultiBinding
        {
            Converter = ProgramIconConverter,
            Bindings =
            {
                new Binding(nameof(CustomProgram.UseCustomIcon)),
                new Binding(nameof(CustomProgram.IconPath)),
                new Binding(nameof(CustomProgram.FilePath))
            }
        });
        iconImage.Bind(Control.IsVisibleProperty, new MultiBinding
        {
            Converter = ProgramIconVisibility,
            Bindings =
            {
                new Binding(nameof(CustomProgram.UseCustomIcon)),
                new Binding(nameof(CustomProgram.IconPath)),
                new Binding(nameof(CustomProgram.FilePath))
            }
        });

        var fallback = IconHelper.MakeIcon("IconApplication", UiMetrics.IconToolbar, "WarningBrush");
        fallback.Bind(Control.IsVisibleProperty, new MultiBinding
        {
            Converter = ProgramIconVisibility,
            ConverterParameter = "Invert",
            Bindings =
            {
                new Binding(nameof(CustomProgram.UseCustomIcon)),
                new Binding(nameof(CustomProgram.IconPath)),
                new Binding(nameof(CustomProgram.FilePath))
            }
        });

        // Issue #158 (1.0.1.123): имя привязано к модели, а не скопировано в Text.
        // CustomProgram реализует INotifyPropertyChanged, поэтому переименование
        // в форме сразу видно в списке (раньше — только после переоткрытия окна).
        var name = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        name.Bind(TextBlock.TextProperty, new Binding(nameof(CustomProgram.Name)));

        var iconHost = new Grid
        {
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { iconImage, fallback }
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(2),
            Children = { iconHost, name }
        };
        return row;
    }

    private Control FieldLabel(string textKey)
    {
        var label = new TextBlock
        {
            Text = T(textKey),
            Margin = new Thickness(0, 6, 0, 2)
        };
        ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        return label;
    }

    private TextBox Field(string path)
    {
        var box = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        box.Bind(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay });
        return box;
    }

    private Control BrowseRow(string path, string buttonKey, Action browse)
    {
        var browseButton = new Button { Content = T(buttonKey), Padding = new Thickness(10, 5) };
        browseButton.Styled(ControlThemes.ModernButton);
        browseButton.Click += (_, _) => browse();

        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(browseButton, Dock.Right);
        browseButton.Margin = new Thickness(6, 0, 0, 0);
        dock.Children.Add(browseButton);
        dock.Children.Add(Field(path));
        return dock;
    }

    private Control BuildIconRow()
    {
        var preview = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0) };
        preview.Bind(Image.SourceProperty, new MultiBinding
        {
            Converter = ProgramIconConverter,
            Bindings =
            {
                new Binding("SelectedProgram.UseCustomIcon"),
                new Binding("SelectedProgram.IconPath"),
                new Binding("SelectedProgram.FilePath")
            }
        });

        var pathBox = new TextBox { IsReadOnly = true };
        pathBox.Bind(TextBox.TextProperty, new Binding("SelectedProgram.IconPath"));

        var pick = new Button { Content = T("Launch.CustomPrograms.PickIcon"), Padding = new Thickness(10, 5) };
        pick.Styled(ControlThemes.ModernButton);
        pick.Click += (_, _) => BrowseIcon();
        var clear = new Button { Content = T("Launch.CustomPrograms.ClearIcon"), Padding = new Thickness(10, 5) };
        clear.Styled(ControlThemes.ModernButton);
        clear.Click += (_, _) => _vm.ClearSelectedIcon();

        // Выбор и сброс файла доступны только при включённой галочке «Иконка из файла».
        var dock = new DockPanel();
        dock.Bind(Control.IsEnabledProperty, new Binding("SelectedProgram.UseCustomIcon"));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(6, 0, 0, 0),
            Children = { pick, clear }
        };
        DockPanel.SetDock(buttons, Dock.Right);
        dock.Children.Add(buttons);

        var textHost = new DockPanel();
        DockPanel.SetDock(preview, Dock.Left);
        textHost.Children.Add(preview);
        textHost.Children.Add(pathBox);
        dock.Children.Add(textHost);
        return dock;
    }

    private Button IconButton(string iconKey, string tooltipKey, Action action,
        string style = ControlThemes.ModernButton, string iconBrush = "TextPrimaryBrush")
    {
        var button = new Button
        {
            Content = IconHelper.MakeIcon(iconKey, 16, iconBrush),
            Padding = new Thickness(8, 5)
        };
        button.Styled(style);
        ToolTip.SetTip(button, T(tooltipKey));
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>
    /// «Сохранить»: запись списка и закрытие окна только при успехе.
    /// Issue #158 (1.0.1.121): ошибка сохранения (в том числе ввода-вывода на Linux)
    /// показывается сообщением, окно остаётся открытым и закрывается кнопкой «Отмена»;
    /// исключение записи не выходит наружу и не блокирует закрытие.
    /// 1.0.1.123: закрытие — через <see cref="CloseSafely"/>, чтобы сбой при перестройке
    /// списка не оставлял окно на экране.
    /// </summary>
    private void SaveAndClose()
    {
        if (!_vm.TrySave(out var error))
        {
            ShowSaveError(error);
            return;
        }
        DialogResult = true;
        CloseSafely();
    }

    /// <summary>
    /// Удаление выбранной программы. Issue #158 (1.0.1.123): исключение не выходит
    /// наружу (иначе вместо удаления показывалось необработанное «Ошибка интерфейса»),
    /// а пишется в журнал; окно остаётся рабочим и закрывается штатно.
    /// </summary>
    private void DeleteSelectedSafe()
    {
        try
        {
            _vm.DeleteSelected();
        }
        catch (Exception ex)
        {
            LogWindowError("Не удалось удалить программу из списка.", ex);
        }
    }

    /// <summary>
    /// Issue #158 (1.0.1.123): гарантированное закрытие окна. Если синхронное
    /// <c>Close()</c> сорвалось (например, исключение в обработчике шаблона строки),
    /// окно закрывается ещё раз из очереди UI-потока, а ошибка попадает в журнал.
    /// </summary>
    private void CloseSafely()
    {
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            LogWindowError("Не удалось закрыть окно «Мои программы».", ex);
            Dispatcher.UIThread.Post(() =>
            {
                try { Close(); }
                catch { /* окно уже закрыто или не открывалось */ }
            });
        }
    }

    /// <summary>Запись ошибки окна в журнал; недоступность журнала не ломает UI.</summary>
    private static void LogWindowError(string message, Exception ex)
    {
        try
        {
            AppServices.TryGetService<IAppLogger>()?.Error($"[custom-programs] {message}", ex);
        }
        catch
        {
            // Журнал не должен ломать сценарий закрытия/удаления.
        }
    }

    /// <summary>Показ понятного сообщения об ошибке сохранения (issue #158).</summary>
    private void ShowSaveError(string error)
    {
        try
        {
            _dialogs.ShowWarning(error, T("Launch.CustomPrograms.SaveFailedTitle"));
        }
        catch (Exception ex)
        {
            try
            {
                AppServices.TryGetService<IAppLogger>()?.Error(
                    "[custom-programs] Не удалось показать сообщение об ошибке сохранения.", ex);
            }
            catch
            {
                // Журнал не должен ломать сценарий закрытия окна.
            }
        }
    }

    private void BrowseFile()
    {
        if (_vm.SelectedProgram is not { } program)
            return;
        var path = _dialogs.OpenFileDialog(
            T("Launch.CustomPrograms.FileDialogTitle"),
            T("Launch.CustomPrograms.FileDialogFilter"),
            SafeDirectory(program.FilePath));
        if (!string.IsNullOrWhiteSpace(path))
            program.FilePath = path!;
    }

    private void BrowseWorkingDirectory()
    {
        if (_vm.SelectedProgram is not { } program)
            return;
        var path = _dialogs.OpenFolderDialog(
            T("Launch.CustomPrograms.WorkingDirectoryDialogTitle"),
            SafeDirectory(program.WorkingDirectory));
        if (!string.IsNullOrWhiteSpace(path))
            program.WorkingDirectory = path!;
    }

    private void BrowseIcon()
    {
        if (_vm.SelectedProgram is not { } program)
            return;
        var path = _dialogs.OpenFileDialog(
            T("Launch.CustomPrograms.IconDialogTitle"),
            T("Launch.CustomPrograms.IconDialogFilter"),
            SafeDirectory(program.IconPath));
        if (!string.IsNullOrWhiteSpace(path))
            program.IconPath = path!;
    }

    /// <summary>Каталог для начального выбора в диалоге (существующий, иначе пусто).</summary>
    private static string? SafeDirectory(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var dir = System.IO.Path.GetDirectoryName(path);
            return !string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir) ? dir : null;
        }
        catch
        {
            return null;
        }
    }
}
#endif
