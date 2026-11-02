using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.ViewModels;

/// <summary>
/// Индикатор фоновых загрузок в главном окне (issue #334 п.1): подписка на
/// <see cref="Services.BackgroundDownloadManager"/> (общий для приложения), агрегированный
/// текст статуса («Скачивается: файл (45%)…» / «Скачивается файлов: N…»), оценка
/// оставшегося времени (issue #360) и команда отмены.
/// Issue #89: события менеджера приходят из фоновых потоков загрузки. WPF прощает
/// обновление скалярных observable-свойств из чужого потока (авто-маршалинг биндингов),
/// Avalonia бросает «InvalidOperationException: Call from invalid thread» при применении
/// значения к контролу. Поэтому каждое изменение свойств доставляется в UI-поток через
/// <see cref="SynchronizationContext"/>, захваченный при конструировании VM (в тестах
/// контекста нет — вызов прямой), см. <see cref="DispatchDownloadsUpdate"/>.
/// </summary>
public partial class MainViewModel
{
    private bool _hasActiveDownloads;
    private string _downloadsStatusText = string.Empty;
    private string _downloadsEtaText = string.Empty;
    private double _activeDownloadsProgress;
    private RelayCommand? _cancelDownloadsCommand;

    /// <summary>True — есть активные фоновые загрузки (индикатор в статусной строке).</summary>
    public bool HasActiveDownloads
    {
        get => _hasActiveDownloads;
        private set => SetProperty(ref _hasActiveDownloads, value);
    }

    /// <summary>Агрегированный текст активных загрузок (пусто — ничего не скачивается).</summary>
    public string DownloadsStatusText
    {
        get => _downloadsStatusText;
        private set => SetProperty(ref _downloadsStatusText, value);
    }

    /// <summary>Агрегированный прогресс активных загрузок 0..1 (среднее по активным).</summary>
    public double ActiveDownloadsProgress
    {
        get => _activeDownloadsProgress;
        private set => SetProperty(ref _activeDownloadsProgress, value);
    }

    /// <summary>Оценка оставшегося времени активных загрузок (issue #360): «16:13» / «2:15:18»;
    /// пусто — данных ещё мало. Для нескольких загрузок — максимальная оценка
    /// (время, когда завершатся все).</summary>
    public string DownloadsEtaText
    {
        get => _downloadsEtaText;
        private set => SetProperty(ref _downloadsEtaText, value);
    }

    /// <summary>Команда «Отменить загрузки»: отменяет все активные фоновые загрузки.</summary>
    public ICommand CancelDownloadsCommand =>
        _cancelDownloadsCommand ??= new RelayCommand(
            () => Services.BackgroundDownloadManager.Default.CancelAll(),
            () => HasActiveDownloads);

    /// <summary>Пункт выбора окна для активной фоновой загрузки (issue #359).</summary>
    /// <param name="Id">Идентификатор записи менеджера загрузок.</param>
    /// <param name="Title">Название скачиваемого файла.</param>
    /// <param name="Progress">Прогресс 0..1.</param>
    /// <param name="WindowKey">Ключ целевого окна (для группировки одинаковых окон).</param>
    /// <param name="WindowLabel">Подпись целевого окна (локализована).</param>
    public sealed record DownloadChoice(string Id, string Title, double Progress, string WindowKey, string WindowLabel);

    /// <summary>Активные фоновые загрузки с привязкой к целевым окнам (issue #359).</summary>
    public IReadOnlyList<DownloadChoice> DownloadChoices => BuildDownloadChoices();

    /// <summary>
    /// True — активные загрузки идут из разных окон: клик по индикатору показывает
    /// выбор окна, а не открывает одно наугад (issue #359).
    /// </summary>
    public bool ShouldShowDownloadPicker =>
        BuildDownloadChoices().Select(c => c.WindowKey).Distinct(StringComparer.Ordinal).Count() > 1;

    /// <summary>Открывает окно первой активной загрузки; загрузок нет — окно скачивания платформы.</summary>
    public void OpenFirstDownloadWindow()
    {
        var choices = BuildDownloadChoices();
        if (choices.Count > 0)
            OpenWindowForDownload(choices[0].Id);
        else
            ShowPlatformDownloadCommand.Execute(null);
    }

    /// <summary>Открывает окно, соответствующее активной загрузке с указанным id (issue #359).</summary>
    public void OpenWindowForDownload(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        if (id.StartsWith("platform-update:", StringComparison.Ordinal))
        {
            ShowPlatformUpdateCommand.Execute(null);
            return;
        }
        if (id.StartsWith("platform:", StringComparison.Ordinal))
        {
            ShowPlatformDownloadCommand.Execute(null);
            return;
        }

        // update:{имя}:{файл} | chain:{имя} — окно проверки обновлений для этой ИБ.
        var infobase = Infobases.FirstOrDefault(x =>
            string.Equals(x.Name, ExtractDownloadBaseName(id), StringComparison.OrdinalIgnoreCase));
        if (infobase is not null)
            OpenUpdateCheckWindowFor(infobase);
    }

    /// <summary>Имя ИБ из id записи update:{имя}:{файл} / chain:{имя} (для platform: — хвост id).</summary>
    private static string ExtractDownloadBaseName(string id)
    {
        var sep = id.IndexOf(':');
        if (sep < 0 || sep == id.Length - 1)
            return string.Empty;
        var rest = id[(sep + 1)..];
        if (!id.StartsWith("update:", StringComparison.Ordinal))
            return rest;
        var cut = rest.LastIndexOf(':');
        return cut >= 0 ? rest[..cut] : rest;
    }

    /// <summary>Ключ группировки и локализованная подпись целевого окна для id записи (issue #359).</summary>
    private (string Key, string Label) DescribeDownloadWindow(string id)
    {
        if (id.StartsWith("platform-update:", StringComparison.Ordinal))
            return ("platform-update", LocalizationManager.T("PlatformUpdate.WindowTitle"));
        if (id.StartsWith("platform:", StringComparison.Ordinal))
            return ("platform", LocalizationManager.T("PlatformDownload.WindowTitle"));

        var name = ExtractDownloadBaseName(id);
        var checkTitle = LocalizationManager.T("Updates.CheckTitle");
        var label = string.IsNullOrEmpty(name)
            ? checkTitle
            : string.Format("{0} — {1}", checkTitle, name);
        return ("update-check:" + name, label);
    }

    /// <summary>Снимок активных загрузок менеджера в виде пунктов выбора окон.</summary>
    private IReadOnlyList<DownloadChoice> BuildDownloadChoices()
    {
        var active = Services.BackgroundDownloadManager.Default.Snapshot()
            .Where(d => d.IsActive)
            .ToList();
        var choices = new List<DownloadChoice>(active.Count);
        foreach (var d in active)
        {
            var (key, label) = DescribeDownloadWindow(d.Id);
            choices.Add(new DownloadChoice(d.Id, d.Title, d.Progress, key, label));
        }
        return choices;
    }

    /// <summary>Открывает окно проверки обновлений для указанной ИБ (реализация — в платформенной части VM).</summary>
    partial void OpenUpdateCheckWindowFor(Infobase infobase);

    /// <summary>Подписка на менеджер фоновых загрузок (вызывается из конструктора).</summary>
    private void InitializeBackgroundDownloadsIndicator()
    {
        // issue #89: конструктор VM выполняется в UI-потоке — контекст пригоден
        // для доставки обновлений индикатора из фоновых потоков загрузки.
        _downloadsUiContext = SynchronizationContext.Current;
        Services.BackgroundDownloadManager.Default.Changed += OnBackgroundDownloadsChanged;
        RefreshDownloadsIndicator();
    }

    /// <summary>Контекст UI-потока, захваченный при конструировании VM (issue #89).</summary>
    private SynchronizationContext? _downloadsUiContext;

    private void OnBackgroundDownloadsChanged()
        => DispatchDownloadsUpdate(_downloadsUiContext, RefreshDownloadsIndicator);

    /// <summary>
    /// Доставляет изменение UI-состояния индикатора в захваченный UI-поток (issue #89).
    /// Контекст не захвачен (тесты) или событие уже в UI-потоке — прямой вызов;
    /// иначе — <see cref="SynchronizationContext.Post"/> (WPF/Avalonia маршаллят оба).
    /// Внутренний статический метод — для юнит-теста маршалинга без конструирования VM.
    /// </summary>
    internal static void DispatchDownloadsUpdate(SynchronizationContext? uiContext, Action action)
    {
        if (uiContext is null || SynchronizationContext.Current == uiContext)
        {
            action();
            return;
        }
        uiContext.Post(static _ =>
        {
            if (_ is Action a)
                a();
        }, action);
    }

    /// <summary>Пересчитывает индикатор из текущего снимка менеджера.</summary>
    private void RefreshDownloadsIndicator()
    {
        var active = Services.BackgroundDownloadManager.Default.Snapshot()
            .Where(d => d.IsActive)
            .ToList();

        HasActiveDownloads = active.Count > 0;
        DownloadsStatusText = active.Count switch
        {
            0 => string.Empty,
            1 => string.Format(
                LocalizationManager.T("Main.Downloads.ActiveSingle"),
                active[0].Title,
                active[0].Progress),
            _ => string.Format(LocalizationManager.T("Main.Downloads.ActiveMany"), active.Count),
        };
        ActiveDownloadsProgress = active.Count == 0
            ? 0
            : active.Average(d => d.Progress);
        DownloadsEtaText = Services.DownloadEta.FormatMax(
            active.Select(d => d.EstimatedRemaining));

        _cancelDownloadsCommand?.RaiseCanExecuteChanged();
    }
}
