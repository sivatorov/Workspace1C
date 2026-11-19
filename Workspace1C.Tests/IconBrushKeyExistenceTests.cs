using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Аудит UI P0-1: ключи кистей, которые передаются в <c>IconHelper.MakeIcon</c>
/// и <c>IconHelper.IconAndText</c>, обязаны существовать в ресурсах Avalonia.
/// Опечатка вида <c>TextOnAccentColorBrush</c> не бросает исключение: значок
/// молча остаётся невидимым, поэтому проверка идёт по исходникам.
/// </summary>
public sealed class IconBrushKeyExistenceTests
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

    private static string ProjectDir => Path.Combine(FindRepoRoot(), "Workspace1C");

    private static bool IsGeneratedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>Строковые литералы в аргументах вызова (обычные и verbatim).</summary>
    private static IEnumerable<string> StringLiterals(string text)
    {
        foreach (Match m in Regex.Matches(text, "@\"(?:[^\"]|\"\")*\"|\"(?:\\\\.|[^\"\\\\])*\"",
                     RegexOptions.Singleline))
        {
            var value = m.Value;
            if (value.StartsWith("@\"", StringComparison.Ordinal))
                yield return value[2..^1].Replace("\"\"", "\"", StringComparison.Ordinal);
            else
                yield return Regex.Unescape(value[1..^1]);
        }
    }

    /// <summary>Тела вызовов <c>IconHelper.<method>(...)</c> с учётом вложенных скобок.</summary>
    private static IEnumerable<string> CallArguments(string source, string method)
    {
        var marker = "IconHelper." + method + "(";
        var index = 0;
        while ((index = source.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            var open = index + marker.Length - 1;
            var close = FindMatchingParenthesis(source, open);
            if (close < 0)
                yield break;
            yield return source.Substring(open + 1, close - open - 1);
            index = close + 1;
        }
    }

    private static int FindMatchingParenthesis(string text, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                // Пропускаем строковый литерал целиком (с экранированием и verbatim).
                var verbatim = i > 0 && text[i - 1] == '@';
                i++;
                while (i < text.Length)
                {
                    if (verbatim && text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }

                    if (!verbatim && text[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (text[i] == '"')
                        break;
                    i++;
                }
                continue;
            }

            if (c == '(')
                depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static bool IsBrushKey(string value) =>
        Regex.IsMatch(value, "^[A-Za-z][A-Za-z0-9_]*Brush$", RegexOptions.CultureInvariant);

    private static string? BrushKeyFromLiteral(string literal)
    {
        if (literal.StartsWith("@", StringComparison.Ordinal))
        {
            var key = literal[1..];
            return IsBrushKey(key) ? key : null;
        }

        return IsBrushKey(literal) ? literal : null;
    }

    /// <summary>Все ключи кистей, доступные в Avalonia-словарях и генерируемые ThemeManager.</summary>
    private static HashSet<string> AvailableBrushKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        // 1. Явные x:Key в Avalonia-словарях (LightTheme.axaml/DarkTheme.axaml,
        //    Controls.axaml, App.axaml, Icons*.axaml).
        foreach (var file in Directory.EnumerateFiles(ProjectDir, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsGeneratedPath(file))
                continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "x:Key=\"([^\"]+)\""))
                keys.Add(m.Groups[1].Value);
        }

        // 2. Кисти, которые ApplyColors создаёт из палитры ColorScheme:
        //    для «Color»-ключа добавляется «ColorBrush», ключи «...Brush» — как есть.
        foreach (var scheme in new[] { ColorScheme.CreateLight(), ColorScheme.CreateDark() })
        {
            foreach (var key in scheme.Palette(scheme.IsDark).Keys)
            {
                if (key.EndsWith("Brush", StringComparison.Ordinal))
                    keys.Add(key);
                else
                    keys.Add(key + "Brush");
            }
        }

        // 3. Кисти, вычисляемые ThemeManager (TextOnAccentBrush, AccentTextBrush,
        //    NavSelected* и т.п.). Берём все строковые литералы-ключи из кода темы.
        var themeManager = Path.Combine(ProjectDir, "Themes", "ThemeManager.Avalonia.cs");
        if (File.Exists(themeManager))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(themeManager), "\"([A-Za-z][A-Za-z0-9_]*Brush)\""))
                keys.Add(m.Groups[1].Value);
        }

        return keys;
    }

    [Fact]
    public void IconHelper_BrushKeys_ExistInThemeResources()
    {
        var sources = Directory.EnumerateFiles(ProjectDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsGeneratedPath(file))
            .Select(File.ReadAllText)
            .ToList();

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            foreach (var method in new[] { "MakeIcon", "IconAndText" })
            {
                foreach (var args in CallArguments(source, method))
                {
                    foreach (var literal in StringLiterals(args))
                    {
                        if (BrushKeyFromLiteral(literal) is { } key)
                            used.Add(key);
                    }
                }
            }
        }

        Assert.NotEmpty(used);

        var available = AvailableBrushKeys();
        var missing = used.Where(key => !available.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            "IconHelper.MakeIcon/IconAndText ссылаются на отсутствующие кисти темы: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void GroupEditWindow_Avalonia_CloseIcon_UsesExistingTextOnAccentBrush()
    {
        var source = File.ReadAllText(Path.Combine(ProjectDir, "Views", "GroupEditWindow.Avalonia.cs"));

        Assert.Contains("IconHelper.MakeIcon(\"IconClose\", 16, \"TextOnAccentBrush\")", source,
            StringComparison.Ordinal);
        // TextOnAccentColorBrush — несуществующий ключ (был причиной невидимой иконки).
        Assert.DoesNotContain("TextOnAccentColorBrush", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Sources_DoNotUseNonExistentTextOnAccentColorBrush()
    {
        var offenders = Directory.EnumerateFiles(ProjectDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsGeneratedPath(file))
            .Where(file => File.ReadAllText(file).Contains("TextOnAccentColorBrush", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(ProjectDir, file))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Ключ TextOnAccentColorBrush не существует в ресурсах; используйте TextOnAccentBrush: " +
            string.Join(", ", offenders));
    }
}
