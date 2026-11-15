using System;
using System.Collections.Generic;
using System.IO;
using Workspace1C;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #126 «Рамки для окон 2», доработка 1.0.1.64: рамка 1 DIP существует, но на
/// Windows 10/RDP невидима. Причина — DWM не рисует собственное ребро/тень, а цвет
/// рамки светлой темы (#E2E8F0) на фоне содержимого (#F1F5F9) даёт контраст ~1.13:1.
/// Решение — чистая политика <see cref="WindowFramePolicy"/>: на системах без ребра
/// DWM контраст поднимается по WCAG до 3:1, добавляется линия тени и привязка к
/// пикселям; на Windows 11 поведение не меняется.
/// <list type="bullet">
/// <item><see cref="WindowFramePolicy.Resolve"/> — все границы состояний (Win11,
/// Win10/RDP, выключенная композиция, DWM «не скруглять», maximized, удалённый сеанс);</item>
/// <item><see cref="WindowFramePolicy.ContrastRatio"/> и
/// <see cref="WindowFramePolicy.EnsureContrast"/> — светлая/тёмная тема, идемпотентность,
/// эталонная пара «чёрный на белом» ≈ 21:1;</item>
/// <item>source-гварды подключения политики в WPF (<c>WindowChromeHelper</c>) и
/// Avalonia (<c>ModalWindowBase</c>, <c>MainWindow.Avalonia.cs</c>), поля журнала
/// <c>frame-corner</c> и отсутствие <c>#if WINDOWS</c> в чистом файле политики.</item>
/// </list>
/// Существующие <c>Issue126WindowFrameTests</c> (наличие рамки) и
/// <c>Issue126FrameCornerRadiusTests</c> (радиус/скругление) не дублируются.
/// </summary>
public sealed class Issue126FrameVisibilityTests
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

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// Приводит исходник к виду, устойчивому к форматированию: любые последовательности
    /// пробельных символов (включая переводы строк и отступы) становятся одним пробелом.
    /// </summary>
    private static string NormalizeWhitespace(string code)
    {
        var result = new System.Text.StringBuilder(code.Length);
        var inWhitespace = false;
        foreach (var ch in code)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWhitespace = true;
                continue;
            }

            if (inWhitespace && result.Length > 0)
                result.Append(' ');
            inWhitespace = false;
            result.Append(ch);
        }

        return result.ToString();
    }

    private static List<int> AllIndexes(string text, string value)
    {
        var indexes = new List<int>();
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            indexes.Add(index);
            index = text.IndexOf(value, index + 1, StringComparison.Ordinal);
        }

        return indexes;
    }

    private static WindowFrameInputs Inputs(
        bool compositionEnabled = true,
        bool osRoundsWindows = true,
        bool dwmStateKnown = false,
        bool dwmRounded = false,
        bool maximized = false,
        bool remoteSession = false,
        double dpiScale = 1.0) =>
        new(compositionEnabled, osRoundsWindows, dwmStateKnown, dwmRounded,
            maximized, remoteSession, dpiScale);

    private static WindowFrameColor Hex(string value) =>
        WindowFrameColor.FromHex(value, WindowFrameColor.Black);

    // ===================== Чистая логика: WindowFramePolicy.Resolve =====================

    [Fact]
    public void Resolve_Win11Rounded_KeepsLocalBehavior()
    {
        // Локальная Windows 11: композиция включена, DWM сообщил DWMWCS_ROUNDED.
        // DWM рисует собственное ребро/тень — рамка остаётся 1 DIP, контраст не
        // усиливается, линия тени не добавляется: поведение не меняется.
        var visual = WindowFramePolicy.Resolve(Inputs(
            compositionEnabled: true, osRoundsWindows: true, dwmStateKnown: true, dwmRounded: true));

        Assert.Equal(WindowFramePolicy.FrameThicknessDip, visual.ThicknessDip);
        Assert.False(visual.StrengthenContrast);
        Assert.False(visual.AddOuterShadowLine);
        Assert.False(visual.SnapToPixels);
    }

    [Fact]
    public void Resolve_Win10RdpFromLog_EnablesFallback()
    {
        // Ровно входы из журнала frame-corner на Windows 10 в RDP:
        // compositionEnabled=true, osRounds=false, dwmStateKnown=false, dwmRounded=false.
        // Системного ребра/тени нет — контраст усиливается, линия тени добавляется.
        var visual = WindowFramePolicy.Resolve(Inputs(
            compositionEnabled: true, osRoundsWindows: false,
            dwmStateKnown: false, dwmRounded: false, remoteSession: true));

        Assert.Equal(WindowFramePolicy.FrameThicknessDip, visual.ThicknessDip);
        Assert.True(visual.StrengthenContrast);
        Assert.True(visual.AddOuterShadowLine);
        Assert.True(visual.SnapToPixels);
    }

    [Fact]
    public void Resolve_CompositionDisabled_EnablesFallback()
    {
        // Композиция DWM выключена — даже если остальные признаки «как на Windows 11»,
        // системного ребра/тени нет: включается тот же фолбэк усиления контраста.
        var visual = WindowFramePolicy.Resolve(Inputs(
            compositionEnabled: false, osRoundsWindows: true,
            dwmStateKnown: true, dwmRounded: true));

        Assert.True(visual.StrengthenContrast);
        Assert.True(visual.AddOuterShadowLine);
        Assert.True(visual.SnapToPixels);
    }

    [Fact]
    public void Resolve_Win11DwmDoNotRound_EnablesFallback()
    {
        // Windows 11, но DWM сообщил DWMWCS_DO_NOT_ROUND (скругление отключено
        // пользователем/системой): ребра DWM нет — рамка усиливается и получает тень.
        var visual = WindowFramePolicy.Resolve(Inputs(
            compositionEnabled: true, osRoundsWindows: true,
            dwmStateKnown: true, dwmRounded: false));

        Assert.True(visual.StrengthenContrast);
        Assert.True(visual.AddOuterShadowLine);
        Assert.True(visual.SnapToPixels);
    }

    [Fact]
    public void Resolve_Maximized_HidesShadowLineButKeepsStrengthenRule()
    {
        // Максимизированное окно прижато к краям рабочей области: дополнительная
        // линия тени не нужна (иначе рамка «утолщается»). Правило усиления контраста
        // при этом сохраняется: без ребра DWM линия всё равно должна быть видна.
        var fallback = WindowFramePolicy.Resolve(Inputs(
            compositionEnabled: false, osRoundsWindows: false, maximized: true));

        Assert.True(fallback.StrengthenContrast);
        Assert.False(fallback.AddOuterShadowLine);

        // На Windows 11 со скруглением ребро DWM есть — усиление не нужно ни в
        // обычном, ни в развёрнутом окне; линия тени скрыта в обоих случаях.
        var win11Maximized = WindowFramePolicy.Resolve(Inputs(
            compositionEnabled: true, osRoundsWindows: true,
            dwmStateKnown: true, dwmRounded: true, maximized: true));

        Assert.False(win11Maximized.StrengthenContrast);
        Assert.False(win11Maximized.AddOuterShadowLine);
    }

    [Fact]
    public void Resolve_RemoteSessionOnWin11_EnablesFallback()
    {
        // В удалённом сеансе (RDP) системному ребру/тени DWM не доверяем даже на
        // Windows 11 с DWMWCS_ROUNDED: клиент RDP может их не нарисовать, и рамка
        // снова «исчезает» — фолбэк включается независимо от состояния DWM.
        var visual = WindowFramePolicy.Resolve(Inputs(
            compositionEnabled: true, osRoundsWindows: true,
            dwmStateKnown: true, dwmRounded: true, remoteSession: true));

        Assert.True(visual.StrengthenContrast);
        Assert.True(visual.AddOuterShadowLine);
        Assert.True(visual.SnapToPixels);
    }

    // ===================== Контраст по WCAG 2.x =====================

    [Fact]
    public void EnsureContrast_LightTheme_ReachesMinRatioAndChangesColor()
    {
        // Светлая тема: рамка #E2E8F0 на фоне #F1F5F9 — исходный контраст ~1.13:1,
        // линия невидима. После подбора — не ниже 3:1 и цвет реально изменён.
        var frame = Hex("#E2E8F0");
        var background = Hex("#F1F5F9");

        Assert.True(WindowFramePolicy.ContrastRatio(frame, background) < WindowFramePolicy.MinContrastRatio);

        var result = WindowFramePolicy.EnsureContrast(frame, background);

        Assert.True(WindowFramePolicy.ContrastRatio(result, background) >= WindowFramePolicy.MinContrastRatio - 1e-9,
            $"Контраст {WindowFramePolicy.ContrastRatio(result, background):0.00} ниже 3:1.");
        Assert.NotEqual(frame, result);
    }

    [Fact]
    public void EnsureContrast_DarkTheme_ReachesMinRatio()
    {
        // Тёмная тема: рамка #334155 на фоне #0F172A — исходный контраст ~1.67:1.
        // Подбор должен поднять его до 3:1 (осветлением), не ломая тёмную палитру
        // «в белый» без необходимости.
        var frame = Hex("#334155");
        var background = Hex("#0F172A");

        Assert.True(WindowFramePolicy.ContrastRatio(frame, background) < WindowFramePolicy.MinContrastRatio);

        var result = WindowFramePolicy.EnsureContrast(frame, background);

        Assert.True(WindowFramePolicy.ContrastRatio(result, background) >= WindowFramePolicy.MinContrastRatio - 1e-9,
            $"Контраст {WindowFramePolicy.ContrastRatio(result, background):0.00} ниже 3:1.");
        Assert.NotEqual(frame, result);
    }

    [Fact]
    public void EnsureContrast_IsIdempotent_AndKeepsSufficientColor()
    {
        // Идемпотентность: повторный вызов на уже усиленном цвете возвращает тот же
        // цвет (иначе каждый Activated/смена темы «темнил» бы рамку всё сильнее).
        var background = Hex("#F1F5F9");
        var once = WindowFramePolicy.EnsureContrast(Hex("#E2E8F0"), background);
        var twice = WindowFramePolicy.EnsureContrast(once, background);

        Assert.Equal(once, twice);

        // Исходно контрастная пара не меняется вовсе (чёрная рамка на белом фоне).
        Assert.Equal(WindowFrameColor.Black,
            WindowFramePolicy.EnsureContrast(WindowFrameColor.Black, WindowFrameColor.White));
    }

    [Fact]
    public void ContrastRatio_BlackOnWhite_IsAbout21()
    {
        // Эталон WCAG 2.x: (1.0 + 0.05) / (0.0 + 0.05) = 21:1. Допуск — на
        // округление относительной яркости, порядок аргументов не важен.
        var ratio = WindowFramePolicy.ContrastRatio(WindowFrameColor.Black, WindowFrameColor.White);

        Assert.InRange(ratio, 20.9, 21.1);
        Assert.Equal(ratio,
            WindowFramePolicy.ContrastRatio(WindowFrameColor.White, WindowFrameColor.Black), 6);
    }

    // ===================== Source-гварды: подключение политики =====================

    [Fact]
    public void WindowChromeHelper_CallsApplyWindowFrameVisualFromBuildChromeAndAttach()
    {
        // Политика видимости применяется и к диалогам (BuildChrome), и к окнам с
        // собственным каркасом (AttachFrameCornerTracking, главное окно): иначе
        // часть окон осталась бы с прежней невидимой рамкой.
        var code = NormalizeWhitespace(
            ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs")));

        var buildChromeIndex = code.IndexOf(
            "private static UIElement BuildChrome(Window window, UIElement inner)", StringComparison.Ordinal);
        var attachIndex = code.IndexOf(
            "public static void AttachFrameCornerTracking(Window window, Border frame", StringComparison.Ordinal);

        Assert.True(buildChromeIndex >= 0, "Не найден BuildChrome в WindowChromeHelper.");
        Assert.True(attachIndex > buildChromeIndex, "Не найден AttachFrameCornerTracking после BuildChrome.");

        var calls = AllIndexes(code, "ApplyWindowFrameVisual(window, frame)");
        Assert.Contains(calls, index => index > buildChromeIndex && index < attachIndex);
        Assert.Contains(calls, index => index > attachIndex);
    }

    [Fact]
    public void WindowChromeHelper_KeepsThemeBrushBindingWhenContrastNotStrengthened()
    {
        // Гвард не конфликтует с Issue126FrameCornerRadiusTests: привязка рамки к
        // ресурсу темы BorderBrushColor сохранена. Но она должна оставаться именно
        // в ветке StrengthenContrast=false — на Windows 11 поведение не меняется,
        // а на Win10/RDP вместо неё ставится усиленный цвет.
        var code = NormalizeWhitespace(
            ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs")));

        const string themeBinding = "SetResourceReference(Border.BorderBrushProperty, \"BorderBrushColor\")";
        var strengthenIndex = code.IndexOf("if (visual.StrengthenContrast)", StringComparison.Ordinal);
        Assert.True(strengthenIndex >= 0, "Не найдено условие if (visual.StrengthenContrast).");

        // Первая привязка в файле — при создании рамки в BuildChrome; здесь важна
        // та, что стоит в ApplyWindowFrameVisual после проверки StrengthenContrast.
        var themeBindingIndex = code.IndexOf(themeBinding, strengthenIndex, StringComparison.Ordinal);
        Assert.True(themeBindingIndex > strengthenIndex,
            "Привязка BorderBrushColor должна оставаться после проверки StrengthenContrast.");

        var elseIndex = code.IndexOf("else", strengthenIndex, StringComparison.Ordinal);
        Assert.True(elseIndex > strengthenIndex && elseIndex < themeBindingIndex,
            "SetResourceReference(BorderBrushColor) должен находиться в ветке else (StrengthenContrast=false).");
    }

    [Fact]
    public void WindowChromeHelper_FrameCornerLogHasVisibilityFields()
    {
        // Диагностика видимости рамки: по журналу frame-corner видно, был ли сеанс
        // удалённым, какая толщина/контраст приняты и усиливалась ли рамка.
        var code = NormalizeWhitespace(
            ReadProjectFile(Path.Combine("Workspace1C", "Views", "WindowChromeHelper.cs")));

        Assert.Contains("frame-corner reason=", code, StringComparison.Ordinal);
        Assert.Contains("remote=", code, StringComparison.Ordinal);
        Assert.Contains("frameThickness=", code, StringComparison.Ordinal);
        Assert.Contains("frameContrast=", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowFramePolicy_HasNoWindowsConditionals()
    {
        // Политика — чистая логика без платформенных директив: используется и WPF,
        // и Avalonia, и покрывается юнит-тестами без Windows-сессии. Упоминание
        // директивы в XML-комментарии (документация) не считается условной
        // компиляцией — проверяются только реальные строки директив.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Services", "WindowFramePolicy.cs"));

        foreach (var line in code.Split('\n'))
        {
            var trimmed = line.Trim();
            Assert.False(trimmed.StartsWith("#if WINDOWS", StringComparison.Ordinal),
                $"Платформенная директива в чистой политике: {trimmed}");
        }
    }

    [Fact]
    public void ModalAndMainAvalonia_UseWindowFramePolicy()
    {
        // Обе Avalonia-поверхности (базовый класс диалогов и главное окно) считают
        // видимость рамки той же чистой политикой: Resolve для решения, EnsureContrast
        // для цвета — паритет с WPF, без расхождений в поведении.
        var modal = NormalizeWhitespace(
            ReadProjectFile(Path.Combine("Workspace1C", "Views", "ModalWindowBase.cs")));
        Assert.Contains("WindowFramePolicy.Resolve(new WindowFrameInputs(", modal, StringComparison.Ordinal);
        Assert.Contains("WindowFramePolicy.EnsureContrast(", modal, StringComparison.Ordinal);

        var main = NormalizeWhitespace(
            ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.cs")));
        Assert.Contains("WindowFramePolicy.Resolve(new WindowFrameInputs(", main, StringComparison.Ordinal);
        Assert.Contains("WindowFramePolicy.EnsureContrast(", main, StringComparison.Ordinal);
    }
}
