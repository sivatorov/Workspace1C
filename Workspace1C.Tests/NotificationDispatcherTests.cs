using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты мультиканального диспетчера уведомлений (функция №5, этап 0.3.9.180):
/// фильтрация по IsEnabled, параллельная рассылка включённым каналам, изоляция ошибок
/// отдельного канала, сохранение совместимости Show(title, message) с видом Info.
/// </summary>
public sealed class NotificationDispatcherTests
{
    private const int WaitMs = 5000;

    // ---------- Фильтрация выключенных каналов ----------

    [Fact]
    public async Task Show_AllChannelsDisabled_NothingSent()
    {
        var channel = new RecordingChannel(enabled: false);
        var dispatcher = CreateDispatcher(channel);

        dispatcher.Show("Заголовок", "Текст");

        await Task.Delay(100);
        Assert.Empty(channel.Received);
    }

    [Fact]
    public async Task Show_EnabledChannel_SendsMessage()
    {
        var channel = new RecordingChannel(enabled: true);
        var dispatcher = CreateDispatcher(channel);

        dispatcher.Show("Заголовок", "Текст");

        await channel.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        var message = Assert.Single(channel.Received);
        Assert.Equal("Заголовок", message.Title);
        Assert.Equal("Текст", message.Message);
    }

    // ---------- Совместимость старой перегрузки ----------

    [Fact]
    public async Task Show_TwoArgOverload_UsesInfoAndGeneral()
    {
        var channel = new RecordingChannel(enabled: true);
        var dispatcher = CreateDispatcher(channel);

        dispatcher.Show("T", "M");

        await channel.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        var message = Assert.Single(channel.Received);
        Assert.Equal(NotificationKind.Info, message.Kind);
        Assert.Equal(NotificationEvent.General, message.Event);
    }

    [Fact]
    public async Task Show_FiveArgOverload_PassesKindAndEvent()
    {
        var channel = new RecordingChannel(enabled: true);
        var dispatcher = CreateDispatcher(channel);

        dispatcher.Show("T", "M", NotificationKind.Error, NotificationEvent.Backup);

        await channel.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        var message = Assert.Single(channel.Received);
        Assert.Equal(NotificationKind.Error, message.Kind);
        Assert.Equal(NotificationEvent.Backup, message.Event);
    }

    // ---------- Параллельная рассылка по включённым каналам ----------

    [Fact]
    public async Task Show_TwoEnabledChannels_BothReceive()
    {
        var first = new RecordingChannel(enabled: true);
        var second = new RecordingChannel(enabled: true);
        var disabled = new RecordingChannel(enabled: false);
        var dispatcher = CreateDispatcher(first, second, disabled);

        dispatcher.Show("T", "M");

        await first.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        await second.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        Assert.Single(first.Received);
        Assert.Single(second.Received);
        Assert.Empty(disabled.Received);
    }

    // ---------- Изоляция ошибок канала ----------

    [Fact]
    public async Task Show_ChannelThrows_OtherChannelsStillReceive()
    {
        var failing = new RecordingChannel(enabled: true, throwOnSend: new InvalidOperationException("сеть недоступна"));
        var healthy = new RecordingChannel(enabled: true);
        var dispatcher = CreateDispatcher(failing, healthy);

        // Не должно быть исключения наружу.
        dispatcher.Show("T", "M");

        await healthy.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        await failing.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        Assert.Single(healthy.Received);
        Assert.Single(failing.Received); // канал был вызван, но упал внутри
    }

    [Fact]
    public async Task Show_ChannelReturnsFalse_DispatcherDoesNotThrow()
    {
        var failing = new RecordingChannel(enabled: true, result: false);
        var healthy = new RecordingChannel(enabled: true);
        var dispatcher = CreateDispatcher(failing, healthy);

        dispatcher.Show("T", "M");

        await healthy.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        await failing.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        Assert.Single(healthy.Received);
        Assert.Single(failing.Received);
    }

    // ---------- Issue #131: маршрутизация длительных операций ----------

    [Fact]
    public async Task LongOperation_ChannelSystem_OnlySystemChannelReceives()
    {
        var settings = LongOperationSettings(LongOperationNotificationChannels.System);
        var system = new RecordingChannel(enabled: true, name: "system");
        var telegram = new RecordingChannel(enabled: true, name: "telegram");
        var dispatcher = new NotificationDispatcher(new[] { system, telegram }, new FakeRepository(settings));

        dispatcher.Show(new NotificationMessage
        {
            Title = "T",
            Message = "M",
            Event = NotificationEvent.LongOperation
        });

        await system.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        await Task.Delay(150);
        Assert.Single(system.Received);
        Assert.Empty(telegram.Received);
    }

    [Fact]
    public async Task LongOperation_ChannelTelegram_OnlyTelegramReceives()
    {
        var settings = LongOperationSettings(LongOperationNotificationChannels.Telegram);
        var system = new RecordingChannel(enabled: true, name: "system");
        var telegram = new RecordingChannel(enabled: true, name: "telegram");
        var dispatcher = new NotificationDispatcher(new[] { system, telegram }, new FakeRepository(settings));

        dispatcher.Show(new NotificationMessage
        {
            Title = "T",
            Message = "M",
            Event = NotificationEvent.LongOperation
        });

        await telegram.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        await Task.Delay(150);
        Assert.Single(telegram.Received);
        Assert.Empty(system.Received);
    }

    [Fact]
    public async Task LongOperation_Disabled_NothingSent()
    {
        var settings = LongOperationSettings(LongOperationNotificationChannels.System);
        settings.LongOperationNotifications.Enabled = false;
        var system = new RecordingChannel(enabled: true, name: "system");
        var dispatcher = new NotificationDispatcher(new[] { system }, new FakeRepository(settings));

        dispatcher.Show(new NotificationMessage { Event = NotificationEvent.LongOperation });

        await Task.Delay(200);
        Assert.Empty(system.Received);
    }

    [Fact]
    public async Task LongOperation_ChannelThrows_DoesNotThrowOutward()
    {
        var settings = LongOperationSettings(LongOperationNotificationChannels.System);
        var failing = new RecordingChannel(enabled: true,
            throwOnSend: new InvalidOperationException("сбой"), name: "system");
        var dispatcher = new NotificationDispatcher(new[] { failing }, new FakeRepository(settings));

        dispatcher.Show(new NotificationMessage { Event = NotificationEvent.LongOperation });

        await failing.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        Assert.Single(failing.Received);
    }

    private static AppSettings LongOperationSettings(string channel) => new()
    {
        LongOperationNotifications = new LongOperationNotificationsSettings
        {
            Enabled = true,
            Channel = channel,
            ThresholdValue = 10,
            ThresholdUnit = LongOperationThresholdUnits.Seconds
        }
    };

    // ---------- IsEnabled получает актуальные настройки ----------

    [Fact]
    public async Task Show_IsEnabledReceivesLoadedSettings()
    {
        var settings = new AppSettings { ShowSystemNotifications = true };
        var repository = new FakeRepository(settings);
        var channel = new RecordingChannel(enabled: true);
        var dispatcher = new NotificationDispatcher(new[] { channel }, repository);

        dispatcher.Show("T", "M");

        await channel.Sent.WaitAsync(TimeSpan.FromMilliseconds(WaitMs));
        Assert.Same(settings, channel.LastSeenSettings);
    }

    // ---------- Вспомогательное ----------

    private static NotificationDispatcher CreateDispatcher(params RecordingChannel[] channels) =>
        new(channels, new FakeRepository(new AppSettings()));

    /// <summary>Фейк канала: записывает полученные сообщения, сигнализирует о завершении.</summary>
    private sealed class RecordingChannel : INotificationChannel
    {
        private readonly bool _enabled;
        private readonly bool _result;
        private readonly Exception? _throwOnSend;
        private readonly TaskCompletionSource<bool> _sent =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingChannel(bool enabled, bool result = true, Exception? throwOnSend = null,
            string name = "test")
        {
            _enabled = enabled;
            _result = result;
            _throwOnSend = throwOnSend;
            Name = name;
        }

        public string Name { get; set; } = "test";

        public List<NotificationMessage> Received { get; } = new();

        public AppSettings? LastSeenSettings { get; private set; }

        /// <summary>Задача завершается после первой отправки (для ожидания fire-and-forget).</summary>
        public Task Sent => _sent.Task;

        public bool IsEnabled(AppSettings settings)
        {
            LastSeenSettings = settings;
            return _enabled;
        }

        public Task<bool> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
        {
            Received.Add(message);
            try
            {
                if (_throwOnSend is not null)
                    throw _throwOnSend;
                return Task.FromResult(_result);
            }
            finally
            {
                _sent.TrySetResult(true);
            }
        }
    }

    /// <summary>Фейк репозитория: отдаёт заранее заданные настройки.</summary>
    private sealed class FakeRepository : IInfobaseRepository
    {
        public FakeRepository(AppSettings settings) => Settings = settings;

        public AppSettings Settings { get; }

        public List<Infobase> Load() => new();
        public void Save(List<Infobase> infobases) { }
        public Task SaveAsync(List<Infobase> infobases, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public List<Group> LoadGroups() => new();
        public void SaveGroups(List<Group> groups) { }
        public Task SaveGroupsAsync(List<Group> groups, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public AppSettings LoadSettings() => Settings;
        public void SaveSettings(AppSettings settings) { }
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}