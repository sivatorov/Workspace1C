#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

    /// <summary>Путь к иконке → Bitmap (ошибки не роняют окно: возвращается null).</summary>
    private static readonly FuncValueConverter<string?, Bitmap?> IconConverter =
        new(path => LoadIcon(path));

    /// <summary>Пустая строка → true (показ стандартной иконки вместо файла).</summary>
    private static readonly FuncValueConverter<string?, bool> IsEmptyConverter =
        new(value => string.IsNullOrWhiteSpace(value));

    /// <param name="viewMode">Текущий режим отображения группы из настроек.</param>
    public CustomProgramsWindow(string? viewMode)
    {
        Title = T("Launch.CustomPrograms.Title");
        Width = 900;
        Height = 580;
        MinWidth = 760;
        MinHeight = 500;
        FontSize = 13;
        _vm = new CustomProgramsViewModel(AppServices.TryGetService<ICustomProgramsStore>(), viewMode);
        DataContext = _vm;
        Content = BuildRoot();
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
            CornerRadius = new CornerRadius(6),
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
        ops.Children.Add(IconButton("IconDelete", "Common.Delete", () => _vm.DeleteSelected()));
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
        form.Children.Add(BuildIconRow());

        var enabled = new CheckBox { Content = T("Launch.CustomPrograms.Enabled"), Margin = new Thickness(0, 6, 0, 0) };
        enabled.Bind(ToggleButton.IsCheckedProperty,
            new Binding("SelectedProgram.Enabled") { Mode = BindingMode.TwoWay });
        form.Children.Add(enabled);

        var hint = new TextBlock
        {
            Text = T("Launch.CustomPrograms.Hint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = UiMetrics.ScaledFont(12),
            Margin = new Thickness(0, 14, 0, 0)
        };
        ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        form.Children.Add(hint);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = form
        };
        Grid.SetColumn(scroll, 2);
        grid.Children.Add(scroll);
        return grid;
    }

    private Control BuildListItem(CustomProgram program)
    {
        var iconImage = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) };
        iconImage.Bind(Image.SourceProperty,
            new Binding(nameof(CustomProgram.IconPath)) { Converter = IconConverter });
        iconImage.Bind(Control.IsVisibleProperty,
            new Binding(nameof(CustomProgram.IconPath))
            {
                Converter = new FuncValueConverter<string?, bool>(v => !string.IsNullOrWhiteSpace(v))
            });

        var fallback = IconHelper.MakeIcon("IconApplication", 18, "WarningBrush");
        fallback.Bind(Control.IsVisibleProperty,
            new Binding(nameof(CustomProgram.IconPath)) { Converter = IsEmptyConverter });

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
        preview.Bind(Image.SourceProperty,
            new Binding("SelectedProgram.IconPath") { Converter = IconConverter });

        var pathBox = new TextBox { IsReadOnly = true };
        pathBox.Bind(TextBox.TextProperty, new Binding("SelectedProgram.IconPath"));

        var pick = new Button { Content = T("Launch.CustomPrograms.PickIcon"), Padding = new Thickness(10, 5) };
        pick.Styled(ControlThemes.ModernButton);
        pick.Click += (_, _) => BrowseIcon();
        var clear = new Button { Content = T("Launch.CustomPrograms.ClearIcon"), Padding = new Thickness(10, 5) };
        clear.Styled(ControlThemes.ModernButton);
        clear.Click += (_, _) => _vm.ClearSelectedIcon();

        var dock = new DockPanel();
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

    private Button IconButton(string iconKey, string tooltipKey, Action action)
    {
        var button = new Button
        {
            Content = IconHelper.MakeIcon(iconKey, 16, "TextPrimaryBrush"),
            Padding = new Thickness(8, 5)
        };
        button.Styled(ControlThemes.ModernButton);
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

    private static Bitmap? LoadIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return System.IO.File.Exists(path) ? new Bitmap(path!) : null;
        }
        catch
        {
            return null;
        }
    }
}
#endif
