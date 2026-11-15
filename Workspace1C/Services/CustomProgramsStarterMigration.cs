using System;
using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Результат автопереноса «1С:Стартер» в группу «Мои программы» (issue #91).
/// </summary>
/// <param name="ProgramAdded">Запись «1С:Стартер» создана и сохранена.</param>
/// <param name="ShouldPersistFlag">
/// Флаг <see cref="AppSettings.CustomProgramsStarterMigrated"/> нужно сохранить:
/// миграция завершена (запись создана либо список уже был настроен пользователем).
/// </param>
public readonly record struct StarterMigrationResult(bool ProgramAdded, bool ShouldPersistFlag)
{
    /// <summary>Миграция не выполнялась и флаг менять не нужно.</summary>
    public static StarterMigrationResult Nothing { get; } = new(false, false);
}

/// <summary>
/// Автоперенос «1С:Стартер» в группу «Мои программы» (issue #91, комментарий 7OH):
/// при первом запуске, когда список <c>custom_programs.json</c> пуст и миграция ещё
/// не выполнялась, добавляется программа с путём к родному стартеру 1С. Чистая
/// функция без UI и без DI — вызывающий код (MainViewModel каждой платформы)
/// передаёт хранилище, флаг из настроек и способ поиска стартера, поэтому поведение
/// покрывается юнит-тестами без файловой системы установленной 1С.
/// </summary>
public static class CustomProgramsStarterMigration
{
    /// <summary>Имя записи по умолчанию, если локализованная строка не передана.</summary>
    public const string DefaultDisplayName = "1С:Стартер";

    /// <summary>
    /// Выполняет миграцию. Возвращает результат, по которому вызывающий код решает,
    /// сохранять ли флаг в настройках. Исключения хранилища и поиска стартера не
    /// выходят наружу: миграция не критична для запуска приложения.
    /// </summary>
    /// <param name="store">Хранилище программ (может быть null — миграция не нужна).</param>
    /// <param name="alreadyMigrated">Флаг из настроек: миграция уже выполнялась.</param>
    /// <param name="findStarterPath">Поиск пути к стартеру (null/пусто — не найден).</param>
    /// <param name="displayName">Локализованное имя записи.</param>
    public static StarterMigrationResult TryMigrate(
        ICustomProgramsStore? store,
        bool alreadyMigrated,
        Func<string?>? findStarterPath,
        string? displayName = null)
    {
        if (store is null || alreadyMigrated)
            return StarterMigrationResult.Nothing;

        IReadOnlyList<CustomProgram> existing;
        try
        {
            existing = store.LoadAll();
        }
        catch
        {
            return StarterMigrationResult.Nothing;
        }

        // Список уже настроен пользователем — записи не трогаем, но фиксируем
        // миграцию, чтобы стартер не появился позже после удаления всех программ.
        if (existing.Count > 0)
            return new StarterMigrationResult(false, true);

        string? starterPath = null;
        try
        {
            starterPath = findStarterPath?.Invoke();
        }
        catch
        {
            starterPath = null;
        }

        // Стартер не найден (платформа не установлена): флаг не ставим, чтобы
        // попытка повторилась при следующем запуске.
        if (string.IsNullOrWhiteSpace(starterPath))
            return StarterMigrationResult.Nothing;

        var program = new CustomProgram
        {
            Name = string.IsNullOrWhiteSpace(displayName) ? DefaultDisplayName : displayName!,
            FilePath = starterPath!,
            Order = 0,
            Enabled = true
        };

        try
        {
            store.Save(program);
        }
        catch
        {
            return StarterMigrationResult.Nothing;
        }

        return new StarterMigrationResult(true, true);
    }
}
