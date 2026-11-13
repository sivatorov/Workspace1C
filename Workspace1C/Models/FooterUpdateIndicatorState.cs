namespace Workspace1C.Models;

/// <summary>
/// Состояние значка проверки обновлений в подвале главного окна (issue #116).
/// Начиная с v1.0.1.61 значок доступен во <b>всех</b> режимах обновления:
/// в автоматических режимах без найденного релиза он больше не скрывается,
/// поэтому ручная проверка обновлений всегда доступна кликом по стрелкам
/// синхронизации. Логика вынесена из WPF/Avalonia-окон в чистую функцию,
/// чтобы покрывать её юнит-тестами без UI.
/// </summary>
/// <param name="IsVisible">Значок виден в подвале (всегда <c>true</c>).</param>
/// <param name="ShowSyncIcon">Показывать стрелки синхронизации (можно проверить вручную).</param>
/// <param name="ShowWarningIcon">Показывать значок «внимание» (есть доступный релиз).</param>
/// <param name="ShowReadyIcon">Показывать зелёную галочку (файл скачан и готов к установке).</param>
public readonly record struct FooterUpdateIndicatorState(
    bool IsVisible,
    bool ShowSyncIcon,
    bool ShowWarningIcon,
    bool ShowReadyIcon)
{
    /// <summary>
    /// Рассчитывает состояние значка обновления (issue #116).
    /// <paramref name="mode"/> намеренно НЕ влияет на видимость: значок доступен
    /// во всех режимах (v1.0.1.61). Параметр оставлен в сигнатуре, чтобы
    /// вызывающий код явно передавал режим, а тесты фиксировали независимость
    /// видимости от режима.
    /// </summary>
    /// <param name="mode">Текущий режим обновления приложения.</param>
    /// <param name="hasRelease">Есть доступный (найденный) релиз.</param>
    /// <param name="isReady">Файл обновления скачан и готов к установке.</param>
    public static FooterUpdateIndicatorState Resolve(
        AppUpdateMode mode,
        bool hasRelease,
        bool isReady)
    {
        // Режим обновления не должен скрывать значок: даже в авто-режимах
        // («Обновлять при запуске», «Загружать с предложением установки»)
        // без найденного релиза пользователь может проверить обновления вручную.
        _ = mode;

        return new FooterUpdateIndicatorState(
            IsVisible: true,
            ShowSyncIcon: !hasRelease && !isReady,
            ShowWarningIcon: hasRelease && !isReady,
            ShowReadyIcon: isReady);
    }
}
