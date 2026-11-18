using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #140 «Инспектор процессов» (1.0.1.84):
/// <list type="bullet">
/// <item>в WPF выделяется каждая вторая строка, а выделение перекрывает зебру и hover;</item>
/// <item>в Avalonia выделение видно на самом <c>ListBoxItem</c> (мультивыделение,
/// пересоздание контейнеров после автообновления);</item>
/// <item>инспектор использует полный матчер: <c>/IBName</c>, переменные окружения,
/// UNC ↔ диск при уникальном листе, веб-базы, кавычки, сервер с портом.</item>
/// </list>
/// Source-гварды проверяют разметку обеих оболочек, остальные тесты — чистую логику
/// <see cref="RunningInfobaseMatcher"/> и <see cref="ProcessInspectorViewModel"/>.
/// </summary>
public sealed class Issue140ProcessInspectorTests
{
    // ===================== WPF: зебра и выделение =====================

    [Fact]
    public void Wpf_InspectorGrid_HasNoLocalAlternatingRowBackground()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.xaml");

        // Локального атрибута AlternatingRowBackground у DataGrid быть не должно.
        Assert.DoesNotContain("AlternatingRowBackground=", xaml, StringComparison.Ordinal);
        Assert.Contains("<DataGrid.RowStyle>", xaml, StringComparison.Ordinal);
        Assert.Contains("AlternationCount=\"2\"", xaml, StringComparison.Ordinal);

        // Глобальный стиль DataGrid задаёт AlternatingRowBackground сеттером — его
        // локальное значение перекрывало бы IsSelected. Для инспектора он обнулён.
        Assert.Contains("ProcessesGridStyle", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"AlternatingRowBackground\" Value=\"{x:Null}\"/>",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_RowStyle_SelectedTrigger_ComesAfterAlternation()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.xaml");
        var start = xaml.IndexOf("<DataGrid.RowStyle>", StringComparison.Ordinal);
        var end = xaml.IndexOf("</DataGrid.RowStyle>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "В ProcessInspectorWindow.xaml нет DataGrid.RowStyle.");

        var rowStyle = xaml[start..end];
        var alternation = rowStyle.IndexOf("ItemsControl.AlternationIndex", StringComparison.Ordinal);
        var selected = rowStyle.IndexOf("Property=\"IsSelected\"", StringComparison.Ordinal);

        Assert.True(alternation >= 0, "В RowStyle нет триггера зебры.");
        Assert.True(selected >= 0, "В RowStyle нет триггера выделения.");
        // Выделение должно стоять последним и перекрывать зебру/hover.
        Assert.True(selected > alternation, "Триггер IsSelected должен идти после AlternationIndex.");
    }

    // ===================== Avalonia: выделение строк =====================

    [Fact]
    public void Avalonia_ModernListBoxItem_SelectedHasItemBackground()
    {
        var axaml = ReadProjectFile("Workspace1C", "Themes", "Controls.axaml");

        // Ищем именно ControlTheme ModernListBoxItem: точный селектор ^:selected
        // (без /template/) должен задавать фон на самом ListBoxItem.
        var themeStart = axaml.IndexOf("x:Key=\"ModernListBoxItem\"", StringComparison.Ordinal);
        Assert.True(themeStart >= 0, "В Controls.axaml нет ControlTheme ModernListBoxItem.");
        var themeEnd = axaml.IndexOf("</ControlTheme>", themeStart, StringComparison.Ordinal);
        Assert.True(themeEnd > themeStart, "ControlTheme ModernListBoxItem не закрыт.");
        var theme = axaml[themeStart..themeEnd];

        var marker = "Selector=\"^:selected\"";
        var index = FindSelectorFollowedBy(theme, marker, '>');
        Assert.True(index >= 0, "В ModernListBoxItem нет стиля Selector=\"^:selected\" на самом ListBoxItem.");

        var styleEnd = theme.IndexOf("</Style>", index, StringComparison.Ordinal);
        Assert.True(styleEnd > index, "Стиль ^:selected не закрыт.");
        var style = theme[index..styleEnd];

        Assert.Contains("Background", style, StringComparison.Ordinal);
        Assert.Contains("ItemSelectedBrush", style, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_Inspector_SetsItemContainerTheme()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.Avalonia.cs");

        Assert.Contains("ItemContainerTheme", code, StringComparison.Ordinal);
        Assert.Contains("ModernListBoxItem", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ModernListBoxItem_ZebraStyleComesBeforeSelected()
    {
        var axaml = ReadProjectFile("Workspace1C", "Themes", "Controls.axaml");
        var themeStart = axaml.IndexOf("x:Key=\"ModernListBoxItem\"", StringComparison.Ordinal);
        Assert.True(themeStart >= 0, "В Controls.axaml нет ControlTheme ModernListBoxItem.");
        var themeEnd = axaml.IndexOf("</ControlTheme>", themeStart, StringComparison.Ordinal);
        var theme = axaml[themeStart..themeEnd];

        // «Зебра» чётных строк задаётся классом zebra на самом контейнере.
        var zebra = FindSelectorFollowedBy(theme, "Selector=\"^.zebra\"", '>');
        Assert.True(zebra >= 0, "В ModernListBoxItem нет стиля Selector=\"^.zebra\".");
        var zebraStyle = theme[zebra..theme.IndexOf("</Style>", zebra, StringComparison.Ordinal)];
        Assert.Contains("ZebraRowBrush", zebraStyle, StringComparison.Ordinal);

        // Выделение должно идти после зебры и перекрывать её.
        var selected = FindSelectorFollowedBy(theme, "Selector=\"^:selected\"", '>');
        Assert.True(selected >= 0, "В ModernListBoxItem нет стиля Selector=\"^:selected\".");
        Assert.True(selected > zebra, "Стиль :selected должен идти после стиля зебры.");
    }

    [Fact]
    public void Avalonia_Inspector_MarksZebraContainers()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "ProcessInspectorWindow.Avalonia.cs");

        // Зебра вешается на контейнер при его подготовке и снимается при
        // переиспользовании — иначе полосы «съезжают» после автообновления.
        Assert.Contains("ContainerPrepared", code, StringComparison.Ordinal);
        Assert.Contains("Classes.Add(\"zebra\")", code, StringComparison.Ordinal);
        Assert.Contains("Classes.Remove(\"zebra\")", code, StringComparison.Ordinal);
    }

    // ===================== Инспектор использует полный матчер =====================

    [Fact]
    public void Inspector_BuildRows_UsesFullMatcher_ForIbName()
    {
        var infobase = new Infobase
        {
            Id = "f1",
            Name = "Demo 27",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\Bases\f1" }
        };
        var service = new FakeRunningService(
            Proc(1000, @"ENTERPRISE /IBName ""Demo 27"""));
        using var vm = CreateViewModel(service, new[] { infobase });

        WaitFor(() => vm.Processes.Count == 1);

        var row = vm.Processes[0];
        Assert.True(row.IsKnown, "Процесс с /IBName должен сопоставляться с базой в инспекторе.");
        Assert.Equal("Demo 27", row.BaseName);
    }

    [Fact]
    public void Matcher_IbName_UniqueMatchesOwner()
    {
        var infobase = new Infobase
        {
            Id = "f1",
            Name = "Demo 27",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\Bases\f1" }
        };

        Assert.Same(
            infobase,
            RunningInfobaseMatcher.FindOwner(new[] { infobase }, @"""1cv8c.exe"" ENTERPRISE /IBName ""Demo 27"""));
    }

    [Fact]
    public void Matcher_IbName_AmbiguousDoesNotMatch()
    {
        // Одинаковое имя у двух баз — защита от ложного сопоставления.
        var first = new Infobase
        {
            Id = "f1",
            Name = "Demo",
            Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = @"C:\Bases\f1" }
        };
        var second = new Infobase
        {
            Id = "s1",
            Name = "Demo",
            Connection = new ConnectionSettings { Type = ConnectionType.ClientServer, Server = "srv", DatabaseName = "demo" }
        };

        Assert.Null(RunningInfobaseMatcher.FindOwner(
            new[] { first, second }, @"""1cv8c.exe"" ENTERPRISE /IBName ""Demo"""));
    }

    // ===================== Матчер: пути и файловые базы =====================

    [Fact]
    public void Matcher_FileBase_ExpandsEnvironmentVariables()
    {
        var variable = "ISSUE140_TEMP";
        var temp = Path.Combine(Path.GetTempPath(), "issue140_ib");
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, temp);
        try
        {
            var infobase = FileBase($@"%{variable}%\ib");
            var commandLine = $@"""1cv8c.exe"" ENTERPRISE /F ""{Path.Combine(temp, "ib")}""";

            Assert.Same(infobase, RunningInfobaseMatcher.FindOwner(new[] { infobase }, commandLine));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void Matcher_FileBase_MatchesUniqueLeafName_WhenPathsDiffer()
    {
        // UNC-путь в карточке ↔ путь через смонтированный диск в командной строке.
        var infobase = FileBase(@"\\srv\share\base");
        var commandLine = @"""1cv8c.exe"" ENTERPRISE /F ""Z:\base""";

        Assert.Same(infobase, RunningInfobaseMatcher.FindOwner(new[] { infobase }, commandLine));
    }

    [Fact]
    public void Matcher_FileBase_DoesNotMatchAmbiguousLeafName()
    {
        var first = FileBase(@"C:\one\base");
        var second = FileBase(@"D:\two\base");
        var commandLine = @"""1cv8c.exe"" ENTERPRISE /F ""Z:\base""";

        Assert.Null(RunningInfobaseMatcher.FindOwner(new[] { first, second }, commandLine));
    }

    [Fact]
    public void Matcher_FileBase_WholeArgumentQuoted()
    {
        var infobase = FileBase(@"C:\base");
        var commandLine = @"""1cv8c.exe"" ENTERPRISE /F""C:\base""";

        Assert.Same(infobase, RunningInfobaseMatcher.FindOwner(new[] { infobase }, commandLine));
    }

    [Fact]
    public void Matcher_FileBase_MatchesShortNameLeaf()
    {
        // Короткое имя 8.3 в карточке ↔ длинное имя каталога в командной строке:
        // пути различаются, но уникальный последний сегмент совпадает.
        var infobase = FileBase(@"C:\BASES~1\IB");
        var commandLine = @"""1cv8c.exe"" ENTERPRISE /F ""C:\Bases\IB""";

        Assert.Same(infobase, RunningInfobaseMatcher.FindOwner(new[] { infobase }, commandLine));
    }

    // ===================== Матчер: веб-базы и сервер с портом =====================

    [Fact]
    public void Matcher_WebBase_MatchesWsUrl()
    {
        var infobase = new Infobase
        {
            Id = "w1",
            Name = "Веб",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.WebServer,
                WebUrl = "https://srv/base"
            }
        };
        var commandLine = @"""1cv8c.exe"" ENTERPRISE /WS https://srv/base/";

        Assert.Same(infobase, RunningInfobaseMatcher.FindOwner(new[] { infobase }, commandLine));
    }

    [Fact]
    public void Matcher_WebBase_MatchesWsInIbConnectionString()
    {
        var infobase = new Infobase
        {
            Id = "w1",
            Name = "Веб",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.WebServer,
                WebUrl = "https://srv/base"
            }
        };
        var commandLine =
            @"""1cv8c.exe"" ENTERPRISE /IBConnectionString ""WS=""https://SRV/base/"";""";

        Assert.Same(infobase, RunningInfobaseMatcher.FindOwner(new[] { infobase }, commandLine));
    }

    [Fact]
    public void Matcher_WebBase_IgnoresScheme()
    {
        // Адрес публикации в карточке https, в командной строке — http:
        // схема в сравнении не участвует.
        var infobase = WebBase("https://srv/base");
        var commandLine = @"""1cv8c.exe"" ENTERPRISE /WS http://SRV/base/";

        Assert.Same(infobase, RunningInfobaseMatcher.FindOwner(new[] { infobase }, commandLine));
    }

    [Fact]
    public void Matcher_WebBase_UsesDatabaseNameWhenWebUrlEmpty()
    {
        var infobase = new Infobase
        {
            Id = "w1",
            Name = "Веб",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.WebServer,
                WebUrl = "",
                DatabaseName = "https://srv/base"
            }
        };
        var commandLine = @"""1cv8c.exe"" ENTERPRISE /WS https://srv/base/";

        Assert.Same(infobase, RunningInfobaseMatcher.FindOwner(new[] { infobase }, commandLine));
    }

    [Fact]
    public void Matcher_ServerBase_WithPortAndSlash()
    {
        var infobase = new Infobase
        {
            Id = "s1",
            Name = "Серверная",
            Connection = new ConnectionSettings
            {
                Type = ConnectionType.ClientServer,
                Server = "srv:1541",
                DatabaseName = "base"
            }
        };
        var commandLine = @"""1cv8c.exe"" ENTERPRISE /S""srv/base""";

        Assert.Same(infobase, RunningInfobaseMatcher.FindOwner(new[] { infobase }, commandLine));
    }

    // ===================== Отображение веб-базы в строке инспектора =====================

    [Fact]
    public void Inspector_WebBase_ShowsUrlAndName()
    {
        var infobase = WebBase("https://srv/base");
        var row = new ProcessRowViewModel(
            Proc(2000, @"""1cv8c.exe"" ENTERPRISE /WS https://srv/base/"),
            infobase);

        // Колонка «База» — имя базы (а не «Неизвестная база»), пояснение — URL публикации.
        Assert.True(row.IsKnown);
        Assert.Equal("Веб", row.BaseName);
        Assert.Equal("https://srv/base", row.Subtitle);

        // «Строка подключения» — фактический адрес из командной строки.
        Assert.Equal("/WS https://srv/base/", row.ConnectionText);
    }

    // ===================== Вспомогательные методы =====================

    private static Infobase FileBase(string path) => new()
    {
        Id = "f1",
        Name = "Файловая",
        Connection = new ConnectionSettings { Type = ConnectionType.File, FilePath = path }
    };

    private static Infobase WebBase(string webUrl) => new()
    {
        Id = "w1",
        Name = "Веб",
        Connection = new ConnectionSettings { Type = ConnectionType.WebServer, WebUrl = webUrl }
    };

    private static RunningOneCProcessDetails Proc(int pid, string commandLine) =>
        new(pid, "1cv8c.exe", commandLine, null, null, null);

    private static ProcessInspectorViewModel CreateViewModel(
        FakeRunningService service, IEnumerable<Infobase> infobases)
    {
        return new ProcessInspectorViewModel(
            service,
            new FakeKiller(),
            new RecordingDialogs(),
            infobases,
            _ => { },
            null);
    }

    private static void WaitFor(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount > deadline)
                throw new TimeoutException("Состояние ViewModel не достигнуто за отведённое время.");
            Thread.Sleep(10);
        }
    }

    /// <summary>Ищет точное вхождение селектора, за которым сразу идёт указанный символ.</summary>
    private static int FindSelectorFollowedBy(string text, string marker, char next)
    {
        var from = 0;
        while (true)
        {
            var index = text.IndexOf(marker, from, StringComparison.Ordinal);
            if (index < 0)
                return -1;

            var after = index + marker.Length;
            if (after < text.Length && text[after] == next)
                return index;

            from = after;
        }
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var relativePath = Path.Combine(parts);
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var fullPath = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);
            dir = dir.Parent;
        }

        Assert.Fail($"Файл проекта не найден: {relativePath}");
        return string.Empty;
    }

    private sealed class FakeRunningService : IRunningInfobasesService
    {
        private readonly IReadOnlyList<RunningOneCProcessDetails> _details;

        public FakeRunningService(params RunningOneCProcessDetails[] details) => _details = details;

        public IReadOnlyList<RunningOneCProcess> GetRunning() => Array.Empty<RunningOneCProcess>();

        public IReadOnlyList<RunningOneCProcessDetails> GetRunningDetails() => _details;
    }

    private sealed class FakeKiller : IOneCProcessKiller
    {
        public string? LastError { get; set; }

        public bool Kill(int pid, string? startTimeToken) => true;
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public void ShowInfo(string message, string title = "")
        {
        }

        public void ShowWarning(string message, string title = "")
        {
        }

        public void ShowError(string message, string title = "")
        {
        }

        public bool Confirm(string message, string title = "") => true;

        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null) => null;

        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null) => null;

        public string? OpenFolderDialog(string title = "", string? initialDirectory = null) => null;
    }
}
