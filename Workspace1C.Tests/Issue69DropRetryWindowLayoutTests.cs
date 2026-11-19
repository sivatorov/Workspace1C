using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты-стражи диалога повтора удаления информационной базы из кластера (issue #69):
/// у WPF-окна <c>RacInfobaseDropRetryWindow.xaml</c> и Avalonia-построителя
/// <c>RacInfobaseDropRetryWindow.Avalonia.cs</c> есть поля «Логин»/«Пароль»,
/// кнопки «Повторить попытку» (default) и «Отмена» (cancel), а подписи полей
/// используют кисть <c>FormLabelBrush</c> (issue #61). Ключи локализации настроены
/// в ru.json И en.json.
/// </summary>
public sealed class Issue69DropRetryWindowLayoutTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Views")) &&
                Directory.Exists(Path.Combine(dir.FullName, "Workspace1C.Tests")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Корень репозитория не найден из тестового каталога.");
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    [Fact]
    public void WpfWindow_HasLoginPasswordFields_AndRetryCancelButtons()
    {
        // WPF-разметка диалога повтора (issue #69): вместо MessageBox с OK — поля
        // «Логин»/«Пароль», кнопка «Повторить попытку» (IsDefault) и «Отмена» (IsCancel).
        var xaml = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml");

        Assert.Contains("LoginBox", xaml, StringComparison.Ordinal);
        Assert.Contains("PasswordBox", xaml, StringComparison.Ordinal);
        Assert.Contains("RetryButton", xaml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", xaml, StringComparison.Ordinal);
        // Подписи полей — кисть FormLabelBrush (issue #61): новое окно сразу с ней.
        Assert.Contains("FormLabelBrush", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfWindow_UsesRetryLocalizationKeys()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml");

        Assert.Contains("ServerMonitor.Infobase.Drop.RetryLogin", xaml, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryPassword", xaml, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryButton", xaml, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.CancelButton", xaml, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryTitle", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_HasSameFieldsAndButtons()
    {
        // Avalonia/Linux-построитель (issue #69): те же поля и кнопки, что в WPF-окне;
        // подписи через AvaloniaUi.Label → кисть FormLabelBrush (issue #61).
        var source = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.Avalonia.cs");

        Assert.Contains("_loginBox", source, StringComparison.Ordinal);
        Assert.Contains("_passwordBox", source, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryButton", source, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.CancelButton", source, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryLogin", source, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.RetryPassword", source, StringComparison.Ordinal);
        Assert.Contains("IsDefault = true", source, StringComparison.Ordinal);
        Assert.Contains("IsCancel = true", source, StringComparison.Ordinal);
        Assert.Contains("AvaloniaUi.Label", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_RetryKeys_ExistInRuAndEn_WithText()
    {
        var ru = ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json");
        var en = ReadProjectFile("Workspace1C", "Localization", "Languages", "en.json");

        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryTitle\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryPrompt\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryLogin\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryPassword\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryButton\"", ru, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.CancelButton\"", ru, StringComparison.Ordinal);

        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryTitle\"", en, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryPrompt\"", en, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryLogin\"", en, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryPassword\"", en, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.RetryButton\"", en, StringComparison.Ordinal);
        Assert.Contains("\"ServerMonitor.Infobase.Drop.CancelButton\"", en, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_RussianRetryPrompt_IsHumanReadable()
    {
        // Человекочитаемый русский текст (в тестовой среде LocalizationManager не
        // инициализирован — читаем словарь напрямую, как тесты покрытия локализации).
        using var doc = JsonDocument.Parse(
            ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json"));
        var strings = doc.RootElement.GetProperty("strings");
        Assert.Contains("администратора", strings.GetProperty("ServerMonitor.Infobase.Drop.RetryPrompt").GetString());
        Assert.Equal("Повторить попытку", strings.GetProperty("ServerMonitor.Infobase.Drop.RetryButton").GetString());
        Assert.Equal("Отмена", strings.GetProperty("ServerMonitor.Infobase.Drop.CancelButton").GetString());
        Assert.Equal("Пользователь", strings.GetProperty("ServerMonitor.Infobase.Drop.RetryLogin").GetString());
        Assert.Equal("Пароль", strings.GetProperty("ServerMonitor.Infobase.Drop.RetryPassword").GetString());
    }

    [Fact]
    public void WpfWindow_HasTerminateSessionsOption_AndSuggestionText()
    {
        // issue #69, комментарий 7OH от 2026-10-26: диалог поясняет типовую ошибку rac
        // (SuggestionText по ClassifyDropError) и предлагает «Завершить сеансы и повторить»
        // (TerminateSessionsButton), когда базу занимают активные сеансы/соединения.
        var xaml = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml");
        var codeBehind = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml.cs");

        Assert.Contains("TerminateSessionsButton", xaml, StringComparison.Ordinal);
        Assert.Contains("SuggestionText", xaml, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.TerminateSessionsButton", xaml, StringComparison.Ordinal);
        Assert.Contains("ClassifyDropError", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OnTerminateSessions_Click", codeBehind, StringComparison.Ordinal);
        Assert.Contains("TerminateSessions", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_HasTerminateSessionsOption()
    {
        // Avalonia/Linux-построитель (issue #69): та же кнопка «Завершить сеансы и
        // повторить» (показывается при занятой базе) и подсказка по типу ошибки.
        var source = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.Avalonia.cs");

        Assert.Contains("ServerMonitor.Infobase.Drop.TerminateSessionsButton", source, StringComparison.Ordinal);
        Assert.Contains("OnTerminateSessions", source, StringComparison.Ordinal);
        Assert.Contains("ClassifyDropError", source, StringComparison.Ordinal);
        Assert.Contains("BuildDropRetryHint", source, StringComparison.Ordinal);
        Assert.Contains("TerminateSessions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_NewIssue69Keys_ExistInRuAndEn()
    {
        var ru = ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json");
        var en = ReadProjectFile("Workspace1C", "Localization", "Languages", "en.json");

        foreach (var key in new[]
        {
            "ServerMonitor.Infobase.Drop.RetryFailedFormat",
            "ServerMonitor.Infobase.Drop.TerminateSessionsButton",
            "ServerMonitor.Infobase.Drop.TerminatedFormat",
            "ServerMonitor.Infobase.Drop.TerminateFailed",
            "ServerMonitor.Infobase.Drop.HintAuth",
            "ServerMonitor.Infobase.Drop.HintSessions",
            "ServerMonitor.Infobase.Drop.HintDatabase"
        })
        {
            Assert.Contains($"\"{key}\"", ru, StringComparison.Ordinal);
            Assert.Contains($"\"{key}\"", en, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Localization_RussianRetryFailedAndHints_AreHumanReadable()
    {
        using var doc = JsonDocument.Parse(
            ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json"));
        var strings = doc.RootElement.GetProperty("strings");

        Assert.Contains("не удалась", strings.GetProperty("ServerMonitor.Infobase.Drop.RetryFailedFormat").GetString());
        Assert.Contains("{0}", strings.GetProperty("ServerMonitor.Infobase.Drop.RetryFailedFormat").GetString());
        Assert.Equal("Завершить сеансы и повторить",
            strings.GetProperty("ServerMonitor.Infobase.Drop.TerminateSessionsButton").GetString());
        Assert.Contains("сеанс", strings.GetProperty("ServerMonitor.Infobase.Drop.HintSessions").GetString());
        Assert.Contains("администратор", strings.GetProperty("ServerMonitor.Infobase.Drop.HintAuth").GetString());
        Assert.Contains("СУБД", strings.GetProperty("ServerMonitor.Infobase.Drop.HintDatabase").GetString());
    }

    [Fact]
    public void WpfWindow_HasEnoughHeight_ForRetryButtons()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «Окошко с кнопкой повторить надо бы
        // выше сделать - кнопки не влезли» — высота увеличена, кнопки не обрезаются.
        var xaml = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml");

        Assert.Contains("Height=\"440\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"400\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Height=\"320\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_HasEnoughHeight_ForRetryButtons()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.Avalonia.cs");

        Assert.Contains("Height = 440;", source, StringComparison.Ordinal);
        Assert.Contains("MinHeight = 400;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Height = 320;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_PreloadPasswordFromPreviousAttempt()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «Пароль сохранять между попытками» —
        // оба окна предзаполняют поле пароля значением с прошлой попытки.
        var codeBehind = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml.cs");
        var avalonia = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.Avalonia.cs");

        Assert.Contains("password ?? string.Empty", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Password = password ?? string.Empty", avalonia, StringComparison.Ordinal);
        // Конструкторы обеих платформ принимают пароль третьим параметром.
        Assert.Contains("string errorText, string? login = null, string? password = null", codeBehind, StringComparison.Ordinal);
        Assert.Contains("string errorText, string? login = null, string? password = null", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_HintSessions_HasNoSessionsTabSuggestion()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «Надо удалить эту фразу до слова "или".
        // Сразу "Нажмите ..."» — просьба завершить сеансы во вкладке «Сеансы» убрана
        // из подсказки в обоих языках, остаётся кнопка «Завершить сеансы и повторить».
        var ru = ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json");
        var en = ReadProjectFile("Workspace1C", "Localization", "Languages", "en.json");

        // Убрана «Завершите сеансы во вкладке «Сеансы» или ...».
        Assert.DoesNotContain("во вкладке", ru, StringComparison.Ordinal);
        Assert.DoesNotContain("Sessions\" tab", en, StringComparison.Ordinal);
        // Осталась прямая подсказка нажать кнопку повтора.
        Assert.Contains("Завершить сеансы и повторить", ru, StringComparison.Ordinal);
        Assert.Contains("Terminate sessions and retry", en, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfWindow_HasBlockScheduledJobsCheckbox()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «Если соединение не снимается —
        // предлагать на форме галочкой "Установить блокировку регламентных заданий"».
        // WPF: CheckBox + ключ локализации; code-behind читает его в оба обработчика
        // («Завершить сеансы и повторить» и «Повторить попытку»).
        var xaml = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml");
        var codeBehind = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml.cs");

        Assert.Contains("BlockScheduledJobsCheckBox", xaml, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.BlockScheduledJobs", xaml, StringComparison.Ordinal);
        Assert.Contains("BlockScheduledJobs", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BlockScheduledJobsCheckBox.IsChecked", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_HasBlockScheduledJobsCheckbox()
    {
        var source = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.Avalonia.cs");

        Assert.Contains("_blockScheduledJobsCheckBox", source, StringComparison.Ordinal);
        Assert.Contains("ServerMonitor.Infobase.Drop.BlockScheduledJobs", source, StringComparison.Ordinal);
        Assert.Contains("BlockScheduledJobs", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_BlockScheduledJobsKeys_ExistInRuAndEn_WithRussianText()
    {
        var ru = ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json");
        var en = ReadProjectFile("Workspace1C", "Localization", "Languages", "en.json");

        foreach (var key in new[]
        {
            "ServerMonitor.Infobase.Drop.BlockScheduledJobs",
            "ServerMonitor.Infobase.Drop.BlockedFormat",
            "ServerMonitor.Infobase.Drop.BlockFailed",
            "ServerMonitor.Infobase.Drop.TerminateRetryFailedFormat"
        })
        {
            Assert.Contains($"\"{key}\"", ru, StringComparison.Ordinal);
            Assert.Contains($"\"{key}\"", en, StringComparison.Ordinal);
        }

        using var doc = JsonDocument.Parse(ru);
        var strings = doc.RootElement.GetProperty("strings");
        Assert.Equal("Установить блокировку регламентных заданий",
            strings.GetProperty("ServerMonitor.Infobase.Drop.BlockScheduledJobs").GetString());
        Assert.Contains("не удалась",
            strings.GetProperty("ServerMonitor.Infobase.Drop.TerminateRetryFailedFormat").GetString());
        Assert.Contains("{0}",
            strings.GetProperty("ServerMonitor.Infobase.Drop.TerminateRetryFailedFormat").GetString());
    }

    [Fact]
    public void ServerMonitorWindow_RetryDialog_OpensWithOwner()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «Диалог появился ... на заднем плане
        // (а должен быть по идее поверх текущего)». Диалог повтора открывается с владельцем:
        // WPF — активное окно приложения (само «Серверы 1С», запасной вариант — MainWindow,
        // WindowOwnerResolver), Avalonia — само окно монитора (ShowSync(this)).
        var wpf = ReadProjectFile("Workspace1C", "Views", "ServerMonitorWindow.xaml.cs");
        var avalonia = ReadProjectFile("Workspace1C", "Views", "ServerMonitorWindow.Avalonia.cs");

        Assert.Contains("WindowOwnerResolver.ActiveOrMain()", wpf, StringComparison.Ordinal);
        Assert.Contains("Owner = WindowOwnerResolver.ActiveOrMain() ?? this", wpf, StringComparison.Ordinal);
        Assert.Contains("retry.ShowSync(this)", avalonia, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerMonitorViewModel_BlockFailure_HasNoSeparateWarningWindow()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «Этого диалога вообще быть не должно».
        // Неудача установки блокировки регламентных заданий показывается строкой в диалоге
        // повтора (готовое сообщение BlockRetryFailedFormat), а не отдельным ShowWarningOnUi.
        var source = ReadProjectFile("Workspace1C", "ViewModels", "ServerMonitorViewModel.cs");

        Assert.Contains("ServerMonitor.Infobase.Drop.BlockRetryFailedFormat", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ServerMonitor.Infobase.Drop.BlockFailed\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_BlockRetryAndNoDbParamsKeys_ExistInRuAndEn()
    {
        var ru = ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json");
        var en = ReadProjectFile("Workspace1C", "Localization", "Languages", "en.json");

        foreach (var key in new[]
        {
            "ServerMonitor.Infobase.Drop.BlockRetryFailedFormat",
            "ServerMonitor.Infobase.Drop.BlockNoDbParams"
        })
        {
            Assert.Contains($"\"{key}\"", ru, StringComparison.Ordinal);
            Assert.Contains($"\"{key}\"", en, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Localization_RussianBlockRetryFormat_IsReadableWithTwoPlaceholders()
    {
        using var doc = JsonDocument.Parse(
            ReadProjectFile("Workspace1C", "Localization", "Languages", "ru.json"));
        var strings = doc.RootElement.GetProperty("strings");

        var format = strings.GetProperty("ServerMonitor.Infobase.Drop.BlockRetryFailedFormat").GetString();
        Assert.Contains("Повторная попытка удаления не удалась", format);
        Assert.Contains("{0}", format);
        Assert.Contains("{1}", format);
        Assert.Contains("блокировку", format);
        Assert.Contains("Не удалось определить параметры",
            strings.GetProperty("ServerMonitor.Infobase.Drop.BlockNoDbParams").GetString());
    }

    [Fact]
    public void WpfWindow_BlockScheduledJobsCheckbox_VisibleFromFirstPrompt()
    {
        // issue #69, комментарий 7OH от 2026-10-26: «И может быть сразу галочку показывать -
        // ещё при первом запросе логина и пароля?». Галочка «Установить блокировку
        // регламентных заданий» видна с ПЕРВОГО показа диалога: в разметке у неё НЕТ
        // Visibility="Collapsed", и code-behind не прячет/не показывает её по типу ошибки.
        var xaml = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml");
        var codeBehind = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.xaml.cs");

        // Открывающий тег галочки не содержит атрибута видимости (видима по умолчанию).
        Assert.Contains("BlockScheduledJobsCheckBox\" Margin=\"0,10,0,0\">", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BlockScheduledJobsCheckBox.Visibility", codeBehind, StringComparison.Ordinal);
        // Кнопка «Завершить сеансы и повторить» по-прежнему только для занятой базы.
        Assert.Contains("TerminateSessionsButton.Visibility = Visibility.Visible", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaWindow_BlockScheduledJobsCheckbox_VisibleFromFirstPrompt()
    {
        // Аналог WPF-стража для Avalonia: галочка блокировки РЗ не скрывается по типу
        // ошибки и всегда добавляется в форму (видна уже при первом запросе логина/пароля).
        var source = ReadProjectFile("Workspace1C", "Views", "RacInfobaseDropRetryWindow.Avalonia.cs");

        Assert.DoesNotContain("_blockScheduledJobsCheckBox.IsVisible", source, StringComparison.Ordinal);
        Assert.Contains("form.Children.Add(_blockScheduledJobsCheckBox)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerMonitorViewModel_ReleasePassesInfobaseCredentials_ToDisconnectAndTerminate()
    {
        // issue #69, комментарий 7OH от 2026-10-27 (лог 23:56): «connection disconnect»
        // и «session terminate» защищённой базы должны получать --infobase-user/
        // --infobase-pwd — иначе rac возвращает «Недостаточно прав пользователя на
        // информационную базу», и «сеансы не снимаются».
        var source = ReadProjectFile("Workspace1C", "ViewModels", "ServerMonitorViewModel.cs");

        Assert.Contains("connection.Id, connection.ProcessId," + Environment.NewLine + "                            infobaseUser, infobasePassword",
            source, StringComparison.Ordinal);
        Assert.Contains("session.Id, processId," + Environment.NewLine + "                            infobaseUser, infobasePassword",
            source, StringComparison.Ordinal);
        // Креды из диалога повтора доходят до разрыва при каждом release.
        Assert.Contains("row.InfobaseId, clusterId, infobaseUser, infobasePassword", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerMonitorViewModel_BlockScheduledJobs_UsesInfobaseCredentials_ForInfoAndUpdate()
    {
        // issue #69, комментарий 7OH от 2026-10-27: «Не удаётся теперь установить
        // блокировку» — догрузка параметров СУБД через «infobase info» и сам rac
        // «infobase update --scheduled-jobs-deny=on» выполняются с кредами ИБ.
        var source = ReadProjectFile("Workspace1C", "ViewModels", "ServerMonitorViewModel.cs");

        Assert.Contains("row, clusterId, infobaseUser, infobasePassword", source, StringComparison.Ordinal);
        Assert.Contains("InfobaseUser = infobaseUser ?? string.Empty", source, StringComparison.Ordinal);
        Assert.Contains("InfobasePassword = infobasePassword ?? string.Empty", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerMonitorViewModel_ServiceConnectionFailure_IsSkippedNotFatal()
    {
        // issue #69, комментарий 7OH от 2026-10-27: «Служебное соединение не может быть
        // удалено» — штатный отказ rac, который НЕ должен прерывать разрыв остальных
        // соединений/сеансов и «infobase drop». Проверяем, что guard существует и
        // применяется и к разрыву соединений, и к завершению сеансов.
        var source = ReadProjectFile("Workspace1C", "ViewModels", "ServerMonitorViewModel.cs");

        Assert.Contains("internal static bool IsServiceConnectionError", source, StringComparison.Ordinal);
        Assert.Contains("if (IsServiceConnectionError(error))" + Environment.NewLine + "                        continue;",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerMonitorWindow_DropOperation_PausesAutoRefresh_AndRestores()
    {
        // issue #69, комментарий 7OH от 2026-10-27: «отключать автообновление данных на
        // время, если оно было включено» — таймер останавливается на время удаления
        // базы (иначе перезапрашиваемые сеансы/соединения мешают разрыву) и
        // восстанавливается в исходное состояние после.
        var source = ReadProjectFile("Workspace1C", "ViewModels", "ServerMonitorViewModel.cs");

        Assert.Contains("var autoRefreshWasEnabled = IsAutoRefreshEnabled;", source, StringComparison.Ordinal);
        Assert.Contains("SetAutoRefreshEnabled(false);", source, StringComparison.Ordinal);
        Assert.Contains("if (autoRefreshWasEnabled)", source, StringComparison.Ordinal);
        Assert.Contains("SetAutoRefreshEnabled(true);", source, StringComparison.Ordinal);
    }
}