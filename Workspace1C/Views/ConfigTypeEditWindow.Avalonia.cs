#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C;

/// <summary>
/// Отдельное модальное окно создания/редактирования пользовательской типовой конфигурации 1С
/// (issue #321): код, наименование, сегмент URL, ник на releases.1c.ru и список редакций.
/// Работает на копии данных: результат возвращается через <see cref="Result"/> при
/// подтверждении — правка одной строки не влияет на другие конфигурации. Предопределённые
/// конфигурации сюда не передаются (окно списка скрывает для них кнопки «Изменить»/«Удалить»).
/// Avalonia/Linux-версия WPF-окна <see cref="ConfigTypeEditWindow"/>.
/// </summary>
public sealed class ConfigTypeEditWindow : ModalWindowBase
{
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    // ObservableCollection: «Добавить» должно сразу показывать новую редакцию в списке
    // (обычный List не уведомляет UI — issue #321).
    private readonly ObservableCollection<OneCConfigEdition> _editions = new();
    private readonly string _originalCode;
    private StackPanel? _rootPanel;

    private readonly TextBox _codeBox = MakeTextBox(string.Empty);
    private readonly TextBox _nameBox = MakeTextBox(string.Empty);
    private readonly TextBox _configNameBox = MakeTextBox(string.Empty);
    private readonly TextBox _nickBox = MakeTextBox(string.Empty);
    private readonly ComboBox _accountBox = new() { Height = 32, DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ViewModels.ItsAccountSelectionItem.Name)) };
    private readonly List<ViewModels.ItsAccountSelectionItem> _accountItems = new();
    // Таблица редакций (issue #321): ListBox с 4 колонками (имя/Ред/Подред/URL);
    // строки правятся отдельным модальным диалогом EditionEditWindow.
    private readonly ListBox _editionsList = new();

    /// <summary>Готовая конфигурация при подтверждении, иначе <c>null</c>.</summary>
    public OneCConfigType? Result { get; private set; }

    /// <param name="model">Редактируемая конфигурация или <c>null</c> для новой.</param>
    public ConfigTypeEditWindow(OneCConfigType? model = null)
    {
        _originalCode = model?.Code ?? string.Empty;
        var isNew = model is null;

        Title = T(isNew ? "Updates.AddConfig" : "Updates.EditConfig");
        Width = 700;
        Height = 720;
        MinWidth = 600;
        MinHeight = 620;
        FontSize = 13;
        CanResize = true;

        _codeBox.Text = model?.Code ?? string.Empty;
        _nameBox.Text = model?.Name ?? string.Empty;
        _configNameBox.Text = model?.ConfigName ?? string.Empty;
        // Поле «Сегмент адреса» (UrlCode) из UI удалено (issue #321): адрес обновлений
        // строится только из ника (releases.1c.ru/project/<nick>), UrlCode не нужен.
        _nickBox.Text = model?.Nick ?? string.Empty;

        // Учётная запись ИТС (issue #333): записи справочника; виртуальный пункт «Основная»
        // добавляется только если в справочнике нет реальной записи с таким именем.
        _accountItems.AddRange(ViewModels.ItsAccountSelectionBuilder.Build(
            AppServices.GetRequiredService<Services.IItsAccountsStore>()));
        _accountBox.ItemsSource = _accountItems;
        var accountIndex = ViewModels.ItsAccountSelectionBuilder.IndexOf(_accountItems, model?.AccountId);
        _accountBox.SelectedIndex = accountIndex >= 0 ? accountIndex : 0;

        if (model is not null)
        {
            foreach (var edition in model.Editions)
                _editions.Add(edition);
        }
        if (_editions.Count > 0)
            _editionsList.SelectedIndex = 0;

        Content = BuildRoot();

        // Фокус в поле «Наименование» (issue #299): отложенно после показа окна —
        // синхронная установка в Opened слетает до активации модального диалога.
        Opened += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _nameBox.Focus();
                if (isNew)
                    _nameBox.SelectAll();
            }, Avalonia.Threading.DispatcherPriority.Background);

            // Высота окна (issue #321): выше, чтобы влезали 3–4 строки редакций, но не
            // за рамки экрана — WindowSizeMath (ClampHeight/FitTop), как в остальных окнах.
            Avalonia.Threading.Dispatcher.UIThread.Post(
                FitHeightToContent, Avalonia.Threading.DispatcherPriority.Background);
        };
    }

    /// <summary>
    /// Подгоняет высоту окна под содержимое (паттерн ScriptScenarioEditWindow, issue #308):
    /// измеряет корневой контейнер при бесконечной высоте, клампит в [MinHeight, MaxHeight]
    /// через <see cref="WindowSizeMath.ClampHeight"/> и поднимает окно, если низ уходит
    /// за нижний край рабочей области (<see cref="WindowSizeMath.FitTop"/>).
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

    private Control BuildRoot()
    {
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };
        _rootPanel = panel;

        var title = new TextBlock
        {
            Text = Title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        };
        panel.Children.Add(title);

        panel.Children.Add(MakeFieldRow(T("Updates.ConfigCode"), _codeBox));
        panel.Children.Add(MakeFieldRow(T("Updates.Name"), _nameBox));
        // Имя конфигурации (issue #321): имя в метаданных 1С — для сопоставления данных
        // о конфигурации, в адрес обновлений не попадает.
        ToolTip.SetTip(_configNameBox, T("Updates.ConfigNameHint"));
        panel.Children.Add(MakeFieldRow(T("Updates.ConfigName"), _configNameBox));
        // Поле «Сегмент адреса» (UrlCode) из UI удалено (issue #321): адрес обновлений
        // строится только из ника (releases.1c.ru/project/<nick>), UrlCode не нужен.
        panel.Children.Add(MakeFieldRow(T("Updates.Nick"), _nickBox));
        panel.Children.Add(MakeFieldRow(T("ItsAccounts.AccountLabel"), _accountBox));

        // Таблица редакций из 4 колонок (issue #321): имя, «Ред», «Подред» и переопределённая
        // ссылка. Строки правятся отдельным модальным диалогом EditionEditWindow —
        // инлайн-поля под списком убраны (раньше видна была только первая колонка).
        var editionsHeader = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(34)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)),
            },
            Margin = new Thickness(0, 6, 0, 2)
        };
        // Колонка 0 — отметка основной редакции «★» (issue #14).
        var defaultHeader = MakeHeaderText("★", 0);
        ToolTip.SetTip(defaultHeader, T("Updates.DefaultEdition"));
        editionsHeader.Children.Add(defaultHeader);
        editionsHeader.Children.Add(MakeHeaderText(T("Updates.Name"), 1));
        editionsHeader.Children.Add(MakeHeaderText(T("Updates.EditionRed"), 2));
        editionsHeader.Children.Add(MakeHeaderText(T("Updates.EditionSubRed"), 3));
        editionsHeader.Children.Add(MakeHeaderText(T("Updates.UrlOverride"), 4));

        _editionsList.ItemsSource = _editions;
        _editionsList.HorizontalAlignment = HorizontalAlignment.Stretch;
        // MinHeight на 3 строки (issue #369): даже при пустой таблице видно, что это таблица
        // (место внизу и так пустует). Строка ~32 + шапка ~30 → 3×32+30 ≈ 130.
        _editionsList.MinHeight = 130;
        _editionsList.MaxHeight = 200;
        _editionsList.ItemTemplate = new FuncDataTemplate<OneCConfigEdition>((edition, _) => BuildEditionRow(edition));

        var editionsColumn = new StackPanel { Spacing = 6 };
        editionsColumn.Children.Add(editionsHeader);
        editionsColumn.Children.Add(_editionsList);

        var editionButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var addEd = new Button { Content = T("Updates.AddEdition"), Height = 30 };
        addEd.Styled(ControlThemes.SelectAllButton);
        addEd.Click += (_, _) => OnAddEditionClick();
        var editEd = new Button { Content = T("Updates.EditEdition"), Height = 30 };
        editEd.Styled(ControlThemes.SelectAllButton);
        editEd.Click += (_, _) => OnEditEditionClick();
        var defaultEd = new Button { Content = T("Updates.SetDefaultEdition"), Height = 30 };
        defaultEd.Styled(ControlThemes.SelectAllButton);
        ToolTip.SetTip(defaultEd, T("Updates.SetDefaultEditionHint"));
        defaultEd.Click += (_, _) => OnSetDefaultEditionClick();
        var removeEd = new Button { Content = T("Updates.Delete"), Height = 30 };
        removeEd.Styled(ControlThemes.SelectAllButton);
        removeEd.Click += (_, _) => OnRemoveEditionClick();
        editionButtons.Children.Add(addEd);
        editionButtons.Children.Add(editEd);
        editionButtons.Children.Add(defaultEd);
        editionButtons.Children.Add(removeEd);
        editionsColumn.Children.Add(editionButtons);

        panel.Children.Add(MakeFieldRow(T("Updates.Editions"), editionsColumn));

        // Кнопки: Сохранить / Отмена.
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var save = new Button { Content = T("Updates.Save"), Width = 130, Height = 36, IsDefault = true };
        save.Styled(ControlThemes.DialogConfirmButton);
        save.Click += (_, _) => OnSaveClick();
        var cancel = BuildCancelActionButton(130);
        cancel.Click += (_, _) => OnCancelClick();
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        return panel;
    }

    private void OnSaveClick()
    {
        var name = _nameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _dialogs.ShowWarning(T("Updates.NameRequired"), T("Updates.EditConfig"));
            _nameBox.Focus();
            return;
        }

        var code = _codeBox.Text?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            // Для связи ИБ ↔ конфигурация нужен стабильный код: сохраняем прежний,
            // а для новой конфигурации генерируем из наименования.
            code = _originalCode.Length > 0 ? _originalCode : GenerateCode(name);
        }

        var selectedAccount = _accountBox.SelectedItem as ViewModels.ItsAccountSelectionItem;

        Result = new OneCConfigType
        {
            Code = code,
            Name = name,
            ConfigName = _configNameBox.Text?.Trim() ?? string.Empty,
            Nick = _nickBox.Text?.Trim() ?? string.Empty,
            // Учётная запись ИТС: пусто — «Основная» (либо выбранная в настройках).
            AccountId = selectedAccount?.Id ?? string.Empty,
            IsBuiltIn = false,
            Editions = new List<OneCConfigEdition>(_editions),
        };
        OneCConfigType.NormalizeDefaultEdition(Result.Editions);
        DialogResult = true;
        Close();
    }

    private void OnCancelClick()
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// «Добавить…» (issue #321): отдельный модальный диалог с полями новой редакции вместо
    /// инлайн-полей под таблицей. Диалог работает на копии; строка добавляется по ОК.
    /// </summary>
    private void OnAddEditionClick()
    {
        var dialog = new EditionEditWindow();
        if (!dialog.ShowSync(this) || dialog.Result is not { } edition)
            return;

        _editions.Add(edition);
        _editionsList.SelectedItem = edition;
        _editionsList.ScrollIntoView(edition);
    }

    /// <summary>«Изменить…»: модальный диалог правки выбранной редакции (копия → замена).</summary>
    private void OnEditEditionClick()
    {
        if (_editionsList.SelectedItem is not OneCConfigEdition edition)
        {
            _dialogs.ShowInfo(T("Updates.SelectEditionFirst"), T("Updates.EditEdition"));
            return;
        }

        var dialog = new EditionEditWindow(new OneCConfigEdition
        {
            Name = edition.Name,
            Red = edition.Red,
            SubRed = edition.SubRed,
            UrlOverride = edition.UrlOverride,
            IsDefault = edition.IsDefault,
        });
        if (!dialog.ShowSync(this) || dialog.Result is not { } updated)
            return;

        // Флаг «Основная» правится кнопкой «Сделать основной», диалог его не меняет (issue #14).
        updated.IsDefault = edition.IsDefault;
        var index = _editions.IndexOf(edition);
        _editions[index] = updated;
        _editionsList.SelectedItem = updated;
    }

    /// <summary>
    /// «Сделать основной» (issue #14): выбранная редакция становится основной — она
    /// используется, когда редакцию не удалось подобрать по версии базы. Строки
    /// пересоздаются копиями, чтобы шаблон списка перерисовал отметку «★».
    /// </summary>
    private void OnSetDefaultEditionClick()
    {
        if (_editionsList.SelectedItem is not OneCConfigEdition edition)
        {
            _dialogs.ShowInfo(T("Updates.SelectEditionFirst"), T("Updates.SetDefaultEdition"));
            return;
        }

        var selectedIndex = _editions.IndexOf(edition);
        OneCConfigType.SetDefaultEdition(_editions, edition);
        for (var i = 0; i < _editions.Count; i++)
        {
            var e = _editions[i];
            _editions[i] = new OneCConfigEdition
            {
                Name = e.Name,
                Red = e.Red,
                SubRed = e.SubRed,
                UrlOverride = e.UrlOverride,
                IsDefault = e.IsDefault,
            };
        }
        _editionsList.SelectedIndex = selectedIndex;
    }

    /// <summary>«Удалить»: удаляет выбранную редакцию из списка.</summary>
    private void OnRemoveEditionClick()
    {
        if (_editionsList.SelectedItem is not OneCConfigEdition edition)
            return;
        var index = _editions.IndexOf(edition);
        _editions.Remove(edition);
        if (_editions.Count > 0)
            _editionsList.SelectedIndex = Math.Min(index, _editions.Count - 1);
    }

    private static TextBox MakeTextBox(string watermark)
    {
        var tb = new TextBox
        {
            Watermark = watermark,
            Margin = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            MinHeight = 34
        };
        tb.Styled(ControlThemes.ModernTextBox);
        return tb;
    }

    /// <summary>Строка «подпись / поле» формы редактора.</summary>
    private static Grid MakeFieldRow(string label, Control field)
    {
        var grid = new Grid { Margin = new Thickness(0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(170)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        var labelBlock = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        return grid;
    }

    /// <summary>Заголовок колонки таблицы редакций.</summary>
    private static TextBlock MakeHeaderText(string text, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0),
            Opacity = 0.65
        };
        Grid.SetColumn(block, column);
        return block;
    }

    /// <summary>Строка таблицы редакций: «★» (основная, issue #14) + имя/Ред/Подред/URL (issue #321).</summary>
    private static Control BuildEditionRow(OneCConfigEdition edition)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(34)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1.5, GridUnitType.Star)),
            }
        };
        var mark = EditionCellText(edition.DefaultMark, 0);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        Themes.ThemeBrushes.Bind(mark, TextBlock.ForegroundProperty, "WarningBrush");
        grid.Children.Add(mark);
        grid.Children.Add(EditionCellText(edition.Name, 1));
        grid.Children.Add(EditionCellText(edition.Red, 2));
        grid.Children.Add(EditionCellText(edition.SubRed, 3));
        grid.Children.Add(EditionCellText(edition.UrlOverride, 4));
        return grid;
    }

    private static TextBlock EditionCellText(string text, int column)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0)
        };
        Grid.SetColumn(block, column);
        return block;
    }

    /// <summary>Генерирует стабильный код из наименования (латиница/цифры/подчёркивания).</summary>
    private static string GenerateCode(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in name)
        {
            if (ch < 128 && char.IsLetterOrDigit(ch))
                sb.Append(char.ToUpperInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');
            if (sb.Length >= 12)
                break;
        }
        var result = sb.ToString().Trim('_');
        return result.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : result;
    }
}
#endif