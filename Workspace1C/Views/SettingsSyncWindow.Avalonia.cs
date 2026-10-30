#if LINUX
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Services.Sync;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Синхронизация настроек» (п.9, 1.0.0.91, Avalonia/Linux): хранилище
/// (WebDAV / сетевая папка), интервал автозапуска, набор файлов и журнал операций.
/// Вход — кнопка «Синхронизация настроек…» в окне «Настройки».
/// Пароль WebDAV сохраняется только зашифрованным, в журнал не попадает.
/// </summary>
public sealed class SettingsSyncWindow : ModalWindowBase
{
    private readonly SettingsSyncViewModel _vm;

    // Поля ввода получают тематические ModernTextBox/ModernComboBox (issue #26, 1.0.1.6):
    // без явной темы Fluent-фон полей не меняется в тёмной теме, и «Интервал
    // автозапуска»/«Файлы для синхронизации» выглядят иначе остальных окон.
    // Журнал (_logList) уже темируется неявным стилем ListBox (ControlDefaults.axaml).
    private readonly ComboBox _kindCombo = new ComboBox().Styled(ControlThemes.ModernComboBox);
    private readonly TextBox _urlBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _folderBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _userBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _passwordBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _intervalBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _patternsBox = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBlock _lastSyncText = new();
    private readonly TextBlock _statusText = new();
    private readonly ListBox _logList = new();
    private readonly Button _syncNowButton = new();

    public SettingsSyncWindow()
    {
        var service = AppServices.TryGetService<SettingsSyncService>();
        _vm = new SettingsSyncViewModel(service);

        Title = T("SettingsSync.Title");
        Width = 720;
        Height = 640;
        MinWidth = 620;
        MinHeight = 560;
        FontSize = 13;
        Content = BuildRoot();
        Loaded += (_, _) => { _kindCombo.SelectedIndex = _vm.StorageKindIndex; };
    }

    private static string T(string key) => LocalizationManager.T(key);

    private Control BuildRoot()
    {
        var root = new DockPanel { Margin = new Thickness(14) };

        // ---- Нижняя панель: кнопки ----
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        _syncNowButton.Content = T("SettingsSync.SyncNow");
        _syncNowButton.Click += async (_, _) => await RunSyncAsync();
        var saveButton = new Button { Content = T("Common.Save"), Width = 120 };
        saveButton.Click += (_, _) => { _vm.Save(); Close(); };
        var closeButton = new Button { Content = T("Common.Close"), Width = 100 };
        closeButton.Click += (_, _) => { _vm.Save(); Close(); };
        foreach (var b in new Control[] { _syncNowButton, saveButton, closeButton })
            b.Styled(ControlThemes.ModernButton);
        bottom.Children.Add(_syncNowButton);
        bottom.Children.Add(saveButton);
        bottom.Children.Add(closeButton);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        // ---- Статус ----
        _statusText.Text = _vm.StatusText;
        _statusText.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _statusText.FontSize = 12;
        _statusText.Margin = new Thickness(0, 6, 0, 0);
        ThemeBrushes.Bind(_statusText, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        DockPanel.SetDock(_statusText, Dock.Bottom);
        root.Children.Add(_statusText);

        // ---- Форма ----
        var panel = new StackPanel();

        var hint = new TextBlock
        {
            Text = T("SettingsSync.PathWarning"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        };
        ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        panel.Children.Add(hint);

        var enabledCheck = new CheckBox { Content = T("SettingsSync.Enabled"), IsChecked = _vm.Enabled, Margin = new Thickness(0, 0, 0, 10) };
        enabledCheck.IsCheckedChanged += (_, _) => _vm.Enabled = enabledCheck.IsChecked == true;
        panel.Children.Add(enabledCheck);

        panel.Children.Add(Label(T("SettingsSync.StorageKind")));
        foreach (var opt in _vm.StorageKindOptions)
            _kindCombo.Items.Add(new ComboBoxItem { Content = opt.Value, Tag = opt.Key });
        _kindCombo.SelectedIndex = _vm.StorageKindIndex;
        _kindCombo.SelectionChanged += (_, _) =>
        {
            if (_kindCombo.SelectedItem is ComboBoxItem item &&
                item.Tag is string kind && kind == "NetworkFolder")
                _vm.StorageKindIndex = 1;
            else if (_kindCombo.SelectedItem is ComboBoxItem)
                _vm.StorageKindIndex = 0;
        };
        _kindCombo.Margin = new Thickness(0, 2, 0, 10);
        panel.Children.Add(_kindCombo);

        panel.Children.Add(Label(T("SettingsSync.WebDavUrl")));
        _urlBox.Text = _vm.WebDavUrl;
        _urlBox.TextChanged += (_, _) => _vm.WebDavUrl = _urlBox.Text ?? string.Empty;
        _urlBox.Margin = new Thickness(0, 2, 0, 10);
        panel.Children.Add(_urlBox);

        panel.Children.Add(Label(T("SettingsSync.FolderPath")));
        _folderBox.Text = _vm.FolderPath;
        _folderBox.TextChanged += (_, _) => _vm.FolderPath = _folderBox.Text ?? string.Empty;
        _folderBox.Margin = new Thickness(0, 2, 0, 10);
        panel.Children.Add(_folderBox);

        panel.Children.Add(Label(T("SettingsSync.User")));
        _userBox.Text = _vm.User;
        _userBox.TextChanged += (_, _) => _vm.User = _userBox.Text ?? string.Empty;
        _userBox.Margin = new Thickness(0, 2, 0, 10);
        panel.Children.Add(_userBox);

        panel.Children.Add(Label(T("SettingsSync.Password")));
        _passwordBox.Text = _vm.Password;
        _passwordBox.TextChanged += (_, _) => _vm.Password = _passwordBox.Text ?? string.Empty;
        _passwordBox.Margin = new Thickness(0, 2, 0, 10);
        panel.Children.Add(_passwordBox);

        panel.Children.Add(Label(T("SettingsSync.Interval")));
        _intervalBox.Text = _vm.IntervalMinutesText;
        _intervalBox.TextChanged += (_, _) => _vm.IntervalMinutesText = _intervalBox.Text ?? string.Empty;
        _intervalBox.Margin = new Thickness(0, 2, 0, 10);
        panel.Children.Add(_intervalBox);

        panel.Children.Add(Label(T("SettingsSync.Patterns")));
        _patternsBox.Text = _vm.PatternsText;
        _patternsBox.AcceptsReturn = true;
        _patternsBox.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _patternsBox.MinHeight = 90;
        _patternsBox.TextChanged += (_, _) => _vm.PatternsText = _patternsBox.Text ?? string.Empty;
        _patternsBox.Margin = new Thickness(0, 2, 0, 4);
        panel.Children.Add(_patternsBox);

        var patternsHint = new TextBlock
        {
            Text = T("SettingsSync.PatternsHint"),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8)
        };
        ThemeBrushes.Bind(patternsHint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        panel.Children.Add(patternsHint);

        var lastSyncRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 4, 0, 6)
        };
        lastSyncRow.Children.Add(Label(T("SettingsSync.LastSync")));
        _lastSyncText.Text = _vm.LastSyncAtText;
        _lastSyncText.VerticalAlignment = VerticalAlignment.Center;
        // issue #26: значение «последняя синхронизация» привязано к вторичной кисти —
        // без неё текст был чёрным в тёмной теме (половина надписей белые, половина чёрные).
        ThemeBrushes.Bind(_lastSyncText, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        lastSyncRow.Children.Add(_lastSyncText);
        panel.Children.Add(lastSyncRow);

        panel.Children.Add(Label(T("SettingsSync.Log")));
        _logList.ItemsSource = _vm.LogEntries;
        _logList.MinHeight = 120;
        _logList.Margin = new Thickness(0, 2, 0, 8);
        panel.Children.Add(_logList);

        var scroll = new ScrollViewer { Content = panel };
        root.Children.Add(scroll);

        return root;
    }

    private async Task RunSyncAsync()
    {
        _vm.Save();
        _syncNowButton.IsEnabled = false;
        try
        {
            await _vm.SyncNowAsync();
        }
        finally
        {
            _syncNowButton.IsEnabled = _vm.CanSyncNow;
        }
        _statusText.Text = _vm.StatusText;
        _lastSyncText.Text = _vm.LastSyncAtText;
    }

    private static TextBlock Label(string text)
    {
        // issue #26 (7OH, 2026-10-28: «Синхронизация колонок - надписи по-прежнему
        // чёрные», «половина надписей в одном окне белые, половина чёрные»): подписи
        // полей формы обязаны брать кисть темы, а не дефолтный чёрный — в тёмной теме
        // без привязки текст нечитаем. Та же кисть FormLabelBrush, что у остальных форм.
        var label = new TextBlock
        {
            Text = text,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Thickness(0, 2, 0, 2)
        };
        ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "FormLabelBrush");
        return label;
    }
}
#endif