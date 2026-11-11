using System;
using System.IO;
using System.ComponentModel;
using Workspace1C.Models;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #103 (v1.0.1.54, договорённость в обсуждении): значков остаётся два,
/// Конфигуратор — без изменений (ключ в кружке); Предприятие — треугольник
/// в кружке: СИНИЙ (InfoBrush) при запуске с отладкой /DEBUG, ОРАНЖЕВЫЙ
/// (WarningBrush) при обычном запуске; подсветка занятости сохранена (белый
/// значок на красном чипе при Hung/Critical, как у Конфигуратора). Отдельный
/// янтарный маркер отладки (v1.0.1.49/v1.0.1.50) удалён на обеих платформах —
/// цвет значка теперь сам кодирует отладку.
/// <para>Avalonia-код собирается только под #if LINUX — покрывается
/// source-гвардами (по образцу <see cref="Issue114StatusBarTooltipTests"/>);
/// логика цвета — юнит-тест <see cref="EnterpriseGlyphBrushConverter"/> в
/// Linux-конфигурации тестов (-p:BuildLinux=true).</para>
/// </summary>
public sealed class Issue103EnterpriseGlyphTests
{
    private static string ReadProjectFile(string relativePath)
    {
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

    // ===================== WPF (MainWindow.xaml) =====================

    [Fact]
    public void Wpf_EnterpriseGlyph_ColorEncodesDebugFlag()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var play = xaml.IndexOf("Kind=\"Play\" Width=\"16\" Height=\"16\"", StringComparison.Ordinal);
        Assert.True(play > 0, "значок-треугольник Play не найден в чипе Предприятия");

        var styleStart = xaml.IndexOf("<materialDesign:PackIcon.Style>", play, StringComparison.Ordinal);
        var styleEnd = xaml.IndexOf("</materialDesign:PackIcon.Style>", play, StringComparison.Ordinal);
        var glyphStyle = xaml[styleStart..styleEnd];

        // Обычный запуск — оранжевый (WarningBrush), запуск с /DEBUG — синий
        // (InfoBrush) по флагу IsEnterpriseDebugRunning.
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource WarningBrush}\"/>", glyphStyle, StringComparison.Ordinal);
        Assert.Contains("<DataTrigger Binding=\"{Binding IsEnterpriseDebugRunning}\" Value=\"True\">", glyphStyle, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource InfoBrush}\"/>", glyphStyle, StringComparison.Ordinal);

        // Подсветка занятости сохранена: зависший процесс — белый значок
        // (триггеры Hung/Critical идут ПОСЛЕ триггера отладки и перекрашивают
        // значок независимо от /DEBUG).
        Assert.Contains("<DataTrigger Binding=\"{Binding EnterpriseRunningStatus}\" Value=\"Hung\">", glyphStyle, StringComparison.Ordinal);
        Assert.Contains("<DataTrigger Binding=\"{Binding EnterpriseRunningStatus}\" Value=\"Critical\">", glyphStyle, StringComparison.Ordinal);
        Assert.True(
            glyphStyle.IndexOf("IsEnterpriseDebugRunning", StringComparison.Ordinal)
            < glyphStyle.IndexOf("Value=\"Hung\"", StringComparison.Ordinal),
            "триггер отладки должен идти раньше триггеров Hung/Critical");
    }

    [Fact]
    public void Wpf_EnterpriseChip_DebugTooltip_OnChip()
    {
        // Подсказка «запущено с отладкой» переехала с удалённого маркера на сам
        // чип Предприятия (перекрывает унаследованную RunningModesTooltip).
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var play = xaml.IndexOf("Kind=\"Play\" Width=\"16\" Height=\"16\"", StringComparison.Ordinal);
        var chip = xaml.LastIndexOf("<Border Width=\"18\" Height=\"18\"", play, StringComparison.Ordinal);
        var chipMarkup = xaml[chip..play];
        Assert.Contains("IsEnterpriseDebugRunning", chipMarkup, StringComparison.Ordinal);
        Assert.Contains("Infobase.DebugRunningTooltip", chipMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_DebugMarkerEllipse_Removed()
    {
        // v1.0.1.54: отдельный янтарный Ellipse-маркер отладки удалён — цвет
        // значка Предприятия сам кодирует отладку.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        Assert.DoesNotContain("<Ellipse Width=\"10\" Height=\"10\" Fill=\"{DynamicResource WarningBrush}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ConfiguratorChip_Unchanged()
    {
        // Конфигуратор не изменён: ключ (Wrench), базовый цвет WarningBrush,
        // привязки к флагу отладки Предприятия у него нет.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var wrench = xaml.IndexOf("Kind=\"Wrench\"", StringComparison.Ordinal);
        Assert.True(wrench > 0);
        var styleStart = xaml.IndexOf("<materialDesign:PackIcon.Style>", wrench, StringComparison.Ordinal);
        var styleEnd = xaml.IndexOf("</materialDesign:PackIcon.Style>", wrench, StringComparison.Ordinal);
        var glyphStyle = xaml[styleStart..styleEnd];
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource WarningBrush}\"/>", glyphStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnterpriseDebugRunning", glyphStyle, StringComparison.Ordinal);
    }

    // ===================== Avalonia (MainWindow.Avalonia.Tree.cs) =====================

    [Fact]
    public void Avalonia_EnterpriseGlyph_BindsDebugFlag()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        // Значок Предприятия — MultiBinding (статус отклика + флаг отладки)
        // через EnterpriseGlyphBrushConverter.
        Assert.Contains("EnterpriseGlyphBrushConverter", source, StringComparison.Ordinal);
        Assert.Contains("nameof(Infobase.IsEnterpriseDebugRunning)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(Infobase.EnterpriseRunningStatus)", source, StringComparison.Ordinal);
        Assert.Contains("EnterpriseGlyphBrushConverter.DefaultParameter", source, StringComparison.Ordinal);
        // Значок остался треугольником в кружке-чипе.
        Assert.Contains("RunningEnterpriseGlyph", source, StringComparison.Ordinal);
        Assert.Contains("BuildRunningChip", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_DebugMarkerEllipse_Removed()
    {
        // v1.0.1.54: отдельный янтарный кружок-маркер отладки удалён.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.DoesNotContain("debugCircle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnterpriseDebugMarkerHex", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_DebugTooltip_MovedOnChip()
    {
        // Подсказка «запущено с отладкой» — на чипе Предприятия, включается по
        // флагу IsEnterpriseDebugRunning и снимается при обычном запуске.
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        Assert.Contains("UpdateEnterpriseDebugTooltip", source, StringComparison.Ordinal);
        Assert.Contains("Infobase.DebugRunningTooltip", source, StringComparison.Ordinal);
    }

    // ===================== Avalonia: логика цвета (только Linux-сборка тестов) =====================

#if LINUX
    [Fact]
    public void EnterpriseGlyphBrushConverter_EncodesDebugAndHang()
    {
        var converter = new Converters.EnterpriseGlyphBrushConverter();
        // HEX-спецификации — тест не зависит от инициализации темы приложения.
        const string parameter = "#FF7A00|#2D6CDF";

        // Обычный запуск — оранжевый, запуск с /DEBUG — синий.
        var normal = converter.Convert(
            new object?[] { RunningDotStatus.Responding, false },
            typeof(Avalonia.Media.IBrush), parameter, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(normal);
        Assert.Equal(Avalonia.Media.Color.Parse("#FF7A00"), normal.Color);

        var debug = converter.Convert(
            new object?[] { RunningDotStatus.Responding, true },
            typeof(Avalonia.Media.IBrush), parameter, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(debug);
        Assert.Equal(Avalonia.Media.Color.Parse("#2D6CDF"), debug.Color);

        // Подсветка занятости приоритетнее отладки: зависший процесс — белый
        // значок на красном чипе (как у Конфигуратора).
        var hung = converter.Convert(
            new object?[] { RunningDotStatus.Hung, true },
            typeof(Avalonia.Media.IBrush), parameter, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(hung);
        Assert.Equal(Avalonia.Media.Colors.White, hung.Color);

        var critical = converter.Convert(
            new object?[] { RunningDotStatus.Critical, false },
            typeof(Avalonia.Media.IBrush), parameter, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(critical);
        Assert.Equal(Avalonia.Media.Colors.White, critical.Color);
    }
#endif

    // ===================== Модель =====================

    [Fact]
    public void RunningModesTooltip_DebugRunning_AppendsSuffix()
    {
        var ib = new Infobase { IsEnterpriseRunning = true };
        Assert.DoesNotContain("Infobase.DebugRunningSuffix", ib.RunningModesTooltip, StringComparison.Ordinal);

        ib.IsEnterpriseDebugRunning = true;
        Assert.EndsWith(", " + Localization.LocalizationManager.T("Infobase.DebugRunningSuffix"),
            ib.RunningModesTooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void IsEnterpriseDebugRunning_RaisesTooltipChangeNotification()
    {
        var ib = new Infobase { IsEnterpriseRunning = true };
        var changed = new List<string>();
        ib.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

        ib.IsEnterpriseDebugRunning = true;

        // Пересборки строки не нужно: и подсказка, и цвет значка обновляются
        // по подписке/привязке на PropertyChanged.
        Assert.Contains(nameof(Infobase.RunningModesTooltip), changed, StringComparer.Ordinal);
        Assert.Contains(nameof(Infobase.IsEnterpriseDebugRunning), changed, StringComparer.Ordinal);
    }
}
