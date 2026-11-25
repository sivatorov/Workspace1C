#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C;

/// <summary>
/// Окно «Уведомления в Telegram и по email» (Avalonia/Linux, выпуск 0.3.12.69):
/// настройки бота Telegram, SMTP и фильтра событий; «Проверить» отправляет тестовое
/// сообщение с текущими значениями полей. Сохраняет <c>notifications.json</c>.
/// </summary>
public sealed class NotificationChannelsWindow : ModalWindowBase
{
    private readonly INotificationSettingsStore _store = AppServices.GetRequiredService<INotificationSettingsStore>();
    private readonly TelegramNotificationChannel _telegram = AppServices.GetRequiredService<TelegramNotificationChannel>();
    private readonly EmailNotificationChannel _email = AppServices.GetRequiredService<EmailNotificationChannel>();
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    private readonly CheckBox _telegramEnabled = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly TextBox _telegramToken = new TextBox { PasswordChar = '●' }.Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _telegramChat = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBlock _telegramStatus = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _telegramTest = new Button { MinWidth = 120 }.Styled(ControlThemes.SecondaryButton);

    private readonly CheckBox _emailEnabled = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly TextBox _smtpHost = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _smtpPort = new TextBox { Width = 90 }.Styled(ControlThemes.ModernTextBox);
    private readonly CheckBox _smtpStartTls = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly TextBox _smtpUser = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _smtpPassword = new TextBox { PasswordChar = '●' }.Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _emailFrom = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBox _emailTo = new TextBox().Styled(ControlThemes.ModernTextBox);
    private readonly TextBlock _emailStatus = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _emailTest = new Button { MinWidth = 120 }.Styled(ControlThemes.SecondaryButton);

    private readonly CheckBox _onBackup = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly CheckBox _onScheduled = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly CheckBox _onUpdates = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);
    private readonly CheckBox _onlyProblems = new CheckBox().Styled(ControlThemes.CacheCleanCheckBox);

    /// <summary>Порог «свободных лицензий меньше N» (п.5, 1.0.0.87); 0 — отключено.</summary>
    private readonly TextBox _licenseThreshold = new TextBox { Width = 110 }.Styled(ControlThemes.ModernTextBox);

    /// <summary>Период повторных оповещений о нехватке лицензий, часов.</summary>
    private readonly TextBox _licenseRepeat = new TextBox { Width = 110 }.Styled(ControlThemes.ModernTextBox);

    /// <summary>Порог «диск под копиями закончится через N дней» (п.7, 1.0.0.89); 0 — отключено.</summary>
    private readonly TextBox _diskForecastThreshold = new TextBox { Width = 110 }.Styled(ControlThemes.ModernTextBox);

    /// <summary>Период повторных оповещений о прогнозе диска, дней.</summary>
    private readonly TextBox _diskForecastRepeat = new TextBox { Width = 110 }.Styled(ControlThemes.ModernTextBox);

    public NotificationChannelsWindow()
    {
        Title = T("Notifications.Title");
        // Issue #119 (T3): целевой стартовый размер из таблицы 1.4 (паритет с WPF).
        Width = 620;
        Height = 660;
        MinWidth = 520;
        MinHeight = 480;
        FontSize = 13;
        CanResize = true;
        Content = BuildRoot();
        Fill(_store.Load());
    }

    /// <summary>Показывает окно модально (синхронно).</summary>
    public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

    private static string T(string key) => LocalizationManager.T(key);

    private static TextBlock Label(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");
        return label;
    }

    private static TextBlock Header(string text)
    {
        var header = new TextBlock
        {
            Text = text,
            FontSize = ThemeManager.FontSizeSubtitle,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Thickness(0, 12, 0, 2)
        };
        ThemeBrushes.Bind(header, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");
        return header;
    }

    private static TextBlock Hint(string text)
    {
        var hint = new TextBlock { Text = text, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        ThemeBrushes.Bind(hint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        return hint;
    }

    private static Control Row(string label, Control editor)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150, GridUnitType.Pixel)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        var caption = Label(label);
        Grid.SetColumn(caption, 0);
        grid.Children.Add(caption);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private Control BuildRoot()
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 8, 0) };

        // ===== Telegram =====
        panel.Children.Add(Header(T("Notifications.TelegramHeader")));
        _telegramEnabled.Content = T("Notifications.TelegramEnabled");
        panel.Children.Add(_telegramEnabled);
        panel.Children.Add(Row(T("Notifications.TelegramToken"), _telegramToken));
        panel.Children.Add(Row(T("Notifications.TelegramChatId"), _telegramChat));
        panel.Children.Add(Hint(T("Notifications.TelegramHint")));
        _telegramTest.Content = T("Notifications.Test");
        _telegramTest.Click += async (_, _) => await TestTelegramAsync();
        panel.Children.Add(TestRow(_telegramTest, _telegramStatus));

        // ===== Email =====
        panel.Children.Add(Header(T("Notifications.EmailHeader")));
        _emailEnabled.Content = T("Notifications.EmailEnabled");
        panel.Children.Add(_emailEnabled);
        var hostRow = new Grid();
        hostRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        hostRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        hostRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(_smtpHost, 0);
        hostRow.Children.Add(_smtpHost);
        var portLabel = Label(T("Notifications.SmtpPort"));
        portLabel.Margin = new Thickness(10, 0, 8, 0);
        Grid.SetColumn(portLabel, 1);
        hostRow.Children.Add(portLabel);
        Grid.SetColumn(_smtpPort, 2);
        hostRow.Children.Add(_smtpPort);
        panel.Children.Add(Row(T("Notifications.SmtpHost"), hostRow));
        _smtpStartTls.Content = T("Notifications.SmtpStartTls");
        panel.Children.Add(Row(string.Empty, _smtpStartTls));
        panel.Children.Add(Row(T("Notifications.SmtpUser"), _smtpUser));
        panel.Children.Add(Row(T("Notifications.SmtpPassword"), _smtpPassword));
        panel.Children.Add(Row(T("Notifications.EmailFrom"), _emailFrom));
        panel.Children.Add(Row(T("Notifications.EmailTo"), _emailTo));
        panel.Children.Add(Hint(T("Notifications.EmailHint")));
        _emailTest.Content = T("Notifications.Test");
        _emailTest.Click += async (_, _) => await TestEmailAsync();
        panel.Children.Add(TestRow(_emailTest, _emailStatus));

        // ===== События =====
        panel.Children.Add(Header(T("Notifications.EventsHeader")));
        _onBackup.Content = T("Notifications.OnBackup");
        _onScheduled.Content = T("Notifications.OnScheduledTasks");
        _onUpdates.Content = T("Notifications.OnUpdates");
        _onlyProblems.Content = T("Notifications.OnlyProblems");
        panel.Children.Add(_onBackup);
        panel.Children.Add(_onScheduled);
        panel.Children.Add(_onUpdates);
        panel.Children.Add(_onlyProblems);

        // ===== Лицензии (п.5 «Мониторинг лицензий», выпуск 1.0.0.87) =====
        panel.Children.Add(Header(T("Notifications.LicensesHeader")));
        panel.Children.Add(Hint(T("Notifications.LicensesHint")));
        panel.Children.Add(Row(T("Notifications.LicenseThreshold"), _licenseThreshold));
        panel.Children.Add(Row(T("Notifications.LicenseRepeatHours"), _licenseRepeat));
        panel.Children.Add(Hint(T("Notifications.LicenseRepeatHint")));

        // ===== Прогноз диска под копиями (п.7 «История размеров», 1.0.0.89) =====
        panel.Children.Add(Header(T("Notifications.DiskForecastHeader")));
        panel.Children.Add(Hint(T("Notifications.DiskForecastHint")));
        panel.Children.Add(Row(T("Notifications.DiskForecastThreshold"), _diskForecastThreshold));
        panel.Children.Add(Row(T("Notifications.DiskForecastRepeatDays"), _diskForecastRepeat));
        panel.Children.Add(Hint(T("Notifications.DiskForecastRepeatHint")));

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var ok = new Button { Content = T("Common.Save"), Width = 120 }.Styled(ControlThemes.DialogConfirmButton);
        ok.Click += (_, _) => OnSave();
        var cancel = new Button { Content = T("Common.Cancel"), Width = 100 }.Styled(ControlThemes.DialogCancelButton);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        bottom.Children.Add(ok);
        bottom.Children.Add(cancel);

        var title = new TextBlock
        {
            Text = T("Notifications.Title"),
            FontSize = 15,
            FontWeight = Avalonia.Media.FontWeight.SemiBold
        };
        ThemeBrushes.Bind(title, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);
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

    private static Control TestRow(Button button, TextBlock status)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        Grid.SetColumn(button, 0);
        row.Children.Add(button);
        status.Margin = new Thickness(10, 0, 0, 0);
        ThemeBrushes.Bind(status, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
        Grid.SetColumn(status, 1);
        row.Children.Add(status);
        return row;
    }

    private void Fill(NotificationChannelSettings s)
    {
        _telegramEnabled.IsChecked = s.TelegramEnabled;
        _telegramToken.Text = s.TelegramBotToken;
        _telegramChat.Text = s.TelegramChatId;
        _emailEnabled.IsChecked = s.EmailEnabled;
        _smtpHost.Text = s.SmtpHost;
        _smtpPort.Text = s.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _smtpStartTls.IsChecked = s.SmtpUseStartTls;
        _smtpUser.Text = s.SmtpUser;
        _smtpPassword.Text = s.SmtpPassword;
        _emailFrom.Text = s.EmailFrom;
        _emailTo.Text = s.EmailTo;
        _onBackup.IsChecked = s.NotifyOnBackup;
        _onScheduled.IsChecked = s.NotifyOnScheduledTasks;
        _onUpdates.IsChecked = s.NotifyOnUpdates;
        _onlyProblems.IsChecked = s.OnlyProblems;
        _licenseThreshold.Text = s.LicenseFreeThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _licenseRepeat.Text = s.LicenseAlertRepeatHours.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _diskForecastThreshold.Text = s.DiskForecastThresholdDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _diskForecastRepeat.Text = s.DiskForecastAlertRepeatDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private NotificationChannelSettings Collect()
    {
        var port = int.TryParse(_smtpPort.Text?.Trim(), out var p) ? p : 587;
        return NotificationSettingsStore.Normalize(new NotificationChannelSettings
        {
            TelegramEnabled = _telegramEnabled.IsChecked == true,
            TelegramBotToken = _telegramToken.Text ?? string.Empty,
            TelegramChatId = _telegramChat.Text ?? string.Empty,
            EmailEnabled = _emailEnabled.IsChecked == true,
            SmtpHost = _smtpHost.Text ?? string.Empty,
            SmtpPort = port,
            SmtpUseStartTls = _smtpStartTls.IsChecked == true,
            SmtpUser = _smtpUser.Text ?? string.Empty,
            SmtpPassword = _smtpPassword.Text ?? string.Empty,
            EmailFrom = _emailFrom.Text ?? string.Empty,
            EmailTo = _emailTo.Text ?? string.Empty,
            NotifyOnBackup = _onBackup.IsChecked == true,
            NotifyOnScheduledTasks = _onScheduled.IsChecked == true,
            NotifyOnUpdates = _onUpdates.IsChecked == true,
            OnlyProblems = _onlyProblems.IsChecked == true,
            LicenseFreeThreshold = ParseNonNegative(_licenseThreshold.Text, 0),
            LicenseAlertRepeatHours = int.TryParse(_licenseRepeat.Text?.Trim(), out var repeat) && repeat > 0
                ? repeat
                : 24,
            DiskForecastThresholdDays = ParseNonNegative(_diskForecastThreshold.Text, 0),
            DiskForecastAlertRepeatDays = int.TryParse(_diskForecastRepeat.Text?.Trim(), out var repeatDays) && repeatDays > 0
                ? repeatDays
                : 1,
        });
    }

    private static int ParseNonNegative(string? text, int fallback) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : fallback;

    private static NotificationMessage TestMessage() => new()
    {
        Title = LocalizationManager.T("App.Title"),
        Message = LocalizationManager.T("Notifications.TestMessage"),
        Kind = NotificationKind.Info,
        Event = NotificationEvent.ManualTest
    };

    private async System.Threading.Tasks.Task TestTelegramAsync()
    {
        _telegramTest.IsEnabled = false;
        _telegramStatus.Text = T("Notifications.Testing");
        try
        {
            var (ok, error) = await _telegram.TestAsync(Collect(), TestMessage());
            _telegramStatus.Text = ok
                ? string.Format(T("Notifications.TestOk"), "Telegram")
                : string.Format(T("Notifications.TestFailed"), "Telegram", error);
        }
        catch (Exception ex)
        {
            _telegramStatus.Text = string.Format(T("Notifications.TestFailed"), "Telegram", ex.Message);
        }
        finally
        {
            _telegramTest.IsEnabled = true;
        }
    }

    private async System.Threading.Tasks.Task TestEmailAsync()
    {
        _emailTest.IsEnabled = false;
        _emailStatus.Text = T("Notifications.Testing");
        try
        {
            var (ok, error) = await _email.TestAsync(Collect(), TestMessage());
            _emailStatus.Text = ok
                ? string.Format(T("Notifications.TestOk"), "email")
                : string.Format(T("Notifications.TestFailed"), "email", error);
        }
        catch (Exception ex)
        {
            _emailStatus.Text = string.Format(T("Notifications.TestFailed"), "email", ex.Message);
        }
        finally
        {
            _emailTest.IsEnabled = true;
        }
    }

    private void OnSave()
    {
        try
        {
            _store.Save(Collect());
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _dialogs.ShowWarning(string.Format(T("Notifications.SaveFailed"), ex.Message), T("Notifications.Title"));
        }
    }
}
#endif
