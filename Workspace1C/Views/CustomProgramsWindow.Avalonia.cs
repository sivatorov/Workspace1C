#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
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
        // Issue #146 (1.0.1.116): высота и минимум — из профиля CustomPrograms (760 DIP).
        Height = DialogWorkAreaPolicy.CustomPrograms.DefaultHeight;
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
    /// Issue #146 (1.0.1.116): рекомендованные размеры окна берутся из профиля
    /// <see cref="DialogWorkAreaPolicy.CustomPrograms"/> — высота 760 DIP помещает форму
    /// и пояснение без прокрутки, а на маленьком экране клампится по рабочей области
    /// (прокрутка остаётся аварийным вариантом).
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
        MaxHeight = size.MaxHeight;
        MinHeight = size.MinHeight;
        Height = size.Height;
    }

    /// <summary>Выбранный в окне режим отображения группы (для сохранения в настройках).</summary>
    public string ViewMode => _vm.ViewMode;

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
        var cancel = new Button { Content = T("Common.Cancel"), MinWidth = 110 };
        cancel.Click += (_, _) => Close();
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
        ops.Children.Add(IconButton("IconDelete", "Common.Delete", () => _vm.DeleteSelected(),
            ControlThemes.DangerButton, "DangerTextBrush"));
        ops.Children.Add(IconButton("IconArrowUp", "Launch.CustomPrograms.MoveUp", () => _vm.MoveSelected(-1)));
        ops.Children.Add(IconButton("IconArrowDown", "Launch.CustomPrograms.MoveDown", () => _vm.MoveSelected(1)));
        DockPanel.SetDock(ops, Dock.Bottom);
        left.Children.Add(ops);

        _list.Bind(ItemsControl.ItemsSourceProperty, new Binding("CustomPrograms"));
        _list.Bind(ListBox.SelectedItemProperty, new Binding("SelectedProgram") { Mode = BindingMode.TwoWay });
        _list.ItemTemplate = new FuncDataTemplate<CustomProgram>((program, _) => BuildListItem(program));
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

        var enabled = new CheckBox { Content = T("Launch.CustomPrograms.Enabled"), Margin = new Thickness(0, 6, 0, 0) };
        enabled.Bind(ToggleButton.IsCheckedProperty,
            new Binding("SelectedProgram.Enabled") { Mode = BindingMode.TwoWay });
        form.Children.Add(enabled);

        // Правая колонка (issue #146, 1.0.1.115): пояснение закреплено внизу и видно
        // всегда; прокручиваются только поля формы (аварийный вариант для малых экранов).
        var right = new DockPanel();
        var hint = new TextBlock
        {
            Text = T("Launch.CustomPrograms.Hint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = UiMetrics.ScaledFont(12),
            Margin = new Thickness(0, 14, 0, 0)
        };
        ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        DockPanel.SetDock(hint, Dock.Bottom);
        right.Children.Add(hint);
        right.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = form
        });
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return grid;
    }

    private Control BuildListItem(CustomProgram program)
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

        var name = new TextBlock
        {
            Text = program.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

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

    private void SaveAndClose()
    {
        if (!_vm.TrySave(out var error))
        {
            _dialogs.ShowWarning(error, T("Launch.CustomPrograms.Title"));
            return;
        }
        DialogResult = true;
        Close();
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
