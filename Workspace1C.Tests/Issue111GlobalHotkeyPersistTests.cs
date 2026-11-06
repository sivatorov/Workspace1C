using System;
using System.IO;
using System.Text.Json;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #111 «Сбрасываются глобальные клавиши»: пустое сохранённое значение
/// («клавиша снята») переживает сериализацию настроек и НЕ заменяется дефолтом
/// Ctrl+Alt+W при загрузке (WPF/Avalonia). Дефолт для свежих установок задаёт
/// свойство <see cref="AppSettings.GlobalShowWindowHotkey"/>.
/// </summary>
public sealed class Issue111GlobalHotkeyPersistTests
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [Fact]
    public void ClearedHotkey_SurvivesJsonRoundTrip()
    {
        var settings = new AppSettings { GlobalShowWindowHotkey = "" };

        var json = JsonSerializer.Serialize(settings, Indented);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, Indented);

        Assert.NotNull(restored);
        Assert.Equal(string.Empty, restored!.GlobalShowWindowHotkey);
    }

    [Fact]
    public void JsonWithoutProperty_FallsBackToDefaultGesture()
    {
        // Старый settings.json без поля: пользователь получает дефолт Ctrl+Alt+W.
        var json = "{}";
        var restored = JsonSerializer.Deserialize<AppSettings>(json, Indented);

        Assert.NotNull(restored);
        Assert.Equal("Ctrl+Alt+W", restored!.GlobalShowWindowHotkey);
    }

    [Fact]
    public void WpfLoader_DoesNotSubstituteDefaultForEmpty()
    {
        // Страж по исходникам (issue #111): fallback «пусто → DefaultGesture» в загрузке
        // настроек WPF убран — сохранённое «пусто» не затирается при каждом перезапуске.
        var code = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("Issue #111", code, StringComparison.Ordinal);
        Assert.DoesNotContain("if (string.IsNullOrEmpty(_globalShowWindowHotkey))", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaGetter_ReturnsStoredValueWithoutDefault()
    {
        var code = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.Avalonia.cs"));
        // getter: _settings.GlobalShowWindowHotkey ?? string.Empty — без DefaultGesture.
        var getterIndex = code.IndexOf("public string GlobalShowWindowHotkey", StringComparison.Ordinal);
        Assert.True(getterIndex >= 0, "Свойство GlobalShowWindowHotkey не найдено в Avalonia-VM");
        var getterBody = code.Substring(getterIndex, Math.Min(700, code.Length - getterIndex));
        Assert.Contains("_settings.GlobalShowWindowHotkey ?? string.Empty", getterBody, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultGesture", getterBody, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Workspace1C.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProjectFile(string relativePath)
    {
        var fullPath = Path.Combine(FindRepoRoot(), relativePath);
        Assert.True(File.Exists(fullPath), $"Файл проекта не найден: {relativePath}");
        return File.ReadAllText(fullPath);
    }
}
