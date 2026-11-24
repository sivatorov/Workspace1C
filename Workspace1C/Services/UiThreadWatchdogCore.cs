using System;

namespace Workspace1C.Services;

/// <summary>
/// Сторож UI-потока (issue #143): фиксирует в журнале факт «UI-поток не отвечает
/// больше порога» вместе с последним этапом операции.
/// <para>
/// Платформенная часть (WPF/Avalonia) раз в 500 мс вызывает <see cref="Beat"/> из
/// <c>DispatcherTimer</c>. Собственный выделенный поток (не пул) проверяет, растёт ли
/// счётчик; если UI-поток занят (например, синхронной файловой операцией), heartbeat
/// перестаёт обновляться, и сторож пишет WARN с последним этапом
/// <see cref="DbmsBackupTrace.LastStageDescription"/>. Это единственный способ
/// подтвердить причину зависания у пользователя.
/// </para>
/// <para>
/// Класс не зависит от UI-библиотек — логика тестируется напрямую
/// (<see cref="CheckOnce"/>).
/// </para>
/// </summary>
public sealed class UiThreadWatchdogCore
{
    private readonly TimeSpan _stallThreshold;
    private readonly TimeSpan _checkInterval;
    private readonly Func<string> _lastStage;
    private readonly Action<string> _report;
    private long _lastBeatTicks;
    private int _stallReported;
    private Thread? _thread;
    private volatile bool _running;

    /// <param name="stallThreshold">Порог паузы (по умолчанию 3 с).</param>
    /// <param name="checkInterval">Период фоновой проверки (по умолчанию 500 мс).</param>
    /// <param name="lastStage">Поставщик описания последнего этапа операции.</param>
    /// <param name="report">Куда писать предупреждение (журнал диагностики).</param>
    public UiThreadWatchdogCore(TimeSpan stallThreshold, TimeSpan checkInterval,
        Func<string> lastStage, Action<string> report)
    {
        _stallThreshold = stallThreshold > TimeSpan.Zero ? stallThreshold : TimeSpan.FromSeconds(3);
        _checkInterval = checkInterval > TimeSpan.Zero ? checkInterval : TimeSpan.FromMilliseconds(500);
        _lastStage = lastStage ?? (() => "none");
        _report = report ?? (_ => { });
        _lastBeatTicks = Environment.TickCount64;
    }

    /// <summary>Фоновый сторож работает.</summary>
    public bool IsRunning => _running;

    /// <summary>Heartbeat UI-потока: вызывается из <c>DispatcherTimer</c>.</summary>
    public void Beat()
    {
        Interlocked.Exchange(ref _lastBeatTicks, Environment.TickCount64);
        Interlocked.Exchange(ref _stallReported, 0);
    }

    /// <summary>
    /// Одна проверка паузы. Возвращает <c>true</c>, если пауза зафиксирована именно
    /// этим вызовом (повторные вызовы в той же паузе не дублируют запись).
    /// </summary>
    public bool CheckOnce()
    {
        var idle = Environment.TickCount64 - Interlocked.Read(ref _lastBeatTicks);
        if (idle < (long)_stallThreshold.TotalMilliseconds)
        {
            Interlocked.Exchange(ref _stallReported, 0);
            return false;
        }

        if (Interlocked.CompareExchange(ref _stallReported, 1, 0) != 0)
            return false;

        var stage = SafeStage();
        try
        {
            _report($"UI-поток не отвечает {idle} мс, последний этап = {stage}");
        }
        catch
        {
            // Сторож не должен сам ронять приложение.
        }
        return true;
    }

    /// <summary>Запускает выделенный поток проверки.</summary>
    public void Start()
    {
        if (_running)
            return;
        _running = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "ui-thread-watchdog"
        };
        _thread.Start();
    }

    /// <summary>Останавливает фоновую проверку.</summary>
    public void Stop() => _running = false;

    private void Loop()
    {
        while (_running)
        {
            try
            {
                Thread.Sleep(_checkInterval);
            }
            catch
            {
                return;
            }

            if (!_running)
                return;
            CheckOnce();
        }
    }

    private string SafeStage()
    {
        try
        {
            var stage = _lastStage();
            return string.IsNullOrWhiteSpace(stage) ? "none" : stage;
        }
        catch
        {
            return "unknown";
        }
    }
}
