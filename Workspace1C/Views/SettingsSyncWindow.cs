#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Services.Sync;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Синхронизация настроек» (п.9, 1.0.0.91, WPF): хранилище (WebDAV / сетевая
/// папка), доступ, интервал автозапуска, набор файлов и журнал операций. Вход —
/// кнопка «Синхронизация настроек…» в окне «Настройки». Пароль WebDAV сохраняется
/// только зашифрованным и в журнал не попадает.
/// </summary>
public partial class SettingsSyncWindow : Window
{
    private readonly SettingsSyncViewModel _vm;
    private readonly ComboBox _kindCombo;
    private readonly TextBox _urlBox;
    private readonly TextBox _folderBox;
    private readonly TextBox _userBox;
    private readonly PasswordBox _passwordBox;
    private readonly TextBox _intervalBox;
    private readonly TextBox _patternsBox;
    private readonly TextBlock _lastSyncText;
    private readonly TextBlock _statusText;
    private readonly ListBox _logList;
    private readonly Button _syncNowButton;

    public SettingsSyncWindow()
    {
        var service = AppServices.TryGetService<SettingsSyncService>();
        _vm = new SettingsSyncViewModel(service);

        Title = LocalizationManager.T("SettingsSync.Title");
        Width = 720;
        Height = 640;
        MinWidth = 640;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "ContentBackgroundBrush");

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // подсказка
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // форма
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // статус
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // кнопки

        // --- Подсказка об ограничениях ---
        var hint = new TextBlock
        {
            Text = LocalizationManager.T("SettingsSync.PathWarning"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        };
        hint.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        grid.Children.Add(hint);
        Grid.SetRow(hint, 0);

        // --- Форма ---
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };

        var enabledCheck = new CheckBox
        {
            Content = LocalizationManager.T("SettingsSync.Enabled"),
            IsChecked = _vm.Enabled,
            Margin = new Thickness(0, 0, 0, 10)
        };
        enabledCheck.Checked += (_, _) => _vm.Enabled = true;
        enabledCheck.Unchecked += (_, _) => _vm.Enabled = false;
        panel.Children.Add(enabledCheck);

        panel.Children.Add(MakeLabel("SettingsSync.StorageKind"));
        _kindCombo = new ComboBox
        {
            ItemsSource = _vm.StorageKindOptions,
            DisplayMemberPath = "Value",
            SelectedIndex = _vm.StorageKindIndex,
            Margin = new Thickness(0, 2, 0, 10)
        };
        _kindCombo.SelectionChanged += (_, _) => { if (IsLoaded) _vm.StorageKindIndex = _kindCombo.SelectedIndex; };
        panel.Children.Add(_kindCombo);

        panel.Children.Add(MakeLabel("SettingsSync.WebDavUrl"));
        _urlBox = new TextBox { Text = _vm.WebDavUrl, Margin = new Thickness(0, 2, 0, 10) };
        _urlBox.TextChanged += (_, _) => _vm.WebDavUrl = _urlBox.Text;
        panel.Children.Add(_urlBox);

        panel.Children.Add(MakeLabel("SettingsSync.FolderPath"));
        _folderBox = new TextBox { Text = _vm.FolderPath, Margin = new Thickness(0, 2, 0, 10) };
        _folderBox.TextChanged += (_, _) => _vm.FolderPath = _folderBox.Text;
        panel.Children.Add(_folderBox);

        panel.Children.Add(MakeLabel("SettingsSync.User"));
        _userBox = new TextBox { Text = _vm.User, Margin = new Thickness(0, 2, 0, 10) };
        _userBox.TextChanged += (_, _) => _vm.User = _userBox.Text;
        panel.Children.Add(_userBox);

        panel.Children.Add(MakeLabel("SettingsSync.Password"));
        _passwordBox = new PasswordBox { Margin = new Thickness(0, 2, 0, 10) };
        _passwordBox.PasswordChanged += (_, _) => _vm.Password = _passwordBox.Password;
        panel.Children.Add(_passwordBox);

        panel.Children.Add(MakeLabel("SettingsSync.Interval"));
        _intervalBox = new TextBox { Text = _vm.IntervalMinutesText, Margin = new Thickness(0, 2, 0, 10) };
        _intervalBox.TextChanged += (_, _) => _vm.IntervalMinutesText = _intervalBox.Text;
        panel.Children.Add(_intervalBox);

        panel.Children.Add(MakeLabel("SettingsSync.Patterns"));
        _patternsBox = new TextBox
        {
            Text = _vm.PatternsText,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 2, 0, 4)
        };
        _patternsBox.TextChanged += (_, _) => _vm.PatternsText = _patternsBox.Text;
        panel.Children.Add(_patternsBox);

        var patternsHint = new TextBlock
        {
            Text = LocalizationManager.T("SettingsSync.PatternsHint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8)
        };
        patternsHint.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        panel.Children.Add(patternsHint);

        var lastSyncRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
        lastSyncRow.Children.Add(MakeLabel("SettingsSync.LastSync"));
        _lastSyncText = new TextBlock { Text = _vm.LastSyncAtText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        // issue #26: значение «последняя синхронизация» привязано к вторичной кисти —
        // без неё текст был чёрным в тёмной теме (как уже сделано в Avalonia-версии).
        _lastSyncText.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        lastSyncRow.Children.Add(_lastSyncText);
        panel.Children.Add(lastSyncRow);

        panel.Children.Add(MakeLabel("SettingsSync.Log"));
        _logList = new ListBox { MinHeight = 120, Margin = new Thickness(0, 2, 0, 8) };
        _logList.ItemsSource = _vm.LogEntries;
        panel.Children.Add(_logList);

        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        grid.Children.Add(scroll);
        Grid.SetRow(scroll, 1);

        // --- Статус ---
        _statusText = new TextBlock
        {
            Text = _vm.StatusText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 12
        };
        _statusText.SetResourceReference(ForegroundProperty, "TextSecondaryBrush");
        grid.Children.Add(_statusText);
        Grid.SetRow(_statusText, 2);

        // --- Кнопки ---
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        _syncNowButton = new Button
        {
            Content = LocalizationManager.T("SettingsSync.SyncNow"),
            Width = 170,
            Height = 34,
            Margin = new Thickness(0, 0, 10, 0),
            IsEnabled = _vm.CanSyncNow
        };
        _syncNowButton.SetResourceReference(StyleProperty, "ModernButton");
        _syncNowButton.Click += async (_, _) => await _vm.SyncNowAsync();
        buttons.Children.Add(_syncNowButton);

        var saveButton = new Button
        {
            Content = LocalizationManager.T("Common.Save"),
            Width = 130,
            Height = 34,
            Margin = new Thickness(0, 0, 10, 0)
        };
        saveButton.SetResourceReference(StyleProperty, "ModernButton");
        saveButton.Click += (_, _) =>
        {
            _vm.Save();
            DialogResult = true;
        };
        buttons.Children.Add(saveButton);

        var closeButton = new Button
        {
            Content = LocalizationManager.T("Common.Close"),
            Width = 100,
            Height = 34,
            IsCancel = true
        };
        closeButton.SetResourceReference(StyleProperty, "SecondaryButton");
        closeButton.Click += (_, _) => { _vm.Save(); Close(); };
        buttons.Children.Add(closeButton);

        grid.Children.Add(buttons);
        Grid.SetRow(buttons, 3);

        Content = grid;
        Loaded += (_, _) =>
        {
            _kindCombo.SelectedIndex = _vm.StorageKindIndex;
            _vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsSyncViewModel.CanSyncNow))
                    _syncNowButton.IsEnabled = _vm.CanSyncNow;
            };
        };
    }

    private static TextBlock MakeLabel(string key)
    {
        // issue #26 (7OH, 2026-10-29 «Синхронизация настроек - надписи в наличии
        // черные на тёмном»): подписи полей обязаны брать кисть темы FormLabelBrush,
        // а не дефолтный чёрный — в тёмной теме без привязки текст нечитаем
        // (та же кисть, что у Avalonia-версии окна).
        var label = new TextBlock
        {
            Text = LocalizationManager.T(key),
            FontWeight = System.Windows.FontWeights.SemiBold,
            Margin = new Thickness(0, 2, 0, 0)
        };
        label.SetResourceReference(ForegroundProperty, "FormLabelBrush");
        return label;
    }
}
#endif