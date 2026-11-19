using System;
using Workspace1C.Localization;

namespace Workspace1C.Services;

/// <summary>Решение о запуске загрузки при проверке пересечения по целевому файлу (issue #22).</summary>
public enum DownloadConflictDecision
{
    /// <summary>Файл свободен — загрузку можно начинать.</summary>
    Proceed,

    /// <summary>Файл уже качается в фоне: отменить фоновую загрузку и начать заново.</summary>
    CancelBackgroundAndRestart,

    /// <summary>Файл уже качается в фоне: подождать (вторую загрузку не начинать).</summary>
    Wait,

    /// <summary>Пользователь отменил действие.</summary>
    Cancel,
}

/// <summary>
/// Единая точка проверки пересечения загрузок по целевому файлу (issue #22, Этап C):
/// перед ручным запуском загрузки проверяет <see cref="BackgroundDownloadManager.IsFileBeingDownloaded"/>
/// и при активной загрузке того же файла предлагает пользователю варианты
/// «Отменить фоновую загрузку и начать заново» / «Подождать» / «Отмена».
/// Чистый класс: диалог показывается через инжектируемый делегат, локализация — через
/// <see cref="LocalizationManager"/>; покрывается юнит-тестами.
/// </summary>
public sealed class DownloadConflictGuard
{
    /// <summary>Ключ сообщения «файл уже скачивается в фоне: {0}» (подстановка — заголовок активной загрузки).</summary>
    public const string FileAlreadyDownloadingKey = "Updates.FileAlreadyDownloading";

    /// <summary>Ключ заголовка диалога «Файл уже скачивается».</summary>
    public const string FileAlreadyDownloadingTitleKey = "Updates.FileAlreadyDownloadingTitle";

    /// <summary>Ключ кнопки «Отменить фоновую загрузку и начать заново».</summary>
    public const string CancelAndRestartKey = "Updates.FileAlreadyDownloadingCancel";

    /// <summary>Ключ кнопки «Подождать».</summary>
    public const string WaitKey = "Updates.FileAlreadyDownloadingWait";

    /// <summary>Ключ кнопки «Отмена».</summary>
    public const string CancelKey = "Common.Cancel";

    private readonly BackgroundDownloadManager _manager;

    /// <param name="manager">Менеджер фоновых загрузок, в котором проверяется активность по целевому файлу.</param>
    public DownloadConflictGuard(BackgroundDownloadManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    /// <summary>Проверяет целевой файл: null — свободен; иначе — активная загрузка того же
    /// файла (заголовок — в <see cref="BackgroundDownloadManager.ActiveDownload.Title"/>).</summary>
    public BackgroundDownloadManager.ActiveDownload? FindActiveDownload(string targetPath)
        => _manager.FindByTargetPath(targetPath);

    /// <summary>UI-сценарий: если файл уже качается — формирует локализованные заголовок и
    /// сообщение и показывает диалог через <paramref name="ask"/> (заголовок, сообщение →
    /// выбранное действие); если файл свободен — <see cref="DownloadConflictDecision.Proceed"/>
    /// без диалога.</summary>
    public DownloadConflictDecision Resolve(string targetPath, Func<string, string, DownloadConflictDecision> ask)
    {
        var active = FindActiveDownload(targetPath);
        if (active is null)
            return DownloadConflictDecision.Proceed;

        var message = string.Format(LocalizationManager.T(FileAlreadyDownloadingKey), active.Title);
        var title = LocalizationManager.T(FileAlreadyDownloadingTitleKey);
        return ask(title, message);
    }
}
