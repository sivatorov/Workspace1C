using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #98 «Поменять местами кнопки» (репорт ArtyoZ от 2026-11-02): в одном из окон
/// порядок кнопок отличался от остальных. Конвенция проекта (комментарии к issue #56 и
/// #374 в ContextMenuEditorWindow/UtilitiesMenuEditorWindow): главная/подтверждающая
/// кнопка — СЛЕВА, «Отмена» — СПРАВА (последней). grep-аудит Views нашёл отклонения
/// (отмена слева, подтверждение справа): PlatformDistributionPickerWindow (WPF+Avalonia),
/// NameInputWindow, BaseSelectionWindow, LoginWindow и общий построитель
/// ModalWindowBase.BuildButtons — все приведены к конвенции, здесь файловые стражи
/// против возврата старого порядка (по образцу Issue26ButtonUniformityTests).
/// </summary>
public sealed class Issue98ButtonOrderTests
{
    private static string ReadProjectFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var fullPath = Path.Combine(dir!.FullName, relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    /// <summary>Индекс первого вхождения <paramref name="value"/> (или int.MaxValue, если нет).</summary>
    private static int Index(string text, string value)
    {
        var i = text.IndexOf(value, StringComparison.Ordinal);
        return i < 0 ? int.MaxValue : i;
    }

    // ===================== Окно выбора дистрибутива платформы =====================

    [Fact]
    public void PlatformDistributionPicker_Wpf_ChooseBeforeCancel()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "PlatformDistributionPickerWindow.xaml"));
        Assert.True(Index(xaml, "DistributionPicker.Choose") < Index(xaml, "IsCancel=\"True\""),
            "issue #98: «Выбрать» должна идти раньше «Отмены» (подтверждение слева).");
        // Enter подтверждает выбор (IsDefault на главной кнопке).
        var chooseRow = xaml.Substring(Math.Max(0, Index(xaml, "DistributionPicker.Choose") - 200), 500);
        Assert.Contains("IsDefault=\"True\"", chooseRow, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformDistributionPicker_Avalonia_ChooseBeforeCancel()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "PlatformDistributionPickerWindow.Avalonia.cs"));
        Assert.True(Index(code, "buttons.Children.Add(choose);") < Index(code, "buttons.Children.Add(cancel);"),
            "issue #98: в Avalonia-версии «Выбрать» добавляется раньше «Отмены».");
    }

    // ===================== Общий построитель кнопок Avalonia =====================

    [Fact]
    public void ModalWindowBase_BuildButtons_ConfirmBeforeCancel()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "ModalWindowBase.cs"));
        var buildButtons = code.Substring(Index(code, "protected StackPanel BuildButtons("));
        Assert.True(Index(buildButtons, "BuildConfirmButton(") < Index(buildButtons, "BuildCancelButton("),
            "issue #98: BuildButtons обязан строить подтверждение раньше «Отмены».");
    }

    // ===================== Диалоги ввода (WPF-разметка) =====================

    [Fact]
    public void NameInputWindow_Wpf_OkBeforeCancel()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "NameInputWindow.xaml"));
        Assert.True(Index(xaml, "x:Name=\"OkButton\"") < Index(xaml, "IsCancel=\"True\""),
            "issue #98: «ОК» раньше «Отмены».");
    }

    [Fact]
    public void BaseSelectionWindow_Wpf_OkBeforeCancel()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "BaseSelectionWindow.xaml"));
        Assert.True(Index(xaml, "x:Name=\"OkButton\"") < Index(xaml, "IsCancel=\"True\""),
            "issue #98: «ОК» раньше «Отмены».");
    }

    [Fact]
    public void LoginWindow_Wpf_LoginBeforeCancel()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LoginWindow.xaml"));
        Assert.True(Index(xaml, "x:Name=\"LoginButton\"") < Index(xaml, "IsCancel=\"True\""),
            "issue #98: «Войти» раньше «Отмены».");
    }

    [Fact]
    public void LoginWindow_Avalonia_LoginBeforeCancel()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "Views", "LoginWindow.Avalonia.cs"));
        var method = code.Substring(Index(code, "private StackPanel BuildLoginButtons()"));
        var end = method.IndexOf("private void TryLogin()", StringComparison.Ordinal);
        if (end > 0)
            method = method[..end];
        Assert.True(Index(method, "panel.Children.Add(login);") < Index(method, "panel.Children.Add(cancel);"),
            "issue #98: в Avalonia-версии «Войти» добавляется раньше «Отмены».");
    }

    /// <summary>Аудит конвенции: в WPF-разметке нет окон, где IsCancel-кнопка стоит
    /// раньше главной IsDefault-кнопки в одной панели кнопок.</summary>
    [Fact]
    public void WpfViews_NoCancelBeforeDefaultInButtonPanels()
    {
        // Поиск корня репозитория от базового каталога тестов.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var views = Directory.GetFiles(Path.Combine(dir!.FullName, "Workspace1C", "Views"), "*.xaml");

        var offenders = new System.Collections.Generic.List<string>();
        foreach (var file in views)
        {
            var text = Regex.Replace(File.ReadAllText(file), @"<!--.*?-->",
                string.Empty, RegexOptions.Singleline);
            foreach (Match m in Regex.Matches(text, @"IsCancel=""True"""))
            {
                // «Отмена» раньше главной кнопки — нарушение только если IsDefault
                // встречается ПОСЛЕ неё в той же панели кнопок (без закрытия
                // </StackPanel> или начала новой панели между ними).
                var between = text.Substring(m.Index,
                    Math.Min(800, text.Length - m.Index));
                var defaultIndex = between.IndexOf("IsDefault=\"True\"", StringComparison.Ordinal);
                if (defaultIndex < 0)
                    continue;
                var segment = between[..defaultIndex];
                if (segment.Contains("</StackPanel>", StringComparison.Ordinal) ||
                    segment.Contains("<StackPanel", StringComparison.Ordinal))
                    continue;
                offenders.Add(Path.GetFileName(file));
            }
        }
        Assert.True(offenders.Count == 0,
            "issue #98: окна с «Отменой» раньше главной кнопки: " + string.Join(", ", offenders));
    }
}
