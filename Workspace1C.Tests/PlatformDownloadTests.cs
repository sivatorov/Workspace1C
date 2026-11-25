using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты загрузки дистрибутива технологической платформы (этап 0.3.9.210): выбор
/// стратегии «многопоточная vs однопоточная», имя итогового файла и сетевая часть
/// <see cref="OneCUpdatesService.DownloadDistributionAsync"/> на fake-обработчике
/// (parallel-first + fallback, отмена, сетевая ошибка). Реальная сеть не используется.
/// </summary>
public sealed class PlatformDownloadTests
{
    private const long MiB = 1024 * 1024;

    private const string DistributionUrl = "https://releases.1c.ru/dist/8.3.27.2214_x64.zip";

    // ======================= ChooseDownloadStrategy =======================

    [Fact]
    public void ChooseDownloadStrategy_TinyFile_ReturnsFalse()
    {
        // Меньше минимального сегмента и даже 2 МБ — многопоточность бессмысленна.
        Assert.False(OneCUpdatesService.ChooseDownloadStrategy(0));
        Assert.False(OneCUpdatesService.ChooseDownloadStrategy(MiB / 2));   // 512 КБ
        Assert.False(OneCUpdatesService.ChooseDownloadStrategy(MiB));       // ровно 1 МБ — один сегмент
        Assert.False(OneCUpdatesService.ChooseDownloadStrategy(MiB + 1));   // чуть больше — всё ещё один сегмент
    }

    [Fact]
    public void ChooseDownloadStrategy_LargeFile_ReturnsTrue()
    {
        Assert.True(OneCUpdatesService.ChooseDownloadStrategy(100 * MiB));
        Assert.True(OneCUpdatesService.ChooseDownloadStrategy(10L * MiB + 13));
    }

    [Fact]
    public void ChooseDownloadStrategy_Boundary_MatchesCanParallelize()
    {
        // Граница «параллельность имеет смысл»: totalBytes ≥ 2 * MinSegmentBytes (2 МБ).
        // Ровно на границе поведение совпадает с CanParallelize (два сегмента по 1 МБ).
        const long boundary = 2 * MiB;
        var expected = ParallelDownloader.CanParallelize(boundary, ParallelDownloader.DefaultMaxParallelism);

        Assert.Equal(expected, OneCUpdatesService.ChooseDownloadStrategy(boundary));
        Assert.True(OneCUpdatesService.ChooseDownloadStrategy(boundary));
    }

    // ======================= BuildTargetFileName =======================

    [Fact]
    public void BuildTargetFileName_KeepsFileNameWithoutVersionPrefix()
    {
        // Issue #162: имя сохраняется ровно как объявлено в каталоге/логе загрузки —
        // префикс версии больше не добавляется (раньше получалось
        // «8.3.27.2214_8.3.27.2214_x64.zip», а в журнале было другое имя).
        Assert.Equal("8.3.27.2214_x64.zip",
            OneCUpdatesService.BuildTargetFileName("8.3.27.2214", "8.3.27.2214_x64.zip"));
    }

    [Fact]
    public void BuildTargetFileName_SanitizesInvalidCharacters()
    {
        // '/' недопустим в именах файлов на обеих платформах (Windows и Linux) —
        // тест стабилен при прогоне под любой ОС.
        Assert.Equal("8.3.27.2214_x64_server.zip",
            OneCUpdatesService.BuildTargetFileName("8.3.27.2214", "8.3.27.2214/x64/server.zip"));

        Assert.Equal("a_b.zip",
            OneCUpdatesService.BuildTargetFileName("8.3.27.2214", "a\0b.zip"));
    }

    [Fact]
    public void BuildTargetFileName_EmptyFileName_ReturnsVersionOnly()
    {
        Assert.Equal("8.3.27.2214", OneCUpdatesService.BuildTargetFileName("8.3.27.2214", string.Empty));
        Assert.Equal("8.3.27.2214", OneCUpdatesService.BuildTargetFileName("8.3.27.2214", null!));
    }

    [Fact]
    public void BuildTargetFileName_EmptyVersion_ReturnsSanitizedFileName()
    {
        Assert.Equal("platform.zip", OneCUpdatesService.BuildTargetFileName(string.Empty, "platform.zip"));
        Assert.Equal(string.Empty, OneCUpdatesService.BuildTargetFileName(string.Empty, string.Empty));
    }

    // ======================= DownloadDistributionAsync =======================

    [Fact]
    public async Task DownloadDistributionAsync_ParallelSuccess_ReturnsPathAndProgress()
    {
        var data = CreatePatternData(2 * MiB); // ровно две зоны по 1 МБ
        var handler = new FakeHttpHandler((request, ct) =>
        {
            var range = request.Headers.Range?.Ranges.FirstOrDefault();
            if (range is not null && range.From.HasValue && range.To.HasValue)
                return Partial(range.From.Value, range.To.Value, data);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
        });

        var service = CreateService(handler);
        var targetPath = Path.Combine(Path.GetTempPath(), $"cm_pdlt_{Guid.NewGuid():N}", "platform.zip");

        try
        {
            // Прогресс отдаётся из фоновых потоков параллельной загрузки: сбор в List
            // не потокобезопасен (значения могут перезаписать друг друга), поэтому
            // добавление/чтение — под общим lock (порядок вызовов уже монотонный:
            // ParallelProgressAggregator публикует только возрастающие проценты).
            // Progress<T> без SynchronizationContext доставляет отчёты через пул потоков
            // асинхронно — при параллельном прогоне всего набора финальный отчёт 1.0 мог
            // не успеть к проверке (флак «последним успевал 0.5»). Синхронная реализация
            // делает проверку детерминированной.
            var progressGate = new object();
            var progressValues = new List<double>();
            var result = await service.DownloadDistributionAsync(
                DistributionUrl, targetPath,
                new SyncProgress(value => { lock (progressGate) { progressValues.Add(value); } }));

            // Успех параллельного пути: возвращён путь, файл существует и целостен.
            Assert.Equal(targetPath, result);
            Assert.True(File.Exists(targetPath));
            Assert.Equal(data, await File.ReadAllBytesAsync(targetPath));

            // ParallelDownloader выполняет: probe (Range 0-0) + по куску на каждую зону.
            Assert.True(handler.RequestCount >= 3, $"Ожидалось ≥3 запросов, фактически {handler.RequestCount}");

            // Прогресс монотонно растёт и завершается на 1. Progress<T> в юнит-тесте постит
            // отчёты в пул потоков (SynchronizationContext отсутствует), поэтому финальный
            // отчёт «1.0» (ParallelDownloader.Publish синхронен) может прийти ПОСЛЕ возврата
            // DownloadDistributionAsync — ждём его с таймаутом, чтобы проверка не зависела
            // от таймингов планировщика (флак: последним успевал 0.5).
            var deadline = DateTime.UtcNow.AddSeconds(5);
            lock (progressGate)
            {
                while ((progressValues.Count == 0 || progressValues[^1] < 1.0) && DateTime.UtcNow < deadline)
                    Monitor.Wait(progressGate, TimeSpan.FromMilliseconds(50));

                Assert.NotEmpty(progressValues);
                Assert.Equal(1.0, progressValues[^1]);
                for (var i = 1; i < progressValues.Count; i++)
                    Assert.True(progressValues[i] >= progressValues[i - 1],
                        $"Прогресс убывает: {progressValues[i - 1]} -> {progressValues[i]}");
            }
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(targetPath)!);
        }
    }

    [Fact]
    public async Task DownloadDistributionAsync_ServerIgnoresRange_FallsBackToSingleThreaded()
    {
        var data = CreatePatternData(MiB / 2); // малый файл — параллельный путь невозможен
        var handler = new FakeHttpHandler((request, ct) =>
            // Сервер игнорирует Range: всегда полный файл с кодом 200.
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });

        var service = CreateService(handler);
        var targetPath = Path.Combine(Path.GetTempPath(), $"cm_pdlt_{Guid.NewGuid():N}", "platform.zip");

        try
        {
            var result = await service.DownloadDistributionAsync(DistributionUrl, targetPath);

            // Fallback (однопоточный путь с авторизацией) сохранил файл.
            Assert.Equal(targetPath, result);
            Assert.True(File.Exists(targetPath));
            Assert.Equal(data, await File.ReadAllBytesAsync(targetPath));

            // Запрос без Range (однопоточный путь) тоже был выполнен.
            Assert.Contains(handler.Requests, r => r.Headers.Range is null);
        }
        finally
        {
            TryDeleteDirectory(Path.GetDirectoryName(targetPath)!);
        }
    }

    [Fact]
    public async Task DownloadDistributionAsync_Cancelled_ReturnsNullAndRemovesPartial()
    {
        var data = CreatePatternData(2 * MiB);
        using var cts = new CancellationTokenSource();

        var requestNumber = 0;
        var handler = new FakeHttpHandler((request, ct) =>
        {
            requestNumber++;
            if (requestNumber == 1)
                return Partial(0, 0, data); // probe: поддержка Range есть, файл большой

            // Первый же запрос сегмента отменяет операцию.
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        var service = CreateService(handler);
        var dir = Path.Combine(Path.GetTempPath(), $"cm_pdlt_{Guid.NewGuid():N}");
        var targetPath = Path.Combine(dir, "platform.zip");

        try
        {
            var result = await service.DownloadDistributionAsync(DistributionUrl, targetPath, ct: cts.Token);

            Assert.Null(result);
            Assert.False(File.Exists(targetPath));
            // Частичные .part-файлы параллельной загрузки удалены.
            Assert.Empty(Directory.Exists(dir) ? Directory.GetFiles(dir, "*.part") : Array.Empty<string>());
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public async Task DownloadDistributionAsync_NetworkFailsInBothPaths_ReturnsNull()
    {
        var handler = new FakeHttpHandler((request, ct) => throw new HttpRequestException("network is down"));

        var service = CreateService(handler);
        var dir = Path.Combine(Path.GetTempPath(), $"cm_pdlt_{Guid.NewGuid():N}");
        var targetPath = Path.Combine(dir, "platform.zip");

        try
        {
            var result = await service.DownloadDistributionAsync(DistributionUrl, targetPath);

            Assert.Null(result);
            Assert.False(File.Exists(targetPath));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    // ======================= Вспомогательное =======================

    /// <summary>Сервис с fake HTTP-обработчиком и пустыми настройками портала.</summary>
    private static OneCUpdatesService CreateService(HttpMessageHandler handler)
        => new(new FakeRepository(), new FakeLogger(), handler);

    /// <summary>Детерминированный набор байт для проверки целостности сохранённого файла.</summary>
    private static byte[] CreatePatternData(long size)
    {
        var data = new byte[size];
        for (var i = 0L; i < size; i++)
            data[i] = (byte)(i % 251);
        return data;
    }

    /// <summary>Ответ 206 Partial Content с диапазоном <paramref name="from"/>–<paramref name="to"/>.</summary>
    private static HttpResponseMessage Partial(long from, long to, byte[] data)
    {
        var slice = new byte[to - from + 1];
        Array.Copy(data, from, slice, 0, slice.Length);
        var content = new ByteArrayContent(slice);
        content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, data.LongLength);
        return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Не критично для теста.
        }
    }

    /// <summary>Fake HTTP-обработчик: каждый запрос обрабатывается делегатом теста.</summary>
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;
        private readonly object _gate = new();
        private int _requestCount;

        public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public int RequestCount
        {
            get { lock (_gate) { return _requestCount; } }
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _requestCount++;
                Requests.Add(request);
            }

            var response = _responder(request, cancellationToken);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>Fake репозитория: настройки портала в памяти, базы/группы пустые.</summary>
    private sealed class FakeRepository : IInfobaseRepository
    {
        public AppSettings Settings { get; set; } = new();

        public List<Infobase> Load() => new();

        public void Save(List<Infobase> infobases)
        {
        }

        public Task SaveAsync(List<Infobase> infobases, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public List<Group> LoadGroups() => new();

        public void SaveGroups(List<Group> groups)
        {
        }

        public Task SaveGroupsAsync(List<Group> groups, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public AppSettings LoadSettings() => Settings;

        public void SaveSettings(AppSettings settings) => Settings = settings;

        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    /// <summary>Fake логгер: все сообщения накапливаются в списке.</summary>
    private sealed class FakeLogger : IAppLogger
    {
        public List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);

        public void Warn(string message) => Messages.Add(message);

        public void Error(string message, Exception? exception = null) => Messages.Add(message);
    }

    /// <summary>
    /// Синхронный <see cref="IProgress{T}"/>: отчёт вызывается немедленно из потока,
    /// публикующего прогресс, без постинга в пул потоков (в отличие от
    /// <see cref="Progress{T}"/> без SynchronizationContext). Используется в тестах
    /// прогресса загрузки, чтобы финальный отчёт «1.0» не терялся в параллельном прогоне
    /// всего набора (флак «последним успевал 0.5», issue #330).
    /// </summary>
    private sealed class SyncProgress : IProgress<double>
    {
        private readonly Action<double> _report;

        public SyncProgress(Action<double> report) => _report = report;

        public void Report(double value) => _report(value);
    }
}