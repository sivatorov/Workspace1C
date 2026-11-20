using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #142 (1.0.1.96): NRE при переключении периода истории в «Центре обслуживания».
/// <para>
/// Причина: обработчик <c>Checked</c> был подписан в XAML, а <c>IsChecked="True"</c>
/// у <c>Period30Radio</c> применяется во время <c>InitializeComponent()</c> — то есть
/// ДО присваивания поля <c>_vm</c>. Событие приходило в <c>OnHistoryPeriodChanged</c>
/// при <c>_vm == null</c> и падало на <c>SetHistoryPeriod</c>.
/// </para>
/// <para>
/// WPF-окно нельзя создать в кроссплатформенных тестах, поэтому порядок инициализации
/// защищён source-гвардами по разметке и code-behind; логика периода проверяется на
/// чистой ViewModel.
/// </para>
/// </summary>
[Collection("LocalDataStoreState")]
public sealed class Issue142MaintenanceCenterTests
{
    // ===================== WPF: порядок инициализации =====================

    [Fact]
    public void Wpf_Markup_HasNoCheckedHandlerOnPeriodRadios()
    {
        var xaml = ReadProjectFile("Workspace1C", "Views", "MaintenanceCenterWindow.xaml");

        // Обработчик в разметке недопустим: IsChecked="True" поднимает Checked
        // раньше, чем конструктор создаст ViewModel.
        Assert.DoesNotContain("Checked=\"OnHistoryPeriodChanged\"", xaml, StringComparison.Ordinal);
        // Начальный период остаётся 30 дней — состояние задаётся разметкой.
        Assert.Contains("x:Name=\"Period30Radio\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"True\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_CodeBehind_SubscribesAfterViewModelCreation()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "MaintenanceCenterWindow.xaml.cs");

        var vmCreated = code.IndexOf("_vm = new MaintenanceCenterViewModel", StringComparison.Ordinal);
        var subscribed = code.IndexOf("Period7Radio.Checked += OnHistoryPeriodChanged", StringComparison.Ordinal);

        Assert.True(vmCreated >= 0, "В конструкторе не найдено создание MaintenanceCenterViewModel.");
        Assert.True(subscribed >= 0, "Подписка Period7Radio.Checked += OnHistoryPeriodChanged не найдена.");
        Assert.True(vmCreated < subscribed,
            "Подписка на Checked должна идти ПОСЛЕ создания ViewModel (issue #142).");

        // Паритет: подписаны все три переключателя.
        Assert.Contains("Period30Radio.Checked += OnHistoryPeriodChanged", code, StringComparison.Ordinal);
        Assert.Contains("Period90Radio.Checked += OnHistoryPeriodChanged", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Wpf_Handler_GuardsNullViewModel()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "MaintenanceCenterWindow.xaml.cs");
        var handler = SliceBetween(code, "private void OnHistoryPeriodChanged", "private void OnBasesGridSelectionChanged");

        Assert.Contains("if (_vm is null)", handler, StringComparison.Ordinal);
        Assert.Contains("_vm.SetHistoryPeriod(days)", handler, StringComparison.Ordinal);
    }

    // ===================== Avalonia: паритет порядка инициализации =====================

    [Fact]
    public void Avalonia_SubscribesAfterViewModelCreation()
    {
        var code = ReadProjectFile("Workspace1C", "Views", "MaintenanceCenterWindow.Avalonia.cs");

        var vmCreated = code.IndexOf("DataContext = _vm = new MaintenanceCenterViewModel", StringComparison.Ordinal);
        var subscribed = code.IndexOf("IsCheckedChanged += OnHistoryPeriodChanged", StringComparison.Ordinal);

        Assert.True(vmCreated >= 0, "В Avalonia-окне не найдено создание MaintenanceCenterViewModel.");
        Assert.True(subscribed >= 0, "В Avalonia-окне не найдена подписка IsCheckedChanged.");
        Assert.True(vmCreated < subscribed,
            "Avalonia-подписка должна идти после создания ViewModel (паритет с WPF, issue #142).");
    }

    // ===================== Чистая ViewModel =====================

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    public void SetHistoryPeriod_OnEmptyList_DoesNotThrow(int days)
    {
        var vm = CreateViewModel(Array.Empty<Infobase>());

        vm.SetHistoryPeriod(days);

        Assert.Equal(days, vm.HistoryPeriodDays);
        Assert.Empty(vm.HistoryRows);
        Assert.Null(vm.SelectedHistoryRow);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(90)]
    public void SetHistoryPeriod_WithBases_DoesNotThrow(int days)
    {
        var vm = CreateViewModel(new[] { ServerBase("base-1") });

        vm.SetHistoryPeriod(days);

        Assert.Equal(days, vm.HistoryPeriodDays);
        Assert.Single(vm.AllRows);
    }

    [Fact]
    public void SwitchingPeriods_RefreshesHistoryRows()
    {
        var directory = Directory.CreateTempSubdirectory("workspace1c-issue142-");
        try
        {
            var today = DateTime.UtcNow.Date;
            var bases = new[] { ServerBase("recent"), ServerBase("monthly"), ServerBase("quarterly") };
            var olderDays = new[] { 3, 20, 60 };
            var snapshots = bases.SelectMany((infobase, index) => new[]
            {
                new SizeSnapshotEntry { InfobaseId = infobase.Id, DateUtc = today.AddDays(-olderDays[index]), SizeBytes = 1000 },
                new SizeSnapshotEntry { InfobaseId = infobase.Id, DateUtc = today, SizeBytes = 2000 },
            }).ToArray();
            var historyStore = new SizeHistoryStore(directory.FullName);
            historyStore.Save(snapshots);
            // Хранилище внедряется явно: глобальный FeatureDataStore.DirectoryOverride
            // и DI-регистрации других тестов не должны влиять на результат.
            var vm = CreateViewModel(bases, historyStore);

            foreach (var (days, count) in new[] { (7, 1), (30, 2), (90, 3), (7, 1) })
            {
                vm.SetHistoryPeriod(days);
                Assert.Equal(days, vm.HistoryPeriodDays);
                Assert.Equal(bases.Take(count).Select(b => b.Id).OrderBy(id => id),
                    vm.HistoryRows.Select(row => row.Infobase.Id).OrderBy(id => id));
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void SetHistoryPeriod_UnknownValue_FallsBackTo30()
    {
        var vm = CreateViewModel(new[] { ServerBase("base-1") });

        vm.SetHistoryPeriod(15);

        Assert.Equal(30, vm.HistoryPeriodDays);
    }

    [Fact]
    public void SelectHistoryById_BeforeAnyPeriodChange_DoesNotThrow()
    {
        var vm = CreateViewModel(new[] { ServerBase("base-1") });

        vm.SelectHistoryById(null);
        vm.SelectHistoryById("missing");

        Assert.Null(vm.SelectedHistoryRow);
    }

    // ===================== Вспомогательные методы =====================

    private static MaintenanceCenterViewModel CreateViewModel(
        IEnumerable<Infobase> bases, SizeHistoryStore? historyStore = null) =>
        new(bases, () => { }, _ => { },
            DiskFreeSpaceHelper.DefaultWarningGb, historyStore);

    private static Infobase ServerBase(string id) => new()
    {
        Id = id,
        Name = "Demo " + id,
        Connection = new ConnectionSettings
        {
            Type = ConnectionType.ClientServer,
            Server = "srv",
            DatabaseName = "demo"
        }
    };

    private static string SliceBetween(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Не найден маркер начала: {startMarker}");
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Не найден маркер конца: {endMarker}");
        return text[start..end];
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
}
