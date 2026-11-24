using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #156 «Режим запуска в разделе "Текущая сессия" работает не по описанию».
/// <para>
/// Зафиксированная семантика: S1 — по умолчанию переопределение клиента/разрядности
/// разовое (после успешного запуска «Предприятия» снова «Авто»); S2 — скрепка
/// сохраняет значения до отжатия; S3/S4 — ни скрепка, ни значения не переживают
/// перезапуск (в настройки не пишутся, при загрузке игнорируются); S5 — при
/// «Авто»/«Авто» запуск штатный, <c>Infobase.LaunchMode</c> не меняется; S6 — сброс
/// только для успешного запуска «Предприятия» по умолчанию.
/// </para>
/// <para>
/// Чистая логика проверяется напрямую через <see cref="SessionLaunchPolicy"/>;
/// привязка к WPF/Avalonia ViewModel — стражами по исходникам (как принято в проекте):
/// WPF-MainViewModel в тестах не конструируется (нужен UI-поток и диалоги).
/// </para>
/// </summary>
[Collection("PlatformVersionCache")]
public sealed class Issue156SessionOneShotTests
{
    // ======================= Чистая логика =======================

    [Fact]
    public void HasOverrides_AutoAuto_False()
    {
        Assert.False(SessionLaunchPolicy.HasOverrides(
            SessionClientMode.Auto, SessionArchitectureMode.Auto));
    }

    [Theory]
    [InlineData(SessionClientMode.Ordinary, SessionArchitectureMode.Auto)]
    [InlineData(SessionClientMode.Thick, SessionArchitectureMode.Auto)]
    [InlineData(SessionClientMode.Thin, SessionArchitectureMode.Auto)]
    [InlineData(SessionClientMode.Auto, SessionArchitectureMode.X86)]
    [InlineData(SessionClientMode.Auto, SessionArchitectureMode.X64)]
    [InlineData(SessionClientMode.Thick, SessionArchitectureMode.X64)]
    public void HasOverrides_AnyNonAuto_True(SessionClientMode client, SessionArchitectureMode arch)
    {
        Assert.True(SessionLaunchPolicy.HasOverrides(client, arch));
    }

    [Fact]
    public void ShouldResetAfterLaunch_EnterpriseSuccessUnpinnedWithOverrides_True()
    {
        // S1: база настроена «Тонкий», в сессии выбран «Толстый» → запуск толстым,
        // после успеха переключатели возвращаются в «Авто».
        Assert.True(SessionLaunchPolicy.ShouldResetAfterLaunch(
            pinned: false,
            SessionClientMode.Thick,
            SessionArchitectureMode.Auto,
            OneCLaunchMode.Enterprise,
            launchSucceeded: true));
    }

    [Fact]
    public void ShouldResetAfterLaunch_Pinned_False()
    {
        // S2: скрепка включена — значения сохраняются до отжатия.
        Assert.False(SessionLaunchPolicy.ShouldResetAfterLaunch(
            pinned: true,
            SessionClientMode.Thick,
            SessionArchitectureMode.X64,
            OneCLaunchMode.Enterprise,
            launchSucceeded: true));
    }

    [Fact]
    public void ShouldResetAfterLaunch_FailedLaunch_False()
    {
        // S6/R2: неудачный запуск разовый выбор не расходует.
        Assert.False(SessionLaunchPolicy.ShouldResetAfterLaunch(
            pinned: false,
            SessionClientMode.Thick,
            SessionArchitectureMode.Auto,
            OneCLaunchMode.Enterprise,
            launchSucceeded: false));
    }

    [Fact]
    public void ShouldResetAfterLaunch_Configurator_False()
    {
        // S6: «Конфигуратор» задаёт клиента сам и к «Текущей сессии» не относится.
        Assert.False(SessionLaunchPolicy.ShouldResetAfterLaunch(
            pinned: false,
            SessionClientMode.Thin,
            SessionArchitectureMode.Auto,
            OneCLaunchMode.Configurator,
            launchSucceeded: true));
    }

    [Fact]
    public void ShouldResetAfterLaunch_NoOverrides_False()
    {
        // S5: при «Авто»/«Авто» переопределений нет — запуск штатный, сбрасывать нечего.
        Assert.False(SessionLaunchPolicy.ShouldResetAfterLaunch(
            pinned: false,
            SessionClientMode.Auto,
            SessionArchitectureMode.Auto,
            OneCLaunchMode.Enterprise,
            launchSucceeded: true));
    }

    [Fact]
    public void ShouldPersistValues_AlwaysFalse()
    {
        // S3/S4: ни значения, ни скрепка не сохраняются — независимо от pin.
        Assert.False(SessionLaunchPolicy.ShouldPersistValues(pinned: false));
        Assert.False(SessionLaunchPolicy.ShouldPersistValues(pinned: true));
    }

    [Fact]
    public void EnterpriseLaunch_ThinSettingAndThickSession_UsesSessionThenResets()
    {
        // Сценарий из issue: у базы «Тонкий», в сессии «Толстый».
        // Переопределение применяется (HasOverrides), а после успешного запуска
        // «Предприятия» — расходуется (ShouldResetAfterLaunch).
        const SessionClientMode session = SessionClientMode.Thick;
        const SessionArchitectureMode arch = SessionArchitectureMode.Auto;

        Assert.True(SessionLaunchPolicy.HasOverrides(session, arch));
        Assert.True(SessionLaunchPolicy.ShouldResetAfterLaunch(
            pinned: false, session, arch, OneCLaunchMode.Enterprise, launchSucceeded: true));

        // Привязка к коду WPF: переопределение применяется в ветке default (Enterprise),
        // а сброс вызывается сразу после успешного Launch.
        var wpfLaunch = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Launch.cs"));
        Assert.Contains("private bool LaunchEnterpriseWithSessionOverrides", wpfLaunch, StringComparison.Ordinal);
        Assert.Contains("SessionLaunchPolicy.ShouldResetAfterLaunch(", wpfLaunch, StringComparison.Ordinal);
        Assert.Contains("OneCLaunchMode.Enterprise, ok))", wpfLaunch, StringComparison.Ordinal);

        // Привязка к коду Avalonia: LaunchViewModel сообщает о расходовании override
        // только при успешном запуске; без переопределений (overrides is null) —
        // сразу штатный запуск, сброса нет.
        var avaloniaLaunchVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "LaunchViewModel.Avalonia.cs"));
        Assert.Contains("public Action? EnterpriseOverridesConsumed", avaloniaLaunchVm, StringComparison.Ordinal);
        var avaloniaEnterprise = SliceMethod(
            avaloniaLaunchVm, "private bool LaunchEnterprise(Infobase infobase)", "public void Launch(object? parameter)");
        Assert.Contains("overrides is null", avaloniaEnterprise, StringComparison.Ordinal);
        Assert.Contains("if (ok)", avaloniaEnterprise, StringComparison.Ordinal);
        Assert.Contains("EnterpriseOverridesConsumed?.Invoke();", avaloniaEnterprise, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitThin32Launch_DoesNotConsumeSessionOverride()
    {
        // S6: явные Thin32/Thick32/Thin64/Thick64 и «Конфигуратор» переопределение
        // не расходуют — в этих ветках нет ни политики, ни вызова сброса.
        var wpfLaunch = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Launch.cs"));
        var wpfSwitch = SliceMethod(wpfLaunch, "switch (kind)", "default:");
        Assert.DoesNotContain("SessionLaunchPolicy", wpfSwitch, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetSessionOverridesAfterLaunch", wpfSwitch, StringComparison.Ordinal);
        Assert.Contains("case LaunchKind.Thin32:", wpfSwitch, StringComparison.Ordinal);
        Assert.Contains("case LaunchKind.Configurator:", wpfSwitch, StringComparison.Ordinal);

        var avaloniaLaunchVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "LaunchViewModel.Avalonia.cs"));
        var avaloniaSwitch = SliceMethod(avaloniaLaunchVm, "LaunchKind.Thin32 =>", "_ => LaunchEnterprise(selected)");
        Assert.DoesNotContain("EnterpriseOverridesConsumed", avaloniaSwitch, StringComparison.Ordinal);
        // Ровно один вызов — внутри LaunchEnterprise (после успешного запуска с override).
        Assert.Equal(1, CountOccurrences(avaloniaLaunchVm, "EnterpriseOverridesConsumed?.Invoke()"));
    }

    [Fact]
    public void AltNAndBatchLaunch_DoNotApplySessionOverrides()
    {
        // Пункт 4 задачи A1: Alt+N и массовый запуск идут напрямую через лаунчер,
        // override «Текущей сессии» к ним не применяется — значит и сброс не нужен.
        var wpfCommands = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Commands.cs"));
        var wpfAltN = SliceMethod(wpfCommands, "private async Task LaunchFavoriteByHotkeyCoreAsync", "catch (Exception ex)");
        Assert.Contains("_launcher.Launch(ib, OneCLaunchMode.Enterprise)", wpfAltN, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionLaunchPolicy", wpfAltN, StringComparison.Ordinal);

        var wpfTools = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Tools.cs"));
        var wpfBatch = SliceMethod(wpfTools, "private void ExecuteBatchLaunchCore", "SyncMessage = string.Format(");
        Assert.DoesNotContain("SessionLaunchPolicy", wpfBatch, StringComparison.Ordinal);
        Assert.DoesNotContain("SessionClientMode", wpfBatch, StringComparison.Ordinal);

        // Avalonia: трей и Alt+E тоже стартуют напрямую, без переопределений.
        var avaloniaLaunch = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Launch.cs"));
        var avaloniaTray = SliceMethod(avaloniaLaunch, "private async Task LaunchFromTrayCoreAsync", "if (ok)");
        Assert.DoesNotContain("EnterpriseOverridesConsumed", avaloniaTray, StringComparison.Ordinal);
        var avaloniaBatch = SliceMethod(avaloniaLaunch, "private async Task LaunchAllBookmarksCoreAsync", "catch (Exception ex)");
        Assert.DoesNotContain("EnterpriseOverridesConsumed", avaloniaBatch, StringComparison.Ordinal);

        // WPF-вариант LaunchViewModel не создаётся ни одной ViewModel — запуск идёт
        // через MainViewModel.LaunchEnterpriseWithSessionOverrides (рефакторинг не нужен).
        var wpfLaunchVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "LaunchViewModel.cs"));
        Assert.DoesNotContain("EnterpriseOverridesConsumed", wpfLaunchVm, StringComparison.Ordinal);
    }

    // ======================= Загрузка и сохранение настроек =======================

    [Fact]
    public void LoadSettings_NonAutoSessionValues_AreIgnored_StartsInAuto()
    {
        // S4: сохранённые SessionClientMode="Thick"/SessionArchitecture="X64"
        // читаются без ошибок, но игнорируются — после старта всегда «Авто»/«Авто».
        var wpfVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.cs"));
        var wpfLoad = SliceMethod(wpfVm, "Issue #156 (S4): «Текущая сессия» — runtime-only", "// Режим «Разрядности по умолчанию»");
        Assert.Contains("_sessionClientMode = SessionClientMode.Auto;", wpfLoad, StringComparison.Ordinal);
        Assert.Contains("_sessionArchitecture = SessionArchitectureMode.Auto;", wpfLoad, StringComparison.Ordinal);
        Assert.Contains("_sessionPinned = false;", wpfLoad, StringComparison.Ordinal);
        Assert.Contains("OneCLauncher.SessionArchitecture = SessionArchitectureMode.Auto;", wpfLoad, StringComparison.Ordinal);
        Assert.DoesNotContain("settings.SessionClientMode", wpfLoad, StringComparison.Ordinal);
        Assert.DoesNotContain("settings.SessionArchitecture", wpfLoad, StringComparison.Ordinal);

        var avaloniaVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs"));
        var avaloniaLoad = SliceMethod(avaloniaVm, "Issue #156 (S4): «Текущая сессия» — runtime-only", "// Общая авторизация для всех ИБ");
        Assert.Contains("_sessionClient = \"Авто\";", avaloniaLoad, StringComparison.Ordinal);
        Assert.Contains("_sessionArch = \"Авто\";", avaloniaLoad, StringComparison.Ordinal);
        Assert.Contains("_sessionPinned = false;", avaloniaLoad, StringComparison.Ordinal);
        Assert.Contains("OneCLauncher.SessionArchitecture = Models.SessionArchitectureMode.Auto;", avaloniaLoad, StringComparison.Ordinal);
        Assert.DoesNotContain("_settings.SessionClientMode", avaloniaLoad, StringComparison.Ordinal);
        Assert.DoesNotContain("_settings.SessionArchitecture", avaloniaLoad, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveSettings_DoesNotPersistSessionValues()
    {
        // S4: значения «Текущей сессии» больше не пишутся в настройки.
        var wpfSave = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Launch.cs"));
        Assert.DoesNotContain("s.SessionClientMode", wpfSave, StringComparison.Ordinal);
        Assert.DoesNotContain("s.SessionArchitecture", wpfSave, StringComparison.Ordinal);

        var avaloniaVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs"));
        Assert.DoesNotContain("_settings.SessionClientMode", avaloniaVm, StringComparison.Ordinal);
        Assert.DoesNotContain("_settings.SessionArchitecture", avaloniaVm, StringComparison.Ordinal);
    }

    [Fact]
    public void PinState_IsNotPersisted_BetweenRestarts()
    {
        // S3: скрепка — только runtime-поле, в AppSettings её нет.
        var persisted = typeof(AppSettings).GetProperties()
            .Where(p => p.Name.Contains("SessionPin", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();
        Assert.Empty(persisted);

        // Оба ViewModel объявляют поле и не пишут его ни в настройки, ни в JSON.
        var wpfVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("private bool _sessionPinned;", wpfVm, StringComparison.Ordinal);
        var avaloniaVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs"));
        Assert.Contains("private bool _sessionPinned;", avaloniaVm, StringComparison.Ordinal);

        foreach (var source in new[] { wpfVm, avaloniaVm })
        {
            Assert.DoesNotContain("s.SessionPinned", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_settings.SessionPinned", source, StringComparison.Ordinal);
        }

        // «Новый» запуск приложения: свойства для скрепки в настройках нет вообще,
        // поэтому состояние по умолчанию — отжатая скрепка.
        Assert.Null(typeof(AppSettings).GetProperty("SessionPinned"));
    }

    // ======================= Статик лаунчера и настройки базы =======================

    [Fact]
    public void ResetSession_AppliesAutoToLauncherArchitecture()
    {
        // R3: после сброса в статике лаунчера не должно остаться прежней разрядности.
        var wpfLaunch = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Launch.cs"));
        var wpfReset = SliceMethod(wpfLaunch, "    private void ResetSessionOverridesAfterLaunch()", "private static OneCClientType? ResolveClientFromInfobase");
        Assert.Contains("OneCLauncher.SessionArchitecture = SessionArchitectureMode.Auto;", wpfReset, StringComparison.Ordinal);

        var avaloniaVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs"));
        var avaloniaReset = SliceMethod(avaloniaVm, "    private void ResetSessionOverridesAfterLaunch()", "// ======================= Загрузка данных");
        Assert.Contains("OneCLauncher.SessionArchitecture = Models.SessionArchitectureMode.Auto;", avaloniaReset, StringComparison.Ordinal);
        Assert.Contains("NotifySessionValues();", avaloniaReset, StringComparison.Ordinal);

        // Поведение статика: X64 сессии перебивает явную «32» базы, после Auto —
        // снова побеждает настройка базы (issue #146).
        var previousDefault = OneCLauncher.DefaultArchitectureMode;
        var previousSession = OneCLauncher.SessionArchitecture;
        try
        {
            OneCLauncher.DefaultArchitectureMode = "X64";
            OneCLauncher.SessionArchitecture = SessionArchitectureMode.X64;
            Assert.Equal(OneCArchitecture.x64, OneCLauncher.ResolveArchitecture("32", null));

            OneCLauncher.SessionArchitecture = SessionArchitectureMode.Auto;
            Assert.Equal(OneCArchitecture.x86, OneCLauncher.ResolveArchitecture("32", null));
        }
        finally
        {
            OneCLauncher.DefaultArchitectureMode = previousDefault;
            OneCLauncher.SessionArchitecture = previousSession;
        }
    }

    [Fact]
    public void ResetSession_DoesNotChangeInfobaseLaunchMode()
    {
        // S5: сброс касается только runtime-переопределения — настройки базы не меняются.
        // («OneCLaunchMode.Enterprise» — это режим запуска платформы, а не Infobase.LaunchMode.)
        var wpfLaunch = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Launch.cs"));
        var wpfReset = SliceMethod(wpfLaunch, "    private void ResetSessionOverridesAfterLaunch()", "private static OneCClientType? ResolveClientFromInfobase");
        Assert.DoesNotContain("ib.LaunchMode", wpfReset, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchMode =", wpfReset, StringComparison.Ordinal);

        var avaloniaVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs"));
        var avaloniaReset = SliceMethod(avaloniaVm, "    private void ResetSessionOverridesAfterLaunch()", "// ======================= Загрузка данных");
        Assert.DoesNotContain("infobase.LaunchMode", avaloniaReset, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchMode =", avaloniaReset, StringComparison.Ordinal);

        var infobase = new Infobase { Name = "Тестовая", LaunchMode = "Тонкий клиент" };
        Assert.True(SessionLaunchPolicy.ShouldResetAfterLaunch(
            pinned: false, SessionClientMode.Thick, SessionArchitectureMode.Auto,
            OneCLaunchMode.Enterprise, launchSucceeded: true));
        Assert.Equal("Тонкий клиент", infobase.LaunchMode);
    }

    [Fact]
    public void WpfAndAvalonia_UseSharedSessionLaunchPolicy()
    {
        var wpfLaunch = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Launch.cs"));
        Assert.Contains("SessionLaunchPolicy.ShouldResetAfterLaunch(", wpfLaunch, StringComparison.Ordinal);

        var avaloniaVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs"));
        Assert.Contains("SessionLaunchPolicy.ShouldResetAfterLaunch(", avaloniaVm, StringComparison.Ordinal);

        // Avalonia: единственная точка сброса — подписка LaunchViewModel.
        Assert.Contains("EnterpriseOverridesConsumed = ResetSessionOverridesAfterLaunch", avaloniaVm, StringComparison.Ordinal);
        // Сброс не подменяет общую точку успеха OnLaunched (там бывает и «Конфигуратор», S6).
        var avaloniaOnLaunched = SliceMethod(
            ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Launch.cs")),
            "private void OnLaunched()", "/// <summary>");
        Assert.DoesNotContain("ResetSessionOverridesAfterLaunch", avaloniaOnLaunched, StringComparison.Ordinal);
    }

    [Fact]
    public void AppSettings_KeepsLegacySessionFields()
    {
        // S4: поля не удаляются — старые settings.json читаются без ошибок.
        Assert.NotNull(typeof(AppSettings).GetProperty("SessionClientMode"));
        Assert.NotNull(typeof(AppSettings).GetProperty("SessionArchitecture"));

        var settings = ReadProjectFile(Path.Combine("Workspace1C", "Models", "AppSettings.cs"));
        Assert.Contains("УСТАРЕЛО (issue #156)", settings, StringComparison.Ordinal);
    }

    // ======================= A2: скрепка, UI и локализация =======================

    [Fact]
    public void Pin_DefaultsToFalse_AndIsNotPersisted()
    {
        // S3: скрепка — только runtime-поле, объявленное без инициализатора
        // (значение по умолчанию false). В AppSettings её нет, и ни один ViewModel
        // не пишет состояние в настройки.
        var wpfVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.cs"));
        var avaloniaVm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs"));
        foreach (var source in new[] { wpfVm, avaloniaVm })
        {
            Assert.Contains("private bool _sessionPinned;", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_sessionPinned = true", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_sessionPinned = false;", SliceMethod(source,
                "private bool _sessionPinned;", "/// <summary>"), StringComparison.Ordinal);
        }

        var settingsSource = ReadProjectFile(Path.Combine("Workspace1C", "Models", "AppSettings.cs"));
        Assert.DoesNotContain("SessionPinned", settingsSource, StringComparison.Ordinal);
        Assert.Null(typeof(AppSettings).GetProperty("SessionPinned"));
    }

    [Fact]
    public void Pinned_ValuesSurviveLaunch()
    {
        // S2: при включённой скрепке успешный запуск «Предприятия» значения не сбрасывает.
        Assert.False(SessionLaunchPolicy.ShouldResetAfterLaunch(
            pinned: true,
            SessionClientMode.Thick,
            SessionArchitectureMode.X64,
            OneCLaunchMode.Enterprise,
            launchSucceeded: true));

        // Обе точки сброса читают runtime-флаг скрепки, а не константу.
        var wpfReset = SliceMethod(
            ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Launch.cs")),
            "    private void ResetSessionOverridesAfterLaunch()", "private static OneCClientType? ResolveClientFromInfobase");
        Assert.Contains("_sessionPinned", wpfReset, StringComparison.Ordinal);

        var avaloniaReset = SliceMethod(
            ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs")),
            "    private void ResetSessionOverridesAfterLaunch()", "// ======================= Загрузка данных");
        Assert.Contains("_sessionPinned", avaloniaReset, StringComparison.Ordinal);
    }

    [Fact]
    public void Unpinned_ValuesResetAfterEnterpriseLaunch()
    {
        // S1: при отжатой скрепке успешный запуск «Предприятия» возвращает «Авто».
        Assert.True(SessionLaunchPolicy.ShouldResetAfterLaunch(
            pinned: false,
            SessionClientMode.Thick,
            SessionArchitectureMode.Auto,
            OneCLaunchMode.Enterprise,
            launchSucceeded: true));

        var wpfReset = SliceMethod(
            ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Launch.cs")),
            "    private void ResetSessionOverridesAfterLaunch()", "private static OneCClientType? ResolveClientFromInfobase");
        Assert.Contains("SessionClientMode = SessionClientMode.Auto;", wpfReset, StringComparison.Ordinal);
        Assert.Contains("SessionArchitecture = SessionArchitectureMode.Auto;", wpfReset, StringComparison.Ordinal);

        var avaloniaReset = SliceMethod(
            ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs")),
            "    private void ResetSessionOverridesAfterLaunch()", "// ======================= Загрузка данных");
        Assert.Contains("SessionClient = \"Авто\";", avaloniaReset, StringComparison.Ordinal);
        Assert.Contains("SessionArch = \"Авто\";", avaloniaReset, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_HasSessionPinButton()
    {
        // A2: «скрепка» в заголовке карточки «Текущая сессия» (MainWindow.xaml).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        Assert.Contains("IconPin", xaml, StringComparison.Ordinal);
        Assert.Contains("IsSessionPinned", xaml, StringComparison.Ordinal);
        Assert.Contains("ToggleSessionPinCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("SessionPinTooltip", xaml, StringComparison.Ordinal);
        // Используется существующий стиль значковых тумблеров (акцент при включении).
        Assert.Contains("ToolbarToggleButton", xaml, StringComparison.Ordinal);

        var display = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Display.cs"));
        Assert.Contains("public string SessionPinTooltip", display, StringComparison.Ordinal);
        Assert.Contains("ToggleSessionPinCommand", display, StringComparison.Ordinal);

        var vm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("ToggleSessionPinCommand = new RelayCommand", vm, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_HasSessionPinButton()
    {
        // A2: та же «скрепка» в заголовке карточки Avalonia (паритет с WPF).
        var tree = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("SessionPinToggle()", tree, StringComparison.Ordinal);
        Assert.Contains("IconPin", tree, StringComparison.Ordinal);
        Assert.Contains("IsSessionPinned", tree, StringComparison.Ordinal);
        Assert.Contains("ToggleSessionPinCommand", tree, StringComparison.Ordinal);
        Assert.Contains("SessionPinTooltip", tree, StringComparison.Ordinal);

        var commands = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.Commands.cs"));
        Assert.Contains("ToggleSessionPinCommand", commands, StringComparison.Ordinal);
        Assert.Contains("ToggleSessionPinCommand = new RelayCommand", commands, StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_HasSessionPinKeys()
    {
        // A2: ключи подсказки скрепки есть в обоих языках, непустые и различаются.
        foreach (var language in new[] { "ru", "en" })
        {
            var json = ReadProjectFile(Path.Combine("Workspace1C", "Localization", "Languages", $"{language}.json"));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            // Строки локализации лежат во вложенном объекте "strings".
            var strings = doc.RootElement.GetProperty("strings");
            var on = strings.GetProperty("Main.SessionPinOn").GetString();
            var off = strings.GetProperty("Main.SessionPinOff").GetString();
            Assert.False(string.IsNullOrWhiteSpace(on), $"{language}: Main.SessionPinOn пуст");
            Assert.False(string.IsNullOrWhiteSpace(off), $"{language}: Main.SessionPinOff пуст");
            Assert.NotEqual(on, off);
        }
    }

    // ======================= Вспомогательное =======================

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

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Не найден маркер начала: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Не найден маркер конца: {endMarker}");
        return source.Substring(start, end - start);
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
