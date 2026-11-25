#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно создания/редактирования пользовательского действия контекстного меню
/// (Avalonia/Linux, функция 7, цикл 0.3.9.193–199). Возвращает результат через свойство
/// <see cref="Result"/> при подтверждении. Двойной клик по токену подстановки вставляет его
/// в позицию курсора поля команды; живое превью командной строки строится по выбранной базе.
/// </summary>
public sealed class CustomActionEditWindow : ModalWindowBase
{
    private readonly CustomActionEditViewModel _vm;
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly List<Infobase> _infobases = new();

    /// <summary>
    /// Редактируемое действие: при подтверждении поля применяются к нему же, чтобы
    /// Store.Save по тому же Id перезаписал файл, а не создавал копию. null — новое.
    /// </summary>
    private readonly CustomAction? _sourceAction;

    private readonly TextBox _nameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _commandBox = new TextBox
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 80,
        MaxHeight = 140
    }.Styled(ControlThemes.ModernTextBox);
    private readonly ComboBox _scopeCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly ComboBox _shellCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly CheckBox _supportsBatchCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly CheckBox _runWithoutConfirmCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly CheckBox _escapeValuesCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly TextBox _timeoutBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _hotkeyBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _workingDirectoryBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly ListBox _tokensList = new();
    private readonly ComboBox _exampleBaseCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly TextBox _previewBox = new TextBox { IsReadOnly = true }.Styled(ControlThemes.ModernTextBox);

    /// <summary>Готовое действие при подтверждении, иначе <c>null</c>.</summary>
    public CustomAction? Result { get; private set; }

    /// <param name="action">Редактируемое действие или <c>null</c> для нового.</param>
    /// <param name="existingActions">
    /// Остальные действия списка (0.3.9.198): их горячие клавиши считаются занятыми —
    /// редактор не даст сохранить конфликтующее сочетание. null — без проверки.
    /// </param>
    public CustomActionEditWindow(CustomAction? action = null, IReadOnlyList<CustomAction>? existingActions = null)
    {
        _sourceAction = action;
        _vm = new CustomActionEditViewModel(action, existingActions);
        Title = T(action is null ? "CustomAction.AddTitle" : "CustomAction.EditTitle");
        // Issue #119 (T3): целевой стартовый размер из таблицы 1.4 (паритет с WPF).
        // Явная высота вместо SizeToContent.Height: авторазмер мог схлопывать окно в пустой
        // прямоугольник при модальном показе (регресс «пустого незакрываемого окна», issue #308).
        // Локальный MaxHeight убран: верхнюю границу задаёт единая политика
        // WindowBoundsPolicy (рабочая область минус отступ) в ModalWindowBase.
        Width = 680;
        Height = 680;
        MinWidth = 600;
        MinHeight = 560;
        CanResize = true;
        FontSize = 13;
        Content = BuildRoot();

        // Фокус в поле «Наименование» (issue #299): отложенно после показа окна.
        Opened += (_, _) =>
        {
            if (_exampleBaseCombo.SelectedItem is not Infobase && _infobases.Count > 0)
                _exampleBaseCombo.SelectedIndex = 0;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _nameBox.Focus();
                if (action is null)
                    _nameBox.SelectAll();
            }, Avalonia.Threading.DispatcherPriority.Background);
        };
    }

    /// <summary>Показывает окно модально (синхронно).</summary>
    public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

    private static string T(string key) => LocalizationManager.T(key);

    private static Control Label(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = FontWeight.SemiBold
        };
        ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");
        return label;
    }

    private static Control HintLabel(string text)
    {
        var hint = new TextBlock { Text = text, FontSize = 11 };
        ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        return hint;
    }

    /// <summary>Локализованная подпись области применения для выпадающего списка.</summary>
    private static string ScopeDisplayName(CustomActionScope scope) => scope switch
    {
        CustomActionScope.Base => T("CustomAction.Scope.Base"),
        CustomActionScope.Group => T("CustomAction.Scope.Group"),
        _ => T("CustomAction.Scope.Both")
    };

    /// <summary>Локализованная подпись интерпретатора для выпадающего списка
    /// (как у сценариев, issue #308, п.9): «Авто» локализуется, имена инструментов фиксированы.</summary>
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

        panel.Children.Add(Label(T("CustomAction.Name")));
        _nameBox.Text = _vm.Name;
        panel.Children.Add(_nameBox);

        panel.Children.Add(Label(T("CustomAction.Command")));
        _commandBox.Text = _vm.Command;
        _commandBox.TextChanged += (_, _) => UpdatePreview();
        panel.Children.Add(_commandBox);
        panel.Children.Add(HintLabel(T("CustomAction.CommandHint")));

        panel.Children.Add(Label(T("CustomAction.Scope")));
        // Область применения: значения enum отображаются локализованными подписями
        // через KeyValuePair (как интерпретатор у сценариев, issue #308).
        var scopeItems = Enum.GetValues(typeof(CustomActionScope))
            .Cast<CustomActionScope>()
            .Select(s => new KeyValuePair<CustomActionScope, string>(s, ScopeDisplayName(s)))
            .ToList();
        _scopeCombo.ItemsSource = scopeItems;
        _scopeCombo.ItemTemplate = new FuncDataTemplate<KeyValuePair<CustomActionScope, string>>((pair, _) =>
            new TextBlock { Text = pair.Value });
        _scopeCombo.SelectedItem = scopeItems.FirstOrDefault(p => p.Key == _vm.SelectedScope);
        _scopeCombo.HorizontalAlignment = HorizontalAlignment.Left;
        _scopeCombo.MinWidth = 260;
        _scopeCombo.SelectionChanged += (_, _) => UpdatePreview();
        panel.Children.Add(_scopeCombo);

        panel.Children.Add(Label(T("Script.Shell")));
        var shellItems = CustomActionEditViewModel.ShellOptions
            .Select(s => new KeyValuePair<ScriptShell, string>(s, ShellDisplayName(s)))
            .ToList();
        _shellCombo.ItemsSource = shellItems;
        _shellCombo.ItemTemplate = new FuncDataTemplate<KeyValuePair<ScriptShell, string>>((pair, _) =>
            new TextBlock { Text = pair.Value });
        _shellCombo.SelectedItem = shellItems.FirstOrDefault(p => p.Key == _vm.SelectedShell);
        _shellCombo.HorizontalAlignment = HorizontalAlignment.Left;
        _shellCombo.MinWidth = 260;
        _shellCombo.SelectionChanged += (_, _) => UpdatePreview();
        panel.Children.Add(_shellCombo);

        _supportsBatchCheck.Content = T("CustomAction.SupportsBatch");
        _supportsBatchCheck.IsChecked = _vm.SupportsBatch;
        _supportsBatchCheck.Click += (_, _) => UpdatePreview();
        panel.Children.Add(_supportsBatchCheck);

        _runWithoutConfirmCheck.Content = T("CustomAction.RunWithoutConfirm");
        _runWithoutConfirmCheck.IsChecked = _vm.RunWithoutConfirm;
        panel.Children.Add(_runWithoutConfirmCheck);

        _escapeValuesCheck.Content = T("CustomAction.EscapeValues");
        _escapeValuesCheck.IsChecked = _vm.EscapeValues;
        _escapeValuesCheck.Click += (_, _) => UpdatePreview();
        panel.Children.Add(_escapeValuesCheck);

        panel.Children.Add(Label(T("CustomAction.TimeoutMs")));
        _timeoutBox.Text = _vm.TimeoutSeconds.ToString();
        _timeoutBox.Width = 100;
        _timeoutBox.HorizontalAlignment = HorizontalAlignment.Left;
        _timeoutBox.TextChanged += (_, _) => UpdatePreview();
        panel.Children.Add(_timeoutBox);

        panel.Children.Add(Label(T("CustomAction.Hotkey")));
        _hotkeyBox.Text = _vm.Hotkey;
        _hotkeyBox.TextChanged += (_, _) => UpdatePreview();
        panel.Children.Add(_hotkeyBox);

        panel.Children.Add(Label(T("CustomAction.WorkingDirectory")));
        _workingDirectoryBox.Text = _vm.WorkingDirectory;
        _workingDirectoryBox.TextChanged += (_, _) => UpdatePreview();
        panel.Children.Add(_workingDirectoryBox);

        panel.Children.Add(Label(T("CustomAction.Tokens")));
        // Токены подстановок: объекты ScriptTokenHint — в шаблоне списка отображается
        // «{token} — описание», двойной клик вставляет ТОЛЬКО токен.
        _tokensList.ItemsSource = CustomActionEditViewModel.AvailableTokens
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
        _tokensList.Height = 120;
        _tokensList.DoubleTapped += (_, _) => InsertTokenAtCaret();
        panel.Children.Add(_tokensList);
        panel.Children.Add(HintLabel(T("CustomAction.TokensHint")));

        panel.Children.Add(Label(T("CustomAction.ExampleBase")));
        try
        {
            _infobases.AddRange(AppServices.GetRequiredService<IInfobaseRepository>().Load());
        }
        catch
        {
            // Окно редактирования работает и без списка баз — пример просто не покажется.
        }
        _exampleBaseCombo.ItemsSource = _infobases;
        _exampleBaseCombo.ItemTemplate = new FuncDataTemplate<Infobase>((ib, _) =>
            new TextBlock { Text = ib.Name });
        if (_infobases.Count > 0)
            _exampleBaseCombo.SelectedIndex = 0;
        _exampleBaseCombo.HorizontalAlignment = HorizontalAlignment.Left;
        _exampleBaseCombo.MinWidth = 260;
        _exampleBaseCombo.SelectionChanged += (_, _) => UpdatePreview();
        panel.Children.Add(_exampleBaseCombo);

        panel.Children.Add(HintLabel(T("CustomAction.Preview")));
        _previewBox.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_previewBox);

        // Issue #119 (T3): кнопки — вне прокрутки (футер Dock.Bottom), прокручивается
        // только длинная форма; горизонтальная прокрутка отключена.
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(14, 0, 14, 12)
        };
        var ok = new Button { Content = T("Common.Save"), Width = 100 }.Styled(ControlThemes.DialogConfirmButton);
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
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        return root;
    }

    private void InsertTokenAtCaret()
    {
        // Двойной клик вставляет ТОЛЬКО токен (как у сценариев, issue #308), а не строку
        // «%token% — описание»; guard по ScriptTokenHint обязателен.
        if (_tokensList.SelectedItem is not ScriptTokenHint hint || string.IsNullOrEmpty(hint.Token))
            return;
        var token = hint.Token;
        // Вставка токена в позицию курсора поля команды.
        var text = _commandBox.Text ?? "";
        var caret = _commandBox.CaretIndex;
        if (caret > text.Length)
            caret = text.Length;
        _commandBox.Text = text.Insert(caret, token);
        _commandBox.CaretIndex = caret + token.Length;
        _commandBox.Focus();
        UpdatePreview();
    }

    private Infobase? SelectedExampleBase =>
        _exampleBaseCombo.SelectedItem is Infobase ib ? ib : _infobases.FirstOrDefault();

    /// <summary>
    /// Живое превью полной командной строки действия: поля формы переносятся в черновик
    /// <see cref="CustomAction"/>, превью строится через
    /// <see cref="CustomActionEditViewModel.BuildExampleCommandLine"/> для выбранной базы.
    /// </summary>
    private void UpdatePreview()
    {
        _vm.Name = _nameBox.Text ?? "";
        _vm.Command = _commandBox.Text ?? "";
        _vm.SelectedScope = _scopeCombo.SelectedItem is KeyValuePair<CustomActionScope, string> sc
            ? sc.Key : _vm.SelectedScope;
        _vm.SelectedShell = _shellCombo.SelectedItem is KeyValuePair<ScriptShell, string> sh
            ? sh.Key : _vm.SelectedShell;
        _vm.SupportsBatch = _supportsBatchCheck.IsChecked ?? false;
        _vm.EscapeValues = _escapeValuesCheck.IsChecked ?? true;
        _vm.TimeoutSeconds = int.TryParse(_timeoutBox.Text, out var timeout) ? timeout : _vm.TimeoutSeconds;
        _vm.Hotkey = _hotkeyBox.Text ?? "";
        _vm.WorkingDirectory = _workingDirectoryBox.Text ?? "";
        var draft = new CustomAction
        {
            Name = _vm.Name,
            Command = _vm.Command,
            Scope = _vm.SelectedScope,
            Shell = _vm.SelectedShell,
            SupportsBatch = _vm.SupportsBatch,
            RunWithoutConfirm = _vm.RunWithoutConfirm,
            EscapeValues = _vm.EscapeValues,
            TimeoutMs = _vm.TimeoutSeconds * 1000,
            Hotkey = _vm.Hotkey,
            WorkingDirectory = _vm.WorkingDirectory
        };
        var preview = CustomActionEditViewModel.BuildExampleCommandLine(draft, SelectedExampleBase);
        _previewBox.Text = string.IsNullOrWhiteSpace(preview) ? "—" : preview;
    }

    private void OkClicked()
    {
        _vm.Name = _nameBox.Text ?? "";
        _vm.Command = _commandBox.Text ?? "";
        _vm.SelectedScope = _scopeCombo.SelectedItem is KeyValuePair<CustomActionScope, string> sc
            ? sc.Key : CustomActionScope.Both;
        _vm.SelectedShell = _shellCombo.SelectedItem is KeyValuePair<ScriptShell, string> sh
            ? sh.Key : ScriptShell.Auto;
        _vm.SupportsBatch = _supportsBatchCheck.IsChecked ?? false;
        _vm.RunWithoutConfirm = _runWithoutConfirmCheck.IsChecked ?? false;
        _vm.EscapeValues = _escapeValuesCheck.IsChecked ?? true;
        _vm.TimeoutSeconds = int.TryParse(_timeoutBox.Text, out var timeout) ? timeout : 0;
        _vm.Hotkey = _hotkeyBox.Text ?? "";
        _vm.WorkingDirectory = _workingDirectoryBox.Text ?? "";

        var errorKey = _vm.Validate();
        if (errorKey is not null)
        {
            _dialogs.ShowWarning(T(errorKey), Title ?? "");
            return;
        }

        // При редактировании поля применяются к переданному действию (Id сохраняется),
        // иначе создаётся новое — Store.Save по тому же Id перезапишет файл без дублей.
        if (_sourceAction is not null)
        {
            _vm.ApplyTo(_sourceAction);
            Result = _sourceAction;
        }
        else
        {
            var created = new CustomAction();
            _vm.ApplyTo(created);
            Result = created;
        }
        DialogResult = true;
        Close();
    }
}
#endif