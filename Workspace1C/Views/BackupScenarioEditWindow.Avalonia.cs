#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно создания/редактирования сценария резервирования (Avalonia/Linux).
/// Возвращает результат через свойство <see cref="Result"/> при подтверждении.
/// </summary>
public sealed class BackupScenarioEditWindow : ModalWindowBase
{
    private readonly BackupScenarioEditViewModel _vm;
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    private readonly TextBox _nameBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _templateBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly CheckBox _timestampCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly ComboBox _formatCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly ComboBox _engineCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly TextBox _prefixBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly ListBox _dirList = new();
    private readonly TextBox _keepCountBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _ageDaysBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly CheckBox _useAuthCheck = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly TextBox _userBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _passwordBox = new TextBox { PasswordChar = '\u25CF' }.Styled(ControlThemes.ModernTextBox);

    /// <summary>Готовый сценарий при подтверждении, иначе <c>null</c>.</summary>
    public BackupScenario? Result { get; private set; }

    /// <param name="scenario">Редактируемый сценарий или <c>null</c> для нового.</param>
    public BackupScenarioEditWindow(BackupScenario? scenario = null)
    {
        _vm = new BackupScenarioEditViewModel(scenario);
        Title = T(scenario is null ? "Backup.AddScenario" : "Backup.EditScenario");
        Width = 620;
        Height = 680;
        MinWidth = 540;
        MinHeight = 580;
        FontSize = 13;
        Content = BuildRoot();

        // Фокус в поле «Наименование» (issue #299): отложенно после показа окна —
        // синхронная установка в Opened слетает до активации модального диалога.
        Opened += (_, _) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _nameBox.Focus();
                if (scenario is null)
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
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        // Цвет подписи из темы (issue #291), как в остальных окнах Avalonia.
        ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");
        return label;
    }

    private static Control HintLabel(string text)
    {
        var hint = new TextBlock { Text = text, FontSize = 11 };
        ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        return hint;
    }

    private Control BuildRoot()
    {
        var panel = new StackPanel { Margin = new Avalonia.Thickness(14), Spacing = 8 };

        panel.Children.Add(Label(T("Backup.Name")));
        _nameBox.Text = _vm.Name;
        panel.Children.Add(_nameBox);

        panel.Children.Add(Label(T("Backup.FileNameTemplate")));
        _templateBox.Text = _vm.FileNameTemplate;
        panel.Children.Add(_templateBox);
        panel.Children.Add(HintLabel(T("Backup.TemplateHint")));

        _timestampCheck.Content = T("Backup.IncludeTimestamp");
        _timestampCheck.IsChecked = _vm.IncludeTimestamp;
        panel.Children.Add(_timestampCheck);

        panel.Children.Add(Label(T("Backup.Format")));
        _formatCombo.ItemsSource = System.Enum.GetValues<BackupFormat>();
        _formatCombo.SelectedItem = _vm.Format;
        _formatCombo.HorizontalAlignment = HorizontalAlignment.Left;
        _formatCombo.MinWidth = 160;
        panel.Children.Add(_formatCombo);

        panel.Children.Add(Label(T("Backup.Engine")));
        _engineCombo.ItemsSource = BackupScenarioEditViewModel.EngineNames;
        _engineCombo.SelectedIndex = _vm.EngineIndex;
        _engineCombo.HorizontalAlignment = HorizontalAlignment.Left;
        _engineCombo.MinWidth = 240;
        panel.Children.Add(_engineCombo);
        panel.Children.Add(HintLabel(T("Backup.EngineHint")));

        panel.Children.Add(Label(T("Backup.BasePrefix")));
        _prefixBox.Text = _vm.BasePrefix;
        panel.Children.Add(_prefixBox);

        panel.Children.Add(Label(T("Backup.TargetDirectories")));
        var dirButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var addDir = new Button { Content = T("Common.Browse"), Width = 110 }.Styled(ControlThemes.SecondaryButton);
        addDir.Click += (_, _) => AddDirectory();
        // Удаление каталога из сценария — опасное действие: DangerButton (P2-1).
        var removeDir = new Button { Content = T("Common.Delete"), Width = 96 }.Styled(ControlThemes.DangerButton);
        removeDir.Click += (_, _) => RemoveDirectory();
        dirButtons.Children.Add(addDir);
        dirButtons.Children.Add(removeDir);
        panel.Children.Add(dirButtons);

        _dirList.ItemsSource = _vm.TargetDirectories;
        _dirList.Height = 110;
        panel.Children.Add(_dirList);

        panel.Children.Add(Label(T("Backup.Rotation")));
        var rotationRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(12) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };
        var keepColumn = new StackPanel();
        keepColumn.Children.Add(HintLabel(T("Backup.KeepCount")));
        _keepCountBox.Text = _vm.KeepCount.ToString();
        keepColumn.Children.Add(_keepCountBox);
        Grid.SetColumn(keepColumn, 0);
        rotationRow.Children.Add(keepColumn);
        var ageColumn = new StackPanel();
        ageColumn.Children.Add(HintLabel(T("Backup.DeleteOlderThanDays")));
        _ageDaysBox.Text = _vm.DeleteOlderThanDays.ToString();
        ageColumn.Children.Add(_ageDaysBox);
        Grid.SetColumn(ageColumn, 2);
        rotationRow.Children.Add(ageColumn);
        panel.Children.Add(rotationRow);
        panel.Children.Add(HintLabel(T("Backup.RotationHint")));

        panel.Children.Add(Label(T("Backup.Credentials")));
        _useAuthCheck.Content = T("Backup.UseInfobaseAuth");
        _useAuthCheck.IsChecked = _vm.UseInfobaseAuth;
        panel.Children.Add(_useAuthCheck);

        panel.Children.Add(Label(T("Backup.User")));
        _userBox.Text = _vm.User;
        panel.Children.Add(_userBox);

        panel.Children.Add(Label(T("Backup.Password")));
        _passwordBox.Text = _vm.Password;
        panel.Children.Add(_passwordBox);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        };
        var ok = new Button { Content = T("Common.Save"), Width = 90 }.Styled(ControlThemes.DialogConfirmButton);
        ok.Click += (_, _) => OkClicked();
        var cancel = new Button { Content = T("Common.Cancel"), Width = 90 }.Styled(ControlThemes.DialogCancelButton);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        bottom.Children.Add(ok);
        bottom.Children.Add(cancel);
        panel.Children.Add(bottom);

        return new ScrollViewer { Content = panel };
    }

    private void AddDirectory()
    {
        var path = _dialogs.OpenFolderDialog(T("Backup.TargetDirectories"));
        if (!string.IsNullOrWhiteSpace(path))
            _vm.TargetDirectories.Add(path);
    }

    private void RemoveDirectory()
    {
        if (_dirList.SelectedIndex >= 0)
            _vm.RemoveDirectoryAt(_dirList.SelectedIndex);
    }

    private void OkClicked()
    {
        _vm.Name = _nameBox.Text ?? "";
        _vm.FileNameTemplate = _templateBox.Text ?? "";
        _vm.IncludeTimestamp = _timestampCheck.IsChecked == true;
        _vm.Format = _formatCombo.SelectedItem is BackupFormat f ? f : BackupFormat.Dt;
        _vm.EngineIndex = Math.Max(0, _engineCombo.SelectedIndex);
        _vm.BasePrefix = _prefixBox.Text ?? "";
        // Числовые поля ротации парсим явно: привязка к int на некорректном тексте не сработала бы.
        _vm.KeepCount = TryParseInt(_keepCountBox.Text, 0);
        _vm.DeleteOlderThanDays = TryParseInt(_ageDaysBox.Text, 0);
        _vm.UseInfobaseAuth = _useAuthCheck.IsChecked == true;
        _vm.User = _userBox.Text ?? "";
        _vm.Password = _passwordBox.Text ?? "";

        var errorKey = _vm.Validate();
        if (errorKey is not null)
        {
            _dialogs.ShowWarning(T(errorKey), Title ?? "");
            return;
        }

        var scenario = new BackupScenario();
        _vm.ApplyTo(scenario);
        Result = scenario;
        DialogResult = true;
        Close();
    }

    /// <summary>Парсит целое число; при неудаче возвращает значение по умолчанию.</summary>
    private static int TryParseInt(string? text, int fallback) =>
        int.TryParse(text?.Trim(), out var value) && value >= 0 ? value : fallback;
}
#endif