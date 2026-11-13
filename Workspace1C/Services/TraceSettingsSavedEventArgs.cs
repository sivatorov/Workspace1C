using System;
using System.Collections.Generic;

namespace Workspace1C.Services;

/// <summary>
/// Аргументы события <see cref="TraceFlags.SettingsSaved"/> (issue #129): список
/// известных флагов, фактически записанных в <c>trace.json</c> вкладкой «Логирование».
/// Главное окно и <c>UpdateService</c> проверяют <see cref="Contains"/> для
/// <see cref="TraceFlags.PrevVersionFlag"/> и сразу пересчитывают подпись версии,
/// состояние значка и доступность обновления — без перезапуска приложения.
/// </summary>
public sealed class TraceSettingsSavedEventArgs : EventArgs
{
    /// <summary>Создаёт аргументы события для переданного набора сохранённых флагов.</summary>
    public TraceSettingsSavedEventArgs(IReadOnlyList<string> changedFlags)
    {
        ChangedFlags = changedFlags ?? Array.Empty<string>();
    }

    /// <summary>Имена флагов, которые были записаны (в верхнем регистре).</summary>
    public IReadOnlyList<string> ChangedFlags { get; }

    /// <summary>
    /// Был ли среди сохранённых указанный флаг (регистр безразличен).
    /// Пустое имя — всегда <c>false</c>.
    /// </summary>
    public bool Contains(string? flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
            return false;
        var changed = ChangedFlags;
        for (var i = 0; i < changed.Count; i++)
        {
            if (string.Equals(changed[i], flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
