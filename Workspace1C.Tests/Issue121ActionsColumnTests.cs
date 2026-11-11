using System;
using System.IO;
using Workspace1C.Models;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #121 (v1.0.1.54): колонка «Действия» в главном окне.
/// <list type="bullet">
/// <item>колонку можно сузить: минимум 80 (MainViewModel.ActionsColumnMinWidth) вместо
/// прежних 120 у обработчика перетаскивания, по умолчанию 120 вместо 170;</item>
/// <item>кнопки-иконки выровнены по левому краю колонки;</item>
/// <item>единый стиль иконок: круглый чип-кружок (22 px, фон карточки с обводкой) с
/// подсветкой при выделении строки — консистентно с индикаторами запуска
/// (issue #101/#103); у кнопки запуска Предприятия индикатор занятости перенесён
/// на кнопку (issue #120/#103).</item>
/// </list>
/// <para>WPF-разметка и Avalonia-код (собирается только под #if LINUX) проверяются
/// source-гвардами; логика цвета — юнит-тесты конвертеров в Linux-конфигурации
/// (-p:BuildLinux=true, по образцу <see cref="Issue103EnterpriseGlyphTests"/>).</para>
/// </summary>
public sealed class Issue121ActionsColumnTests
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

    // ===================== Ширина колонки (WPF) =====================

    [Fact]
    public void Wpf_ActionsColumn_DefaultWidthShrunk()
    {
        // Три ColumnDefinition колонки «Действия» (заголовок, группа, база):
        // по умолчанию 120 вместо прежних 170 — колонку можно сузить.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var count = CountOccurrences(
            xaml, "<MultiBinding Converter=\"{StaticResource ColumnVis}\" ConverterParameter=\"120\">");
        Assert.True(count >= 3, $"ожидались минимум 3 определения колонки «Действия» с шириной 120, найдено {count}");
        // Прежний дефолт 170 остался только у колонок «Действия» — убран полностью.
        Assert.DoesNotContain("ConverterParameter=\"170\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_ActionsColumn_MinWidthClampedOnLoad()
    {
        // Кламп сохранённой ширины при загрузке настроек (MainViewModel) и общий
        // минимум, который держит обработчик перетаскивания (MainWindow.Columns.cs).
        var vm = ReadProjectFile(Path.Combine("Workspace1C", "ViewModels", "MainViewModel.cs"));
        Assert.Contains("Math.Max(Models.AppSettings.ActionsColumnMinWidth, settings.ActionsColumnWidth)", vm, StringComparison.Ordinal);

        // Общий минимум — в модели настроек: частичный MainViewModel разделён
        // по платформам (#if WINDOWS / #if LINUX), константа нужна обеим.
        var settings = ReadProjectFile(Path.Combine("Workspace1C", "Models", "AppSettings.cs"));
        Assert.Contains("public const double ActionsColumnMinWidth = 80;", settings, StringComparison.Ordinal);

        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Columns.cs"));
        Assert.Contains("private const double ActionsColumnMinWidth = Models.AppSettings.ActionsColumnMinWidth;", columns, StringComparison.Ordinal);
    }

    // ===================== Единый стиль чипов (WPF) =====================

    [Fact]
    public void Wpf_RowActionChip_StyleExists()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        // Круглый чип-кружок с подсветкой при выделении строки (акцентная обводка).
        var chip = xaml.IndexOf("<Style x:Key=\"RowActionChip\" TargetType=\"Border\">", StringComparison.Ordinal);
        Assert.True(chip > 0, "стиль RowActionChip не найден");
        var chipBlock = xaml[chip..xaml.IndexOf("</Style>", chip, StringComparison.Ordinal)];
        // Issue #121 (v1.0.1.55): кружок уменьшен до 18 px — пропорция значок/кружок
        // как у одобренных чипов-индикаторов запуска 18/16.
        Assert.Contains("<Setter Property=\"Width\" Value=\"18\"/>", chipBlock, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"18\"/>", chipBlock, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"9\"/>", chipBlock, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource CardBackgroundBrush}", chipBlock, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource BorderBrushColor}", chipBlock, StringComparison.Ordinal);
        // Подсветка при выделении строки — по IsSelected контейнера TreeViewItem.
        Assert.Contains("Binding=\"{Binding IsSelected, RelativeSource={RelativeSource AncestorType=TreeViewItem}}\"", chipBlock, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource AccentBrush}", chipBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_RowActionButtons_WrappedInChipsAndLeftAligned()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        // Панель кнопок строки: по левому краю (issue #121), кнопки — чип-стилем.
        var rowActions = xaml.IndexOf("<StackPanel Grid.Column=\"7\" Grid.RowSpan=\"2\"", StringComparison.Ordinal);
        Assert.True(rowActions > 0, "панель кнопок строки не найдена");
        var rowBlock = xaml[rowActions..xaml.IndexOf("</StackPanel>", rowActions, StringComparison.Ordinal)];
        Assert.Contains("HorizontalAlignment=\"Left\"", rowBlock, StringComparison.Ordinal);
        Assert.Contains("RowActionChipButton", rowBlock, StringComparison.Ordinal);

        // Все три кнопки строки обёрнуты в чипы: две — прямым стилем RowActionChip,
        // кнопка Предприятия — стилем на основе RowActionChip (размеры из стиля,
        // issue #121 v1.0.1.55: локальные 22 px сняты, чип 18 px).
        Assert.Equal(3, CountOccurrences(rowBlock, "<Border Style=\"{StaticResource RowActionChip}\"")
            + CountOccurrences(rowBlock, "<Style TargetType=\"Border\" BasedOn=\"{StaticResource RowActionChip}\">"));
        Assert.Contains("Kind=\"Play\"", rowBlock, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Wrench\"", rowBlock, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Broom\"", rowBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_GroupActionButtons_WrappedInChipsAndLeftAligned()
    {
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        // Команды группы («Изменить»/«Удалить») — тот же стиль чипов, левое выравнивание.
        // Якорь — ПОСЛЕДНИЙ значок «Pencil»: первая Pencil — в верхней панели команд.
        var pencil = xaml.LastIndexOf("Kind=\"Pencil\"", StringComparison.Ordinal);
        Assert.True(pencil > 0, "значок команды группы не найден");
        var groupActions = xaml.LastIndexOf("<StackPanel Grid.Column=\"7\"", pencil, StringComparison.Ordinal);
        var groupBlock = xaml[groupActions..xaml.IndexOf("</StackPanel>", pencil, StringComparison.Ordinal)];
        Assert.Contains("HorizontalAlignment=\"Left\"", groupBlock, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Pencil\"", groupBlock, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Delete\"", groupBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("RowIconButton", groupBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_EnterpriseButton_RunningIndicatorOnButton()
    {
        // Issue #120/#103: индикатор запуска перенесён на кнопку запуска Предприятия:
        // зависший процесс красит чип в красный, значок — в белый.
        var xaml = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.xaml"));
        var rowActions = xaml.IndexOf("<StackPanel Grid.Column=\"7\" Grid.RowSpan=\"2\"", StringComparison.Ordinal);
        var rowBlock = xaml[rowActions..xaml.IndexOf("</StackPanel>", rowActions, StringComparison.Ordinal)];

        Assert.Contains("<DataTrigger Binding=\"{Binding EnterpriseRunningStatus}\" Value=\"Hung\">", rowBlock, StringComparison.Ordinal);
        Assert.Contains("<DataTrigger Binding=\"{Binding EnterpriseRunningStatus}\" Value=\"Critical\">", rowBlock, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource ErrorBrush}", rowBlock, StringComparison.Ordinal);
        Assert.Contains("<DataTrigger Binding=\"{Binding IsEnterpriseRunning}\" Value=\"True\">", rowBlock, StringComparison.Ordinal);
        Assert.Contains("<DataTrigger Binding=\"{Binding IsEnterpriseDebugRunning}\" Value=\"True\">", rowBlock, StringComparison.Ordinal);
    }

    // ===================== Avalonia (source-гварды) =====================

    [Fact]
    public void Avalonia_RowActionButton_ChipsAndLeftAligned()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        // Кнопка действия выровнена по левому краю колонки.
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Left", source, StringComparison.Ordinal);
        // Круглая подложка-кружок 18 px (issue #121 v1.0.1.55: 22 → 18),
        // кисти по выделению/статусу — конвертером.
        Assert.Contains("UiMetrics.Scaled(18)", source, StringComparison.Ordinal);
        Assert.Contains("CornerRadius = new CornerRadius(UiMetrics.Scaled(9))", source, StringComparison.Ordinal);
        Assert.Contains("RowActionChipBrushConverter", source, StringComparison.Ordinal);
        Assert.Contains("RowActionChipBrushConverter.BackgroundParameter", source, StringComparison.Ordinal);
        Assert.Contains("RowActionChipBrushConverter.BorderParameter", source, StringComparison.Ordinal);
        // Выделение строки — по IsSelected контейнера TreeViewItem.
        Assert.Contains("RelativeSourceMode.FindAncestor", source, StringComparison.Ordinal);
        Assert.Contains("typeof(TreeViewItem)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_EnterpriseButton_BindsRunningState()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Tree.cs"));
        // Цвет значка кнопки Предприятия — индикатор запуска: мульти-конвертер
        // (IsEnterpriseRunning / EnterpriseRunningStatus / IsEnterpriseDebugRunning).
        Assert.Contains("RowActionGlyphBrushConverter", source, StringComparison.Ordinal);
        Assert.Contains("nameof(Infobase.IsEnterpriseRunning)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(Infobase.EnterpriseRunningStatus)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(Infobase.IsEnterpriseDebugRunning)", source, StringComparison.Ordinal);
        // Кнопка запуска — с флагом enterpriseLaunch (индикатор занятости).
        Assert.Contains("enterpriseLaunch: true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ActionsPanel_LeftAligned()
    {
        var source = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Controls.cs"));
        // Панель кнопок больше не центрирует содержимое колонки «Действия».
        Assert.DoesNotContain("(finalSize.Width - total) / 2", source, StringComparison.Ordinal);
        Assert.Contains("Issue #121", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Avalonia_ActionsColumn_WidthAndMinClamped()
    {
        var columns = ReadProjectFile(Path.Combine("Workspace1C", "Views", "MainWindow.Avalonia.Columns.cs"));
        // Сохранённая ширина клампится к минимуму; по умолчанию 120 вместо 170.
        Assert.Contains("Math.Max(Models.AppSettings.ActionsColumnMinWidth, vm.ActionsColumnWidth)", columns, StringComparison.Ordinal);
        Assert.Contains(": 120;", columns, StringComparison.Ordinal);
        Assert.DoesNotContain(": 170;", columns, StringComparison.Ordinal);
        Assert.Contains("private static double ActionsColumnMinWidth => Models.AppSettings.ActionsColumnMinWidth;", columns, StringComparison.Ordinal);
    }

    // ===================== Модель =====================

    [Fact]
    public void ActionsColumnMinWidth_FitsThreeChips()
    {
        // Минимум должен вмещать три чипа по 22 px с отступами кнопок (2×1) и
        // панелей (6+4) — иначе часть действий становится недоступной.
        Assert.Equal(80, AppSettings.ActionsColumnMinWidth);
        Assert.True(AppSettings.ActionsColumnMinWidth >= 3 * 22 + 6, "минимум меньше трёх чипов с отступами");
        // И быть заметно меньше прежнего дефолта 120 — иначе сужение бессмысленно.
        Assert.True(AppSettings.ActionsColumnMinWidth < 120);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    // ===================== Логика цвета конвертеров (только Linux-сборка тестов) =====================

#if LINUX
    [Fact]
    public void RowActionGlyphBrushConverter_EncodesRunningDebugAndHang()
    {
        var converter = new Converters.RowActionGlyphBrushConverter();
        // HEX-спецификации — тест не зависит от инициализации темы приложения.
        const string parameter = "#111111|#FF7A00|#2D6CDF";

        // Не запущено — вторичный цвет.
        var idle = converter.Convert(
            new object?[] { false, RunningDotStatus.Responding, false },
            typeof(Avalonia.Media.IBrush), parameter, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(idle);
        Assert.Equal(Avalonia.Media.Color.Parse("#111111"), idle.Color);

        // Запущено — оранжевый (обычный запуск), синий при /DEBUG (issue #103).
        var running = converter.Convert(
            new object?[] { true, RunningDotStatus.Responding, false },
            typeof(Avalonia.Media.IBrush), parameter, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(running);
        Assert.Equal(Avalonia.Media.Color.Parse("#FF7A00"), running.Color);

        var debug = converter.Convert(
            new object?[] { true, RunningDotStatus.Responding, true },
            typeof(Avalonia.Media.IBrush), parameter, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(debug);
        Assert.Equal(Avalonia.Media.Color.Parse("#2D6CDF"), debug.Color);

        // Подсветка занятости приоритетнее: зависший процесс — белый значок.
        var hung = converter.Convert(
            new object?[] { true, RunningDotStatus.Hung, true },
            typeof(Avalonia.Media.IBrush), parameter, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(hung);
        Assert.Equal(Avalonia.Media.Colors.White, hung.Color);

        var critical = converter.Convert(
            new object?[] { false, RunningDotStatus.Critical, false },
            typeof(Avalonia.Media.IBrush), parameter, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(critical);
        Assert.Equal(Avalonia.Media.Colors.White, critical.Color);
    }

    [Fact]
    public void RowActionChipBrushConverter_SelectionAndHang()
    {
        var converter = new Converters.RowActionChipBrushConverter();
        // Обводка: тонкая в покое, акцентная на выделенной строке.
        const string border = "#777777|#2D6CDF";

        var idle = converter.Convert(
            new object?[] { RunningDotStatus.Responding, false },
            typeof(Avalonia.Media.IBrush), border, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(idle);
        Assert.Equal(Avalonia.Media.Color.Parse("#777777"), idle.Color);

        var selected = converter.Convert(
            new object?[] { RunningDotStatus.Responding, true },
            typeof(Avalonia.Media.IBrush), border, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(selected);
        Assert.Equal(Avalonia.Media.Color.Parse("#2D6CDF"), selected.Color);
    }

    [Fact]
    public void RowActionChipBrushConverter_Plain_IgnoresRunningStatus()
    {
        // Кнопки Конфигуратора и очистки кеша (-plain) не реагируют на статус
        // Предприятия: обводка остаётся «покоя» даже при Hung.
        var converter = new Converters.RowActionChipBrushConverter();
        const string border = Converters.RowActionChipBrushConverter.BorderParameter;
        const string borderPlain = Converters.RowActionChipBrushConverter.BorderPlainParameter;

        var plainIdle = converter.Convert(
            new object?[] { RunningDotStatus.Hung, false },
            typeof(Avalonia.Media.IBrush), borderPlain, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(plainIdle);

        var plainSelected = converter.Convert(
            new object?[] { RunningDotStatus.Critical, true },
            typeof(Avalonia.Media.IBrush), borderPlain, System.Globalization.CultureInfo.InvariantCulture)
            as Avalonia.Media.ISolidColorBrush;
        Assert.NotNull(plainSelected);
    }

    [Fact]
    public void RowActionChipBrushConverter_Parameters_DifferOnlyBySelection()
    {
        // Фон подложки при выделении не меняется (контраст на акцентной строке
        // держит фон карточки), меняется только обводка.
        Assert.StartsWith(
            Converters.RowActionChipBrushConverter.BackgroundParameter,
            Converters.RowActionChipBrushConverter.BackgroundPlainParameter,
            StringComparison.Ordinal);
        Assert.StartsWith(
            Converters.RowActionChipBrushConverter.BorderParameter,
            Converters.RowActionChipBrushConverter.BorderPlainParameter,
            StringComparison.Ordinal);
        Assert.EndsWith(
            Converters.RowActionChipBrushConverter.PlainSuffix,
            Converters.RowActionChipBrushConverter.BorderPlainParameter,
            StringComparison.Ordinal);
    }
#endif
}
