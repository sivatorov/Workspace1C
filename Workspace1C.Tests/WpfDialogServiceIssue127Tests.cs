using System.IO;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #127 «Ошибка при создании базы из dt»: при показе диалога возникли
/// Win32Exception 1816 «Недостаточно квот для обработки команды» и 1400 «Недопустимый
/// дескриптор окна» из WpfDialogService.Show → ShowError — приложение упало.
/// Требование: показ диалога в <see cref="WpfDialogService"/> (и его Avalonia-аналоге)
/// не должен уводить процесс вниз — сбой material-окна перехватывается, включается
/// резервный MessageBox, при повторном сбое остаётся только журнал.
/// <para>
/// WpfDialogService компилируется только под Windows (файл исключён из Linux-сборки
/// в Workspace1C.csproj), поэтому прямой runtime-тест на Linux CI невозможен —
/// проверки делаются по исходникам, по образцу EscCloseWindowsTests.
/// </para>
/// </summary>
public sealed class WpfDialogServiceIssue127Tests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "Workspace1C", "Services")) &&
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

    [Fact]
    public void ShowDialog_IsWrappedInTryCatch()
    {
        // И Confirm, и Show (внутри него ShowInfo/ShowWarning/ShowError) должны
        // перехватывать сбой показа material-окна: два catch (Exception) — в Confirm и Show.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "WpfDialogService.cs"));
        Assert.Contains("catch (Exception ex)", source);
        // Каждое место ShowDialog окружено try-блоком: try-блоков не меньше, чем ShowDialog.
        var tryCount = CountOccurrences(source, "try");
        var showCount = CountOccurrences(source, "ShowDialog()");
        Assert.True(tryCount >= showCount,
            $"Ожидается try-обёртка вокруг каждого ShowDialog (try={tryCount}, ShowDialog={showCount}).");
    }

    [Fact]
    public void Fallback_UsesStandardMessageBox()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "WpfDialogService.cs"));
        // Резервный показ — стандартный MessageBox без собственного hwnd-рендера WPF.
        Assert.Contains("System.Windows.MessageBox.Show", source);
    }

    [Fact]
    public void DoubleFailure_IsLoggedWithoutThrowing()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "WpfDialogService.cs"));
        // Повторный сбой резервного MessageBox логируется через IAppLogger, а не бросается.
        Assert.Contains("LogDialogFailure", source);
        Assert.Contains("AppServices.TryGetService<IAppLogger>()?.Error", source);
        // Явных throw в сервисе быть не должно: ошибка показа не уводит процесс вниз.
        Assert.DoesNotContain("throw", source);
    }

    [Fact]
    public void AvaloniaDialogService_HasShowFallback()
    {
        // Симметрия: Avalonia-вариант уже перехватывает сбой показа и делает запасной
        // немодальный показ (issue #168) — страж, что fallback-ветка не потерялась.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Services", "AvaloniaDialogService.cs"));
        Assert.Contains("Запасной путь показа", source);
        Assert.Contains("window.Show()", source);
    }

    private static int CountOccurrences(string text, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }
}
