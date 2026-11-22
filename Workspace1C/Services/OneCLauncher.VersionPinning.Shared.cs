using System;
using System.IO;
using System.Linq;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Выбор версии платформы при запуске базы с учётом закрепления в ibases.v8i.
/// Общая часть <see cref="OneCLauncher"/> для Windows (WPF) и Linux (Avalonia):
/// у каждой платформы свой поиск exe и свой <c>PlatformVersionService</c>,
/// но правило выбора версии должно быть одно.
/// </summary>
public static partial class OneCLauncher
{
    /// <summary>
    /// Версия платформы для поиска исполняемого файла при запуске базы.
    /// Точная версия из карточки базы (4 сегмента) не меняется. Неполная версия
    /// («8.3», «8.3.27») заменяется версией, закреплённой за базой в ibases.v8i
    /// (DefaultVersion), если та установлена для нужной разрядности: иначе клиент
    /// запускается последней установленной сборкой линии и при старте может
    /// перезапуститься в закреплённую, потеряв параметры запуска (/WSN, /WSP).
    /// Нет записи, файла, версия не установлена — прежнее поведение (последняя
    /// установленная сборка по префиксу) и одна строка в лог о причине фолбэка.
    /// </summary>
    private static string ResolveLaunchVersion(Infobase infobase, OneCArchitecture architecture)
    {
        var requested = infobase.PlatformVersion ?? string.Empty;

        PlatformVersionService.ParseVariant(requested, out var cleanRequested, out _);
        if (PlatformVersionService.CountVersionSegments(cleanRequested) >= 4)
            return requested; // Пользователь указал точную сборку — не трогаем.

        var pinned = FindPinnedVersion(infobase.Id, IbasesV8iImporter.FindDefaultPath());
        var archKey = architecture == OneCArchitecture.x64 ? "64" : "32";
        var installed = PlatformVersionService.FindPlatformVersionDirs(archKey)
            .Select(v => v.Version)
            .ToList();

        var effective = PickEffectiveVersion(requested, pinned, installed);
        if (!string.IsNullOrWhiteSpace(pinned))
        {
            if (string.Equals(effective, pinned, StringComparison.OrdinalIgnoreCase))
                GetLogger()?.Info(
                    $"Запуск «{infobase.Name}»: версия «{requested}» из карточки заменена закреплённой в ibases.v8i «{pinned}» (DefaultVersion).");
            else
                GetLogger()?.Warn(
                    $"Запуск «{infobase.Name}»: используется последняя установленная версия по «{requested}» — закреплённая в ibases.v8i «{pinned}» не подходит (не установлена для этой разрядности или другой линии).");
        }

        return effective;
    }

    /// <summary>
    /// Выбирает версию платформы для запуска: точная версия карточки возвращается
    /// без изменений; неполная заменяется закреплённой в ibases.v8i, если та установлена
    /// и продолжает линию карточки; иначе возвращается версия карточки — поиск возьмёт
    /// последнюю установленную сборку по префиксу.
    /// </summary>
    /// <param name="requested">Версия из карточки базы (может быть частичной).</param>
    /// <param name="pinned">Версия из ibases.v8i (DefaultVersion/Version записи) или null.</param>
    /// <param name="installed">Установленные версии нужной разрядности.</param>
    public static string PickEffectiveVersion(
        string? requested, string? pinned, IReadOnlyCollection<string>? installed)
    {
        var requestedText = (requested ?? string.Empty).Trim();

        PlatformVersionService.ParseVariant(requestedText, out var cleanRequested, out _);
        if (PlatformVersionService.CountVersionSegments(cleanRequested) >= 4)
            return requestedText;

        PlatformVersionService.ParseVariant(pinned ?? string.Empty, out var cleanPinned, out _);
        if (PlatformVersionService.CountVersionSegments(cleanPinned) < 4)
            return requestedText;

        // Закреплённая версия должна продолжать линию из карточки: иначе запускалась бы
        // чужая версия вместо явно выбранной пользователем (например, 8.3 при 8.5).
        if (!VersionMatches(cleanRequested, cleanPinned))
            return requestedText;

        var isInstalled = installed is not null && installed.Any(v =>
            string.Equals((v ?? string.Empty).Trim(), cleanPinned, StringComparison.OrdinalIgnoreCase));
        return isInstalled ? cleanPinned : requestedText;
    }

    /// <summary>
    /// Читает версию, закреплённую за базой в файле ibases.v8i: DefaultVersion записи
    /// с тем же ID (GUID), иначе её Version. Только чтение; при отсутствии файла, записи
    /// или ошибке разбора возвращается null — запуск идёт по версии карточки.
    /// </summary>
    internal static string? FindPinnedVersion(string? infobaseId, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(infobaseId) || string.IsNullOrWhiteSpace(filePath))
            return null;

        try
        {
            if (!File.Exists(filePath))
                return null;

            var entry = IbaseEntry.Parse(filePath).FirstOrDefault(e =>
                !e.IsGroup
                && string.Equals(e.Id.Trim(), infobaseId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return null;

            PlatformVersionService.ParseVariant(entry.DefaultVersion, out var pinned, out _);
            if (PlatformVersionService.CountVersionSegments(pinned) >= 4)
                return pinned;

            PlatformVersionService.ParseVariant(entry.Version, out var version, out _);
            return PlatformVersionService.CountVersionSegments(version) >= 4 ? version : null;
        }
        catch
        {
            return null; // Файл недоступен или не разобран — текущее поведение без исключений.
        }
    }
}
