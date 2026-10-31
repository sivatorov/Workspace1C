#if LINUX
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>Main ViewModel (Avalonia/Linux): экспорт/импорт, обнаружение платформ/шаблонов, утилиты (partial).</summary>
public partial class MainViewModel : ViewModelBase
{
    /// <summary>Версии платформы, найденные штатными путями и дополнительными путями
    /// из настроек. Дополнительные пути в Linux-ветке до сих пор никуда
    /// не передавались, и настройка была бесполезной.
    /// </summary>
    public List<string> FindPlatformVersions(IEnumerable<string>? additionalPaths = null)
    {
        try { return _platformService.FindInstalledVersions(additionalPaths ?? _settings.AdditionalPlatformSearchPaths); }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось получить список версий платформы: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>Дополнительные пути поиска платформы из настроек.</summary>
    public IReadOnlyList<string> AdditionalPlatformSearchPaths => _settings.AdditionalPlatformSearchPaths;

    /// <summary>Режим «Разрядности по умолчанию»: «X64», «X86» или «Priority».</summary>
    public string DefaultArchitecture => _settings.DefaultArchitecture;

    /// <summary>Предупреждение из окна настроек: диалоги живут в сервисе вьюмодели.</summary>
    public void ShowWarning(string message) => _dialog.ShowWarning(message);

    /// <summary>Сообщение из окна настроек.</summary>
    public void ShowInfo(string message) => _dialog.ShowInfo(message);

    /// <summary>Сообщение со своим заголовком окна.</summary>
    public void ShowInfo(string message, string title) => _dialog.ShowInfo(message, title);

    /// <summary>Сообщение об ошибке из окна настроек.</summary>
    public void ShowError(string message) => _dialog.ShowError(message);

    /// <summary>Запрос подтверждения из окна настроек.</summary>
    public bool Confirm(string message) => _dialog.Confirm(message);

    /// <summary>Диалог выбора файла для окна настроек.</summary>
    public string? PickFile(string title, string filter) => _dialog.OpenFileDialog(title, filter);
 
    /// <summary>Диалог сохранения файла для окна настроек.</summary>
    public string? PickSaveFile(string title, string defaultFileName) =>
        _dialog.SaveFileDialog(title, defaultFileName);
 
    /// <summary>
    /// «Экспорт в файл *.v8i…» (issue #29): показывает диалог сохранения и выгружает список
    /// баз и групп приложения в произвольный файл ibases.v8i. Единая точка входа для
    /// настроек (вкладка «Базы» → «Список баз») и меню «Утилиты» — оба места используют
    /// один и тот же диалог, сервис и сообщения о результате.
    /// </summary>
    public void ExportToIbasesFileInteractive()
    {
        var path = _dialog.SaveFileDialog(
            LocalizationManager.T("Settings.Bases.ExportV8iFileDialogTitle"),
            "ibases.v8i",
            $"{LocalizationManager.T("Settings.Ibases.FileFilter")}|*.v8i|{LocalizationManager.T("Common.AllFiles")}|*.*");
        if (string.IsNullOrWhiteSpace(path))
            return;
        ExportToIbasesFile(path);
    }
 
    /// <summary>
    /// «Импорт из файла *.v8i…» (issue #29): показывает диалог открытия и загружает список
    /// баз из произвольного файла ibases.v8i. Единая точка входа для настроек (вкладка
    /// «Базы» → «Список баз») и меню «Утилиты» — оба места используют один и тот же
    /// диалог, сервис и сообщения о результате.
    /// </summary>
    public void ImportFromIbasesFileInteractive()
    {
        var path = _dialog.OpenFileDialog(
            LocalizationManager.T("Settings.Bases.ImportV8iFileDialogTitle"),
            $"{LocalizationManager.T("Settings.Ibases.FileFilter")}|*.v8i|{LocalizationManager.T("Common.AllFiles")}|*.*");
        if (string.IsNullOrWhiteSpace(path))
            return;
        ImportFromIbasesFile(path);
    }

    /// <summary>Диалог выбора каталога для окна настроек.</summary>
    public string? PickFolder(string title, string? initialDirectory = null)
        => _dialog.OpenFolderDialog(title, initialDirectory);

    /// <summary>
    /// Применяет настройки вкладки «Платформы»: дополнительные пути поиска
    /// и разрядность по умолчанию.
    /// </summary>
    public void ApplyPlatformSettings(IEnumerable<string> additionalPaths, string architecture)
    {
        _settings.AdditionalPlatformSearchPaths = additionalPaths
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.DefaultArchitecture = NormalizeDefaultArchitecture(architecture);

        ApplyDefaultArchitecture();
        ApplyAdditionalSearchPaths();
        SaveSettingsSilently();
    }

    /// <summary>
    /// Отдаёт дополнительные пути поиска самой платформе: вкладка настроек
    /// передаёт их аргументом, а запуск читает статический список сервиса,
    /// и без этой передачи платформа из нестандартного каталога была видна
    /// в списке, но не находилась при запуске.
    /// </summary>
    private void ApplyAdditionalSearchPaths() =>
        PlatformVersionService.SetAdditionalSearchPaths(_settings.AdditionalPlatformSearchPaths);

    /// <summary>
    /// Убирает из списка файловые базы, у которых нет каталога или файла базы.
    /// Перед удалением показывается список того, что будет убрано.
    /// </summary>
    public void RemoveMissingFileBases()
    {
        var states = _allInfobases
            .Select(ib => (Infobase: ib, State: InfobaseMaintenanceService.GetFileBaseState(ib)))
            .ToList();

        var missing = states
            .Where(x => x.State == InfobaseMaintenanceService.FileBaseState.Missing)
            .Select(x => x.Infobase)
            .ToList();

        // Базы с недоступного диска в удаление не идут: «проверить не удалось»
        // это не «нет». О них сказано отдельно.
        var unknown = states.Count(x => x.State == InfobaseMaintenanceService.FileBaseState.Unknown);

        if (missing.Count == 0)
        {
            _dialog.ShowInfo(
                unknown == 0
                    ? LocalizationManager.T("Main.MissingNone")
                    : string.Format(LocalizationManager.T("Main.MissingOnlyUnchecked"), unknown),
                LocalizationManager.T("Main.CheckFileBasesTitle"));
            return;
        }

        var preview = string.Join("\n", missing.Take(15).Select(ib => "• " + ib.Name));
        if (missing.Count > 15)
            preview += string.Format(LocalizationManager.T("Main.MissingMore"), missing.Count - 15);
        if (unknown > 0)
            preview += "\n\n" + string.Format(LocalizationManager.T("Main.MissingUnchecked"), unknown);

        if (!_dialog.Confirm(
                string.Format(LocalizationManager.T("Main.MissingConfirm"), missing.Count, preview),
                LocalizationManager.T("Main.RemoveMissingTitle")))
            return;

        // Сначала запись, потом замена списка в памяти: при ошибке диска
        // пользователь остался бы с урезанным списком в окне и полным на диске,
        // а следующее сохранение записало бы урезанный поверх.
        var removing = new HashSet<Infobase>(missing, ReferenceEqualityComparer.Instance as IEqualityComparer<Infobase>);
        var remaining = _allInfobases.Where(ib => !removing.Contains(ib)).ToList();
        if (!SaveList(remaining))
        {
            _dialog.ShowError(LocalizationManager.T("Main.SaveFailedHint"),
                LocalizationManager.T("Main.RemoveMissingTitle"));
            return;
        }

        _allInfobases.Clear();
        _allInfobases.AddRange(remaining);
        // Состав списка изменился: слоты избранного пересчитываются, иначе
        // номер остаётся у удалённой базы, а её слот занят навсегда.
        SyncFavoriteHotkeys();

        if (SelectedInfobase is { } selected && !_allInfobases.Contains(selected))
            SelectedInfobase = null;

        RebuildTree();
        _logger.Info($"Удалено отсутствующих файловых баз: {missing.Count}");

        _dialog.ShowInfo(
            string.Format(LocalizationManager.T("Main.MissingRemoved"), missing.Count),
            LocalizationManager.T("Main.RemoveMissingTitle"));
    }

    /// <summary>Завершает запущенные процессы платформы 1С.</summary>
    public void KillOneCProcesses()
    {
        // Один снимок на вопрос и на действие: между ними процессы приходят
        // и уходят, и завершать пришлось бы не то, что показано.
        var snapshot = InfobaseMaintenanceService.SnapshotOneCProcesses();
        if (snapshot.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("Main.NoProcesses"),
                LocalizationManager.T("Main.OneCProcessesTitle"));
            return;
        }

        // Разбивка и предупреждение идут перед вопросом: ключ подтверждения
        // общий с версией для Windows и заканчивается словом «Продолжить?»,
        // поэтому дописывать предупреждение после него нельзя.
        var breakdown = InfobaseMaintenanceService.DescribeProcesses(snapshot);
        var details = string.Format(LocalizationManager.T("Main.KillProcessesBreakdown"),
            string.Join(", ", breakdown.Select(p => $"{p.Name}: {p.Count}")));

        // Про остановку кластера предупреждаем только когда серверные процессы
        // действительно в списке.
        if (breakdown.Any(p => ServerProcessNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)))
            details += " " + LocalizationManager.T("Main.KillProcessesServerNote");

        var question = details + "\n\n"
            + string.Format(LocalizationManager.T("Main.KillProcessesConfirm"), snapshot.Count);

        if (!_dialog.Confirm(question, LocalizationManager.T("Main.KillProcessesTitle")))
            return;

        var (killed, failed) = InfobaseMaintenanceService.KillOneCProcesses(snapshot);
        _logger.Info($"Завершено процессов 1С: {killed}, не удалось: {failed}");

        var message = string.Format(LocalizationManager.T("Main.ProcessesKilled"), killed);
        if (failed > 0)
            message += "\n" + string.Format(LocalizationManager.T("Main.ProcessesKillFailed"), failed);

        _dialog.ShowInfo(message, LocalizationManager.T("Main.OneCProcessesTitle"));
    }

    /// <summary>Очищает список баз и групп целиком. Сами базы на диске не трогает.</summary>
    public void ClearAllInfobases()
    {
        if (_allInfobases.Count == 0 && _groups.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("Main.ClearAllAlreadyEmpty"),
                LocalizationManager.T("Main.ClearAllTitle"));
            return;
        }

        if (!_dialog.Confirm(
                string.Format(LocalizationManager.T("Main.ClearAllConfirm"), _allInfobases.Count, _groups.Count),
                LocalizationManager.T("Main.ClearAllTitle")))
            return;

        // Пустые списки пишутся на диск до того, как очищается память:
        // иначе при отказе записи в окне пусто, а на диске прежнее, и первое
        // же следующее сохранение затирает уцелевшее. Файла два, поэтому при
        // отказе на втором первый возвращается обратно: иначе на диске
        // оставался бы пустой список баз при живых группах.
        var previousInfobases = _allInfobases.ToList();
        if (!SaveList(new List<Infobase>()))
        {
            _dialog.ShowError(LocalizationManager.T("Main.SaveFailedHint"),
                LocalizationManager.T("Main.ClearAllTitle"));
            return;
        }

        if (!SaveGroupList(new List<Group>()))
        {
            // Список баз уже записан пустым, возвращаем прежний. Если и это
            // не удалось, на диске пусто, а в памяти нет: чтобы состояния
            // сошлись, память тоже очищается, и об этом сказано отдельно.
            if (!SaveList(previousInfobases))
            {
                _allInfobases.Clear();
                // Состав списка изменился: слоты избранного пересчитываются, иначе
                // номер остаётся у удалённой базы, а её слот занят навсегда.
                SyncFavoriteHotkeys();
                _groups.Clear();
                SelectedInfobase = null;
                RebuildTree();
                _dialog.ShowError(LocalizationManager.T("Main.ClearAllPartial"),
                    LocalizationManager.T("Main.ClearAllTitle"));
                return;
            }

            _dialog.ShowError(LocalizationManager.T("Main.SaveFailedHint"),
                LocalizationManager.T("Main.ClearAllTitle"));
            return;
        }

        _allInfobases.Clear();
        // Состав списка изменился: слоты избранного пересчитываются, иначе
        // номер остаётся у удалённой базы, а её слот занят навсегда.
        SyncFavoriteHotkeys();
        _groups.Clear();
        SelectedInfobase = null;

        // Выгрузка в ibases.v8i намеренно не вызывается, как и в версии
        // для Windows: экспорт убирает из файла записи, которых нет
        // в приложении, и очистка списка вынесла бы пусковой список платформы.
        RebuildTree();
        _logger.Info("Список баз и групп очищен");

        _dialog.ShowInfo(LocalizationManager.T("Main.ClearAllDone"),
            LocalizationManager.T("Main.ClearAllTitle"));
    }

    /// <summary>Добавлять ли метку времени к имени файла выгрузки.</summary>
    public bool AddTimestampToExportFileName => _settings.AddTimestampToExportFileName;

    /// <summary>Формат метки времени в имени файла выгрузки.</summary>
    public string ExportTimestampFormat => _settings.ExportTimestampFormat;

    /// <summary>Применяет настройки имени файла выгрузки со вкладки «Базы».</summary>
    public void ApplyExportFileNameSettings(bool addTimestamp, string timestampFormat)
    {
        _settings.AddTimestampToExportFileName = addTimestamp;
        _settings.ExportTimestampFormat = string.IsNullOrWhiteSpace(timestampFormat)
            ? "yyyyMMdd_HHmmss"
            : timestampFormat.Trim();
        SaveSettingsSilently();
    }

    /// <summary>Выгружает список баз и групп в JSON-файл.</summary>
    public void ExportInfobases(bool addTimestamp, string timestampFormat)
    {
        if (_allInfobases.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("Main.ExportEmpty"),
                LocalizationManager.T("Main.ExportBasesTitle"));
            return;
        }

        var path = _dialog.SaveFileDialog(
            LocalizationManager.T("Main.ExportBasesDialogTitle"),
            BuildExportFileName("infobases_export", ".json", addTimestamp, timestampFormat),
            LocalizationManager.T("Main.JsonFileFilter"));
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var json = JsonSerializer.Serialize(
                new InfobaseExportData
                {
                    Infobases = _allInfobases.ToList(),
                    Groups = _groups.ToList()
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    // Кириллицу и прочие не-ASCII символы пишем читаемыми UTF-8,
                    // а не \uXXXX-последовательностями (issue #170).
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            File.WriteAllText(path, json);

            _dialog.ShowInfo(
                string.Format(LocalizationManager.T("Main.ExportDone"), _allInfobases.Count, _groups.Count, path),
                LocalizationManager.T("Main.ExportBasesTitle"));
            _logger.Info($"Список баз выгружен в {path}");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка выгрузки списка баз", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrExportFailed"), ex.Message),
                LocalizationManager.T("Main.ExportErrorTitle"));
        }
    }

    /// <summary>
    /// Экспортирует ВСЕ базы, которые пользователь видит сейчас (с учётом фильтра
    /// приватных баз, режима списка, поиска и тегов — тот же набор, что в дереве),
    /// в CSV-файл, открываемый в Excel (0.3.9.91).
    /// </summary>
    public void ExportBasesCsv()
    {
        // Тот же набор, что в ApplyFilter: MatchesFilter учитывает приватность,
        // режим списка («Избранное»/«Недавние»), поиск и отбор по тегам.
        var visible = _allInfobases.Where(MatchesFilter).ToList();
        if (visible.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("Main.ExportEmpty"),
                LocalizationManager.T("ExportCsv.Title"));
            return;
        }

        var path = _dialog.SaveFileDialog(
            LocalizationManager.T("ExportCsv.Title"),
            $"Bases_{DateTime.Now:yyyy-MM-dd}.csv",
            LocalizationManager.T("ExportCsv.FileFilter"));
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            CsvExporter.WriteFile(path, BuildCsvRows(visible));

            _dialog.ShowInfo(
                string.Format(LocalizationManager.T("ExportCsv.Success"), visible.Count, path),
                LocalizationManager.T("ExportCsv.Title"));
            _logger.Info($"Список баз ({visible.Count}) выгружен в CSV: {path}");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка экспорта списка баз в CSV", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("ExportCsv.Error"), ex.Message),
                LocalizationManager.T("Main.ExportErrorTitle"));
        }
    }

    /// <summary>
    /// Экспортирует видимые базы (тот же набор, что в CSV-экспорте: приватные базы
    /// заблокированного профиля скрыты, режимы «Избранное»/«Недавние», поиск и теги
    /// учтены) в самодостаточный HTML-отчёт (0.3.9.131): шапка с датой/временем и
    /// профилем, сводка по последним известным данным, таблицы по группам с подсветкой
    /// проблемных баз по критериям Центра обслуживания. Стили встроены — файл открывается
    /// в любом браузере и пригоден для печати/рассылки.
    /// </summary>
    public void ExportBasesHtml()
    {
        var visible = _allInfobases.Where(MatchesFilter).ToList();
        if (visible.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("Main.ExportEmpty"),
                LocalizationManager.T("ExportHtml.Title"));
            return;
        }

        var path = _dialog.SaveFileDialog(
            LocalizationManager.T("ExportHtml.Title"),
            $"Bases_{DateTime.Now:yyyy-MM-dd}.html",
            LocalizationManager.T("ExportHtml.FileFilter"));
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            HtmlReportExporter.WriteFile(path, BuildHtmlReportData(visible));

            _dialog.ShowInfo(
                string.Format(LocalizationManager.T("ExportHtml.Success"), visible.Count, path),
                LocalizationManager.T("ExportHtml.Title"));
            _logger.Info($"HTML-отчёт по базам ({visible.Count}) сформирован: {path}");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка формирования HTML-отчёта по базам", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("ExportHtml.Error"), ex.Message),
                LocalizationManager.T("Main.ExportErrorTitle"));
        }
    }

    /// <summary>
    /// Экспортирует ВСЁ состояние списка баз в JSON (0.3.9.122): базы со всеми полями
    /// (строка подключения, имя, группа, теги, закладка 1–9, закрепление, приватность,
    /// внешняя обработка, скрипты pre/post запуска, раздельные учётные данные,
    /// параметры запуска, порядок сортировки) и иерархию групп целиком. В отличие
    /// от CSV-экспорта переносит и избранное, и закрепление. Приватные базы
    /// включаются только при разблокированном профиле (иначе скрыты, как в CSV).
    /// </summary>
    public void ExportBasesJson()
    {
        // Приватные базы заблокированного профиля в выгрузку не попадают (как в CSV).
        var exportable = _allInfobases.Where(IsVisibleForPrivateFilter).ToList();
        if (exportable.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("Main.ExportEmpty"),
                LocalizationManager.T("ExportJson.Title"));
            return;
        }

        var path = _dialog.SaveFileDialog(
            LocalizationManager.T("ExportJson.Title"),
            BuildExportFileName("infobases_full_export", ".json",
                _settings.AddTimestampToExportFileName, _settings.ExportTimestampFormat),
            LocalizationManager.T("Main.JsonFileFilter"));
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            // Заметки баз (п.8, 1.0.0.90): в снимок входят markdown-карточки и имена
            // секретов (значения зашифрованы ключом текущей машины и не переносятся).
            var snapshot = InfobaseJsonTransfer.BuildSnapshot(exportable, _groups, GetOrCreateNotesStore().GetAll());
            var json = InfobaseJsonTransfer.Serialize(snapshot);
            File.WriteAllText(path, json);

            _dialog.ShowInfo(
                string.Format(LocalizationManager.T("ExportJson.Success"),
                    snapshot.Infobases.Count, snapshot.Groups.Count, path),
                LocalizationManager.T("ExportJson.Title"));
            _logger.Info($"Список баз ({snapshot.Infobases.Count}) и групп ({snapshot.Groups.Count}) " +
                         $"выгружен в JSON: {path}");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка экспорта списка баз в JSON", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("ExportJson.Error"), ex.Message),
                LocalizationManager.T("Main.ExportErrorTitle"));
        }
    }

    /// <summary>
    /// Импорт списка баз из JSON с режимом «добавить» (0.3.9.122): дубликаты по строке
    /// подключения пропускаются, новые базы/группы/теги добавляются. Перед импортом
    /// показывается сводка (сколько баз/групп/тегов будет добавлено, сколько пропущено
    /// дубликатов) с подтверждением.
    /// </summary>
    public void ImportBasesJson()
    {
        var path = _dialog.OpenFileDialog(
            LocalizationManager.T("ImportJson.Title"),
            LocalizationManager.T("Main.JsonFileFilter"));
        if (string.IsNullOrWhiteSpace(path))
            return;

        InfobaseListSnapshot? snapshot;
        try
        {
            var json = File.ReadAllText(path);
            snapshot = InfobaseJsonTransfer.Deserialize(json);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка чтения файла импорта списка баз (JSON)", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("ImportJson.Error"), ex.Message),
                LocalizationManager.T("Main.ImportErrorTitle"));
            return;
        }

        if (snapshot is null || snapshot.Infobases.Count == 0)
        {
            _dialog.ShowWarning(LocalizationManager.T("ImportJson.NoBases"),
                LocalizationManager.T("ImportJson.Title"));
            return;
        }

        // Сводка перед импортом: сколько добавится, сколько пропустится.
        var plan = InfobaseJsonTransfer.PlanImport(
            _allInfobases,
            _groups,
            _allInfobases.SelectMany(i => i.Tags),
            snapshot);

        if (plan.BasesToAdd.Count == 0 && plan.GroupsToAdd.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("ImportJson.NothingNew"),
                LocalizationManager.T("ImportJson.Title"));
            return;
        }

        if (!_dialog.Confirm(
                string.Format(LocalizationManager.T("ImportJson.Confirm"),
                    plan.BasesToAdd.Count, plan.GroupsToAdd.Count, plan.TagsToAdd.Count,
                    plan.DuplicatesSkipped),
                LocalizationManager.T("ImportJson.Title")))
            return;

        _allInfobases.AddRange(plan.BasesToAdd);
        SyncFavoriteHotkeys();

        // Группы из файла, которых ещё нет (по имени), добавляются вместе с иерархией
        // ParentId; конфликтующие Id переименовываются без потери связей.
        var mergedGroups = InfobaseJsonTransfer.MergeNewGroups(_groups, plan.GroupsToAdd);
        if (mergedGroups.Count > 0)
            _groups.AddRange(mergedGroups);

        // Заметки баз (п.8, 1.0.0.90): переносятся markdown-карточки (значения секретов
        // зашифрованы на исходной машине и не переносятся). Локальная заметка с тем же
        // ключом базы остаётся нетронутой.
        try
        {
            GetOrCreateNotesStore().ImportTransferred(snapshot.Notes);
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка импорта заметок баз (JSON)", ex);
        }

        SelectedInfobase = null;
        var saved = SaveSilently();
        saved &= SaveGroupsSilently();
        RebuildTree();

        if (!saved)
        {
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrLoadFailed"),
                    LocalizationManager.T("Main.SaveFailedHint")),
                LocalizationManager.T("Main.LoadErrorTitle"));
            return;
        }

        _dialog.ShowInfo(
            string.Format(LocalizationManager.T("ImportJson.Done"),
                plan.BasesToAdd.Count, plan.GroupsToAdd.Count, plan.TagsToAdd.Count,
                plan.DuplicatesSkipped),
            LocalizationManager.T("ImportJson.Title"));
        _logger.Info($"Импорт списка баз из JSON: добавлено {plan.BasesToAdd.Count} баз, " +
                     $"{plan.GroupsToAdd.Count} групп, {plan.TagsToAdd.Count} тегов, " +
                     $"пропущено дубликатов {plan.DuplicatesSkipped}");
    }

    /// <summary>
    /// Строки CSV-документа: локализованный заголовок + по строке на видимую базу.
    /// Сборка вынесена в <see cref="Services.InfobaseCsvExportService"/> (v100113) —
    /// единая для WPF/Avalonia и юнит-тестируемая; добавлены колонки «Версия платформы»
    /// и «Дата последней копии».
    /// </summary>
    private static List<IReadOnlyList<string?>> BuildCsvRows(IEnumerable<Infobase> bases)
        => new(Services.InfobaseCsvExportService.BuildRows(bases));

    /// <summary>Дата изменений файла ИБ в формате ГГГГ-ММ-ДД ЧЧ:ММ (локальное время) или пусто.</summary>
    private static string FormatCsvModified(Infobase ib) =>
        ib.FileLastWriteTimeUtc.HasValue
            ? ib.FileLastWriteTimeUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : string.Empty;

    /// <summary>Размер ИБ в удобочитаемом виде, если известен; иначе пусто.</summary>
    private static string FormatCsvSize(Infobase ib)
    {
        if (ib.ManualSizeBytes.HasValue)
            return Infobase.FormatSize(ib.ManualSizeBytes.Value);
        return ib.FileSizeBytes.HasValue ? Infobase.FormatSize(ib.FileSizeBytes.Value) : string.Empty;
    }

    // ======================= Выборочный экспорт/импорт баз =======================

    private System.Windows.Input.ICommand? _exportSelectedInfobasesCommand;

    /// <summary>Команда «Экспорт выбранных баз…»: окно-чеклист + сохранение в JSON.</summary>
    public System.Windows.Input.ICommand ExportSelectedInfobasesCommand =>
        _exportSelectedInfobasesCommand ??= new RelayCommand(_ => ExecuteExportSelectedInfobases(),
            _ => _allInfobases.Count > 0);

    private void ExecuteExportSelectedInfobases()
    {
        if (_allInfobases.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("Main.ExportEmpty"),
                LocalizationManager.T("Main.ExportBasesTitle"));
            return;
        }

        var items = _allInfobases
            .Select(b => new BaseSelectionItem(b.Name, b.GroupDisplay) { Tag = b })
            .ToList();
        var selector = new Workspace1C.BaseSelectionWindow(
            LocalizationManager.T("ExportSelect.Title"),
            LocalizationManager.T("ExportSelect.ExportHint"),
            items);
        if (!selector.ShowDialogSync(OwnerWindow()))
            return;

        var selected = selector.GetSelected().Select(i => (Infobase)i.Tag!).ToList();
        if (selected.Count == 0)
            return;

        var path = _dialog.SaveFileDialog(
            LocalizationManager.T("Main.ExportBasesDialogTitle"),
            BuildExportFileName("infobases_selected", ".json", _settings.AddTimestampToExportFileName, _settings.ExportTimestampFormat),
            LocalizationManager.T("Main.JsonFileFilter"));
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var json = JsonSerializer.Serialize(
                new InfobaseExportData
                {
                    Version = 1,
                    Infobases = selected,
                    Groups = _groups.ToList()
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            File.WriteAllText(path, json);

            _dialog.ShowInfo(
                string.Format(LocalizationManager.T("ExportSelect.ExportDone"), selected.Count, path),
                LocalizationManager.T("Main.ExportBasesTitle"));
            _logger.Info($"Выбранные базы ({selected.Count}) выгружены в {path}");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка выгрузки выбранных баз", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrExportFailed"), ex.Message),
                LocalizationManager.T("Main.ExportErrorTitle"));
        }
    }

    private System.Windows.Input.ICommand? _importMergeInfobasesCommand;

    /// <summary>Команда «Импорт баз (добавлением)…»: выбор файла, чеклист, слияние по Id.</summary>
    public System.Windows.Input.ICommand ImportMergeInfobasesCommand =>
        _importMergeInfobasesCommand ??= new RelayCommand(_ => ExecuteImportMergeInfobases());

    private void ExecuteImportMergeInfobases()
    {
        var path = _dialog.OpenFileDialog(
            LocalizationManager.T("Main.ImportBasesDialogTitle"),
            LocalizationManager.T("Main.JsonFileFilter"));
        if (string.IsNullOrWhiteSpace(path))
            return;

        List<Infobase> loaded;
        List<Group> loadedGroups;
        try
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            InfobaseExportData? exportData = null;
            try { exportData = JsonSerializer.Deserialize<InfobaseExportData>(json, options); }
            catch (JsonException) { }

            if (exportData is not null && exportData.Infobases.Count > 0)
            {
                loaded = exportData.Infobases;
                loadedGroups = exportData.Groups;
            }
            else
            {
                loaded = JsonSerializer.Deserialize<List<Infobase>>(json, options) ?? new List<Infobase>();
                loadedGroups = new List<Group>();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка чтения файла импорта", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrLoadFailed"), ex.Message),
                LocalizationManager.T("Main.LoadErrorTitle"));
            return;
        }

        if (loaded.Count == 0)
        {
            _dialog.ShowWarning(LocalizationManager.T("Main.ImportNoBases"),
                LocalizationManager.T("Main.LoadBasesTitle"));
            return;
        }

        // Уже существующие базы снимаются с флажков: импорт их пропустит.
        var existingKeys = new HashSet<string>(
            _allInfobases.Select(InfobaseExportMerge.DuplicateKey), StringComparer.OrdinalIgnoreCase);
        var items = loaded
            .Select(b =>
            {
                var duplicate = existingKeys.Contains(InfobaseExportMerge.DuplicateKey(b));
                var subtitle = duplicate
                    ? LocalizationManager.T("ExportSelect.AlreadyExists")
                    : b.GroupDisplay;
                return new BaseSelectionItem(b.Name, subtitle, !duplicate) { Tag = b };
            })
            .ToList();

        var selector = new Workspace1C.BaseSelectionWindow(
            LocalizationManager.T("ExportSelect.ImportTitle"),
            LocalizationManager.T("ExportSelect.ImportHint"),
            items);
        if (!selector.ShowDialogSync(OwnerWindow()))
            return;

        var chosen = selector.GetSelected().Select(i => (Infobase)i.Tag!).ToList();
        if (chosen.Count == 0)
            return;

        var merge = InfobaseExportMerge.SelectNew(_allInfobases, chosen);

        _allInfobases.AddRange(merge.Added);
        SyncFavoriteHotkeys();

        // Группы из файла, которых ещё нет (по имени), добавляются целиком.
        var existingGroupNames = new HashSet<string>(
            _groups.Select(g => (g.Name ?? "").Trim()), StringComparer.OrdinalIgnoreCase);
        var newGroups = loadedGroups
            .Where(g => !string.IsNullOrWhiteSpace(g.Name) && existingGroupNames.Add(g.Name.Trim()))
            .ToList();
        if (newGroups.Count > 0)
            _groups.AddRange(newGroups);

        SelectedInfobase = null;
        var saved = SaveSilently();
        saved &= SaveGroupsSilently();
        RebuildTree();

        if (!saved)
        {
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrLoadFailed"),
                    LocalizationManager.T("Main.SaveFailedHint")),
                LocalizationManager.T("Main.LoadErrorTitle"));
            return;
        }

        _dialog.ShowInfo(
            string.Format(LocalizationManager.T("ExportSelect.ImportDone"),
                merge.Added.Count, chosen.Count - merge.Added.Count),
            LocalizationManager.T("Main.LoadBasesTitle"));
        _logger.Info($"Импорт добавлением: добавлено {merge.Added.Count}, пропущено {chosen.Count - merge.Added.Count}");
    }

    /// <summary>
    /// Загружает список баз и групп из JSON-файла, заменяя текущий.
    /// Понимает и старый формат, где в файле лежит только список баз.
    /// </summary>
    public void ImportInfobases()
    {
        var path = _dialog.OpenFileDialog(
            LocalizationManager.T("Main.ImportBasesDialogTitle"),
            LocalizationManager.T("Main.JsonFileFilter"));
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            InfobaseExportData? exportData = null;
            try { exportData = JsonSerializer.Deserialize<InfobaseExportData>(json, options); }
            catch (JsonException) { }

            List<Infobase> loaded;
            List<Group> loadedGroups;
            if (exportData is not null && exportData.Infobases.Count > 0)
            {
                loaded = exportData.Infobases;
                loadedGroups = exportData.Groups;
            }
            else
            {
                loaded = JsonSerializer.Deserialize<List<Infobase>>(json, options) ?? new List<Infobase>();
                loadedGroups = new List<Group>();
            }

            if (loaded.Count == 0)
            {
                _dialog.ShowWarning(LocalizationManager.T("Main.ImportNoBases"),
                    LocalizationManager.T("Main.LoadBasesTitle"));
                return;
            }

            if (!_dialog.Confirm(
                    string.Format(LocalizationManager.T("Main.ImportConfirm"), loaded.Count, loadedGroups.Count),
                    LocalizationManager.T("Main.LoadBasesTitle")))
                return;

            _allInfobases.Clear();
            _allInfobases.AddRange(loaded);
            // Состав списка изменился: слоты избранного пересчитываются, иначе
            // номер остаётся у удалённой базы, а её слот занят навсегда.
            SyncFavoriteHotkeys();
            _groups.Clear();
            _groups.AddRange(loadedGroups);
            SelectedInfobase = null;

            var saved = SaveSilently();
            saved &= SaveGroupsSilently();
            RebuildTree();

            if (!saved)
            {
                // Список в памяти уже заменён, а на диске осталось прежнее
                // состояние: сообщать об успехе нельзя.
                _dialog.ShowError(
                    string.Format(LocalizationManager.T("Main.ErrLoadFailed"),
                        LocalizationManager.T("Main.SaveFailedHint")),
                    LocalizationManager.T("Main.LoadErrorTitle"));
                return;
            }

            _dialog.ShowInfo(
                string.Format(LocalizationManager.T("Main.ImportDoneMsg"), loaded.Count, loadedGroups.Count),
                LocalizationManager.T("Main.LoadBasesTitle"));
            _logger.Info($"Список баз загружен из {path}");
        }
        catch (Exception ex)
        {
            _logger.Error("Ошибка загрузки списка баз", ex);
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrLoadFailed"), ex.Message),
                LocalizationManager.T("Main.LoadErrorTitle"));
        }
    }

    /// <summary>Имя файла выгрузки с меткой времени, если она включена.</summary>
    private static string BuildExportFileName(string baseName, string extension, bool addTimestamp, string timestampFormat)
    {
        if (!addTimestamp)
            return $"{baseName}{extension}";

        var format = string.IsNullOrWhiteSpace(timestampFormat) ? "yyyyMMdd_HHmmss" : timestampFormat;
        try
        {
            return $"{baseName}_{DateTime.Now.ToString(format)}{extension}";
        }
        catch (FormatException)
        {
            // Шаблон мог прийти из файла настроек, правленного руками.
            return $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
        }
    }

    /// <summary>
    /// Процессы, завершение которых бьёт не по клиентскому сеансу: кластер
    /// серверов, сервер отладки, сервер данных и утилиты, держащие базу.
    /// </summary>
    private static readonly string[] ServerProcessNames =
    {
        "ragent", "rmngr", "rphost", "ras", "rac", "dbgs", "dbda", "ibsrv", "ibcmd", "crserver"
    };

    /// <summary>
    /// Сохраняет список баз и перестраивает дерево после точечного изменения отдельных баз
    /// (например, определения конфигураций всех баз, issue #236). Аналог
    /// <see cref="MainViewModel.PersistInfobasesAfterInlineEdit"/> для Windows.
    /// </summary>
    public void PersistInfobasesAfterInlineEdit()
    {
        SaveSilently();
        RebuildTree();
    }

    /// <summary>
    /// Добавляет найденную на диске базу в список приложения (issue #247, диалог
    /// «Поиск потерянных и забытых баз 1С»). Только добавляет в коллекцию; сохранение
    /// и перестроение дерева выполняет вызывающий код через
    /// <see cref="PersistInfobasesAfterInlineEdit"/> после закрытия диалога, если были
    /// добавления.
    /// </summary>
    public void AddFoundInfobase(Infobase infobase)
    {
        _allInfobases.Add(infobase);
        OnPropertyChanged(nameof(Infobases));
    }

    /// <summary>Каталоги шаблонов конфигураций, заданные пользователем.</summary>
    public IReadOnlyList<string> TemplateCatalogPaths => _settings.TemplateCatalogPaths;

    /// <summary>
    /// Применяет настройки каталогов шаблонов со вкладки «Базы». Поиск шаблонов
    /// читает статический список сервиса, поэтому без этой передачи заданные
    /// каталоги оставались только в файле настроек.
    /// </summary>
    public void ApplyTemplateCatalogPaths(IEnumerable<string> paths)
    {
        _settings.TemplateCatalogPaths = (paths ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        ApplyTemplateCatalogPaths();
        SaveSettingsSilently();
    }

    private void ApplyTemplateCatalogPaths() =>
        OneCTemplateService.SetUserTemplatePaths(_settings.TemplateCatalogPaths);

    /// <summary>
    /// Каталоги шаблонов, известные самой платформе: из её настроек и
    /// стандартный tmplts. Кнопка «Из 1С» на вкладке «Базы» заполняет
    /// список ими.
    /// </summary>
    public List<string> DiscoverTemplateCatalogPaths()
    {
        var found = new List<string>();
        try
        {
            var configured = OneCTemplateService.GetConfiguredOrDefaultTemplatePath();
            if (!string.IsNullOrWhiteSpace(configured))
                found.Add(configured);
            found.AddRange(OneCTemplateService.GetTemplateRootFolders());
        }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось прочитать каталоги шаблонов: {ex.Message}");
        }

        return found
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Передаёт режим «Разрядности по умолчанию» лаунчеру платформы. Без этого
    /// настройка в Linux-ветке не действовала: запуск всегда считал её x64.
    /// Строка "Priority" («Использовать приоритет базы») передаётся как есть —
    /// разрешение разрядности происходит в лаунчере.
    /// </summary>
    private void ApplyDefaultArchitecture() =>
        OneCLauncher.DefaultArchitectureMode = NormalizeDefaultArchitecture(_settings.DefaultArchitecture);

    /// <summary>Нормализует строку режима «Разрядности по умолчанию» (X86 / X64 / Priority).</summary>
    private static string NormalizeDefaultArchitecture(string? value)
    {
        if (string.Equals(value, "X86", StringComparison.OrdinalIgnoreCase))
            return "X86";
        if (string.Equals(value, "Priority", StringComparison.OrdinalIgnoreCase))
            return "Priority";
        return "X64";
    }

    /// <summary>
    /// Смена версии платформы у базы двойным щелчком по колонке версии.
    /// Разбирает вариант вида «8.3.27.1234 (64)»: разрядность уходит в своё
    /// поле, а в версии остаётся чистый номер, как в версии для Windows
    /// (MainWindow.Events.cs, OpenPlatformVersionPicker).
    /// </summary>
    public void PickPlatformVersionFor(Infobase? infobase)
    {
        if (infobase is null)
            return;

        SelectedInfobase = infobase;
        var dialog = new PlatformVersionPickerWindow(FindInstalledPlatformVersions(), infobase.PlatformVersion);
        if (!dialog.ShowDialogSync(OwnerWindow()))
            return;

        var selected = dialog.Result?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selected))
            return;

        PlatformVersionService.ParseVariant(selected, out var cleanVersion, out var arch);
        var newVersion = string.IsNullOrWhiteSpace(cleanVersion) ? selected : cleanVersion;
        var versionChanged = !string.Equals(infobase.PlatformVersion, newVersion, StringComparison.Ordinal);
        // Раньше здесь был ранний return при совпадении версии — из-за этого
        // нельзя было сменить разрядность (х86 → х64) одной и той же версии (issue #146).
        if (versionChanged)
            infobase.PlatformVersion = newVersion;
        // Разрядность записываем, только если она задана в выбранном варианте ЯВНО суффиксом
        // «(32)/(64)». ParseVariant по умолчанию возвращает разрядность даже без суффикса,
        // поэтому выбор папки/частичной версии («8.3», «8.3.27») не должен подставлять x86 —
        // разрешение разрядности остаётся за лаунчером (issue #251).
        if ((arch is "32" or "64") && PlatformVersionService.HasExplicitArchitecture(selected))
            infobase.Architecture = arch;
        if (versionChanged || arch is "32" or "64")
        {
            SaveSilently();
            RebuildTree();
        }
    }

    /// <summary>
    /// Список установленных версий платформы 1С (issue #54): публичное свойство для окон
    /// (в WPF-сборке аналогичное свойство в MainViewModel.cs). Linux: живой запрос к
    /// сервису поиска <see cref="FindInstalledPlatformVersions"/>, как было до переименования
    /// метода (все прежние вызовы метода сохранены).
    /// </summary>
    public IReadOnlyList<string> InstalledPlatformVersions => FindInstalledPlatformVersions();

    private List<string> FindInstalledPlatformVersions()
    {
        try { return _platformService.FindInstalledVersions(_settings.AdditionalPlatformSearchPaths); }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось получить список версий платформы: {ex.Message}");
            return new List<string>();
        }
    }

    private void DeleteInfobase()
    {
        // Выбран узел группы: удаляется группа, как в WPF-версии.
        if (SelectedGroupNode?.Group is Group group)
        {
            DeleteGroup(group);
            return;
        }

        var ib = SelectedInfobase;
        if (ib is null)
            return;

        var dialog = new Workspace1C.DeleteInfobaseWindow(ib);
        if (!dialog.ShowDialogSync(OwnerWindow()) || !dialog.Confirmed)
            return;

        if (dialog.DeletePhysically)
        {
            var error = InfobaseMaintenanceService.TryDeleteFileBasePhysically(ib);
            if (error is not null)
            {
                _dialog.ShowError(error);
                // Даже при ошибке на диске из списка удаляем, если пользователь подтвердит.
                if (!_dialog.Confirm(LocalizationManager.T("Main.ConfirmDeleteFromList")))
                    return;
            }
        }

        _allInfobases.Remove(ib);
        // Состав списка изменился: слоты избранного пересчитываются, иначе
        // номер остаётся у удалённой базы, а её слот занят навсегда.
        SyncFavoriteHotkeys();
        SaveSilently();
        RebuildTree();
        SelectedInfobase = null;
        ExportToIbasesAfterLocalChange();
    }

    private void CopyConnectionString()
    {
        if (SelectedInfobase is not Infobase ib)
            return;

        var text = ib.ConnectionStringDisplay;
        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime
                as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var clipboard = lifetime?.MainWindow?.Clipboard;
            if (clipboard is null)
            {
                _logger.Warn("Буфер обмена недоступен (нет активного окна).");
                return;
            }
            _ = clipboard.SetTextAsync(text);
            _logger.Info($"Скопирована строка подключения базы «{ib.Name}»");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Не удалось скопировать строку подключения: {ex.Message}");
        }
    }

    private void OpenSettings()
    {
        // Построение окна настроек — большая процедура (восемь вкладок, иконки, темы).
        // На Linux сбой в ней завершал процесс abort-ом (issue #168); ловим и логируем,
        // чтобы приложение продолжало работать, а ошибка попала в журнал.
        Workspace1C.SettingsWindow settings;
        try
        {
            settings = new Workspace1C.SettingsWindow(this);
        }
        catch (Exception ex)
        {
            _logger.Error($"Не удалось открыть окно настроек: {ex.Message}", ex);
            return;
        }

        // Владелец берётся только видимый: окно, спрятанное в трей, остаётся
        // в списке окон приложения, и показ поверх него ничего не показывает.
        // Настройки открываются из меню трея именно в таком состоянии.
        if (OwnerWindow() is { } owner)
            settings.ShowDialog(owner);
        else
            settings.Show();

        // После «ОК» окно настроек могло изменить «Дополнение заголовка программы»
        // (issue #28) — пересобираем заголовок главного окна.
        if (OwnerWindow() is Workspace1C.MainWindow main)
        {
            main.RefreshWindowTitle();
            // Правка структуры меню трея (issue #62): настройка TrayMenuEditingEnabled
            // и файл компоновки могли измениться — меню трея пересобирается сразу
            // (по отпечатку компоновки RefreshTrayMenu отсеет пустые пересборки).
            main.RefreshTrayMenuAfterSettings();
        }
    }

    /// <summary>
    /// Проверяет доступность всех баз 1С и помечает недоступные красной иконкой
    /// типа базы в списке (оригинальная иконка подключения в красном цвете,
    /// issue #289). Ручная команда верхней панели команд вместо автопроверки при
    /// запуске: старт не блокируется запросами ко всем базам. Доступность
    /// определяется по факту: для файловых баз — наличие каталога/файла базы по
    /// пути, для клиент-серверных — реальная попытка подключения, для веб-баз —
    /// заполненность адреса.
    /// <para>
    /// Сразу после запуска все базы помечаются как «проверяется» (серый значок
    /// ожидания). Результат каждой базы публикуется в строку состояния по мере
    /// готовности, а значок обновляется через подписку строки на свойства базы.
    /// Проверки идут в фоне с ограниченным параллелизмом, чтобы не блокировать UI.
    /// </para>
    /// </summary>
    private void CheckAvailability()
    {
        if (_availabilityCheckRunning)
            return;

        var targets = Infobases.ToList();
        if (targets.Count == 0)
        {
            ShowTemporaryStatusMessage(LocalizationManager.T("Main.ConfigListEmpty"));
            return;
        }

        _availabilityCheckRunning = true;
        IsLoading = true;
        LoadingMessage = LocalizationManager.T("Main.CheckAvailabilityLabel");
        foreach (var ib in targets)
            ib.SetChecking(true);
        RebuildTree();
        StatusBarInfo = string.Format(
            LocalizationManager.T("Main.AvailabilityProgress"), 0, targets.Count);

        _ = RunAvailabilityCheckAsync(targets);
    }

    /// <summary>Ограничение параллельных проверок доступности баз.</summary>
    private const int AvailabilityParallelism = 4;

    /// <summary>
    /// Размер батча результатов проверки доступности для ОДНОГО маршалинга в UI-поток
    /// (P4, v100112): вместо Dispatcher.UIThread.Post на каждую завершённую базу
    /// результаты публикуются пачками; хвост батча сбрасывается по завершении проверки.
    /// </summary>
    private const int AvailabilityBatchSize = 4;

    private bool _availabilityCheckRunning;

    private async Task RunAvailabilityCheckAsync(List<Infobase> targets)
    {
        try
        {
            var results = new List<(Infobase Base, bool Available)>(targets.Count);
            var processed = 0;

            // Батч ещё не опубликованных результатов (P4, v100112): накапливается
            // воркерами, публикуется одним маршалингом на батч.
            var pendingBatch = new List<(Infobase Base, bool Available)>();
            var batchLock = new object();

            void FlushBatch(bool force)
            {
                List<(Infobase Base, bool Available)> toApply;
                lock (batchLock)
                {
                    if (pendingBatch.Count == 0)
                        return;
                    if (!force && pendingBatch.Count < AvailabilityBatchSize)
                        return;
                    toApply = new List<(Infobase Base, bool Available)>(pendingBatch);
                    pendingBatch.Clear();
                }

                var done = Volatile.Read(ref processed);
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var (ib, available) in toApply)
                        ib.SetCheckedAvailability(available);
                    StatusBarInfo = string.Format(
                        LocalizationManager.T("Main.AvailabilityProgress"), done, targets.Count);
                });
            }

            await Task.Run(() => Parallel.ForEach(targets,
                new ParallelOptions { MaxDegreeOfParallelism = AvailabilityParallelism },
                ib =>
                {
                    var available = IsBaseAvailable(ib);
                    Interlocked.Increment(ref processed);
                    lock (results)
                        results.Add((ib, available));
                    lock (batchLock)
                        pendingBatch.Add((ib, available));
                    FlushBatch(force: false);
                })).ConfigureAwait(false);

            // Хвост батча: результаты, не дотянувшие до полного батча.
            FlushBatch(force: true);

            Dispatcher.UIThread.Post(() =>
            {
                IsLoading = false;
                RebuildTree();
                var total = results.Count;
                var unavailable = results.Count(r => !r.Available);
                ShowTemporaryStatusMessage(string.Format(
                    LocalizationManager.T("Main.AvailabilityStatus"),
                    total, total - unavailable, unavailable));
            });
        }
        finally
        {
            _availabilityCheckRunning = false;
        }
    }

    /// <summary>
    /// Показывает сообщение в нижней строке состояния на 10 секунд, после чего
    /// возвращает обычный текст. Повторный вызов сбрасывает предыдущий таймер.
    /// </summary>
    private void ShowTemporaryStatusMessage(string message)
    {
        _statusMessageCts?.Cancel();
        _statusMessageCts?.Dispose();
        _statusMessageCts = null;

        StatusBarInfo = message;

        var cts = new System.Threading.CancellationTokenSource();
        _statusMessageCts = cts;
        var token = cts.Token;
        _ = ClearStatusMessageAfterDelayAsync(token);
    }

    private async Task ClearStatusMessageAfterDelayAsync(System.Threading.CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(true);
            if (!token.IsCancellationRequested)
                UpdateStatus();
        }
        catch (TaskCanceledException)
        {
            // новое сообщение заменило предыдущее
        }
    }

    /// <summary>
    /// Доступность отдельной базы. Файловая — есть ли каталог/файл по пути;
    /// клиент-серверная — удалось ли подключиться; веб-база — заполнен ли адрес.
    /// Для клиент-серверных баз выполняется реальная попытка подключения через
    /// COM-коннектор (на Linux недоступна, поэтому такие базы считаются недоступными).
    /// </summary>
    private bool IsBaseAvailable(Infobase ib)
    {
        try
        {
            switch (ib.Connection?.Type)
            {
                case ConnectionType.File:
                    // Наличие каталога или файла базы по пути.
                    return InfobaseMaintenanceService.FileBaseExists(ib);

                case ConnectionType.ClientServer:
                {
                    // Быстрая TCP-проверка порта кластера (функция 12, флаг
                    // AvailabilityTcpPrecheckEnabled): закрытый порт — мгновенно «недоступно».
                    // На Linux COM-коннектор отсутствует, поэтому при открытом порте база
                    // всё равно считается недоступной (как и документировано выше).
                    if (NetworkAvailabilityPrecheck.IsUnreachableFast(
                            AvailabilityTcpPrecheckEnabled,
                            ib.Connection.Server,
                            ib.Connection.Port > 0 ? ib.Connection.Port : OneCPorts.Cluster,
                            NetworkDiagnosticsService.TcpPortCheckAsync))
                        return false;
                    return false;
                }

                case ConnectionType.WebServer:
                    return !string.IsNullOrWhiteSpace(ib.Connection.WebUrl);

                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    // ======================= Этап 6: папки / ярлыки / стартер =======================

    /// <summary>
    /// Недавно запускавшиеся базы (для меню трея). До семи по дате запуска,
    /// как в Windows-версии (MainWindow.Tray.cs:216 запрашивает семь).
    /// </summary>
    public List<Infobase> RecentInfobases =>
        // Приватные базы заблокированного профиля в меню трея не показываются (0.3.9.85).
        _allInfobases
            .Where(ib => ib.LastLaunchDate.HasValue && IsVisibleForPrivateFilter(ib))
            .OrderByDescending(ib => ib.LastLaunchDate)
            .Take(7)
            .ToList();

    /// <summary>Все информационные базы (для диалога выбора при очистке кеша).</summary>
    public IReadOnlyList<Infobase> Infobases => _allInfobases;

    /// <summary>Открыть каталог файловой базы в файловом менеджере рабочего стола.</summary>
    private void OpenInfobaseFolder()
    {
        var ib = SelectedInfobase;
        if (ib is null)
            return;
        if (!InfobaseMaintenanceService.OpenInfobaseFolder(ib))
            _dialog.ShowError(LocalizationManager.T("Main.ErrOpenBaseFolder"));
    }

    /// <summary>Создать ярлык .desktop на рабочем столе для запуска базы.</summary>
    private void CreateDesktopShortcut()
    {
        var ib = SelectedInfobase;
        if (ib is null)
            return;
        if (InfobaseMaintenanceService.CreateDesktopShortcut(ib))
            _dialog.ShowInfo(string.Format(LocalizationManager.T("Main.ShortcutCreated"), ib.Name));
        else
            _dialog.ShowError(string.Format(LocalizationManager.T("Main.ErrShortcutCreate"), ib.Name));
    }

    /// <summary>Запустить родной стартер 1С (1cestart).</summary>
    private void OpenNativeStarter()
    {
        if (!InfobaseMaintenanceService.OpenNativeStarter())
            _dialog.ShowError(LocalizationManager.T("Main.ErrStartStarter"));
    }

    // ======================= Очистка кеша 1С =======================

    /// <summary>
    /// Быстрая очистка всего кеша (программного и пользовательского) выбранной базы.
    /// Перед очисткой запрашивает подтверждение у пользователя.
    /// </summary>
    /// <param name="parameter">Информационная база (или null — используется выбранная).</param>
    private void QuickClearCache(object? parameter)
    {
        var ib = parameter as Infobase ?? SelectedInfobase;
        if (ib is null)
            return;

        if (!_dialog.Confirm(
            string.Format(LocalizationManager.T("Main.CacheClearAllConfirm"), ib.Name),
            LocalizationManager.T("Main.ClearCacheDlgTitle")))
            return;

        try
        {
            // Объём кэша базы до очистки — для отчёта (issue #178).
            var size = OneCCacheCleaner.GetSize(ib, OneCCacheKind.All);
            var removed = OneCCacheCleaner.Clear(ib, OneCCacheKind.All);
            var kindLabel = CacheKindLabel(OneCCacheKind.All);
            var baseLabel = string.Format(LocalizationManager.T("Main.CacheBaseOne"), ib.Name);
            var message = removed > 0
                ? string.Format(LocalizationManager.T("Main.CacheCleaned"), kindLabel, baseLabel, removed, Infobase.FormatSize(size))
                : string.Format(LocalizationManager.T("Main.CacheNotFound"), kindLabel, baseLabel);
            _dialog.ShowInfo(message, LocalizationManager.T("Main.ClearCacheDlgTitle"));
        }
        catch (Exception ex)
        {
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrCacheClear"), ex.Message),
                LocalizationManager.T("Main.CacheErrorTitle"));
        }
    }

    /// <summary>
    /// Открывает окно выбора типа кеша и информационных баз, после подтверждения выполняет очистку.
    /// </summary>
    /// <param name="kind">Тип кеша, выбранный по умолчанию.</param>
    /// <param name="defaultInfobase">База, отмеченная по умолчанию (например, выделенная в главном окне).</param>
    private void OpenCacheClean(OneCCacheKind kind, Infobase? defaultInfobase = null)
    {
        if (Infobases.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("Main.CacheEmpty"),
                LocalizationManager.T("Main.ClearCacheDlgTitle"));
            return;
        }

        // Список отдаётся копией и окно открывается модально: иначе пока оно
        // открыто, список баз можно очистить из главного окна, и очистка
        // остатков посчитает остатками уже весь кеш.
        var dialog = new CacheCleanWindow(Infobases.ToList(), kind, defaultInfobase ?? SelectedInfobase);
        if (!dialog.ShowSync(OwnerWindow()))
            return;

        var infobases = dialog.SelectedInfobases;
        var selectedKind = dialog.SelectedCacheKind;
        var cleanOrphans = dialog.CleanOrphans;
        if (selectedKind == OneCCacheKind.None)
            return;
        if (infobases.Count == 0 && !cleanOrphans)
            return;

        var kindLabel = CacheKindLabel(selectedKind);

        // Описание подтверждения: выбранные базы и/или остатки кеша от удалённых баз.
        var confirmParts = new List<string>();
        if (infobases.Count > 0)
            confirmParts.Add(string.Join(", ", infobases.Select(ib => ib.Name)));
        if (cleanOrphans)
            confirmParts.Add(LocalizationManager.T("Main.CacheOrphanNote"));

        if (!_dialog.Confirm(
            string.Format(LocalizationManager.T("Main.CacheConfirm"), kindLabel, string.Join("\n", confirmParts)),
            LocalizationManager.T("Main.ClearCacheDlgTitle")))
            return;

        try
        {
            // Объём «остатков» до очистки — для отчёта (issue #178).
            var orphanSize = cleanOrphans ? OneCCacheCleaner.GetOrphanSize(selectedKind, Infobases) : 0L;
            // Объём кэша баз до очистки — для отчёта (issue #178).
            var basesSize = infobases.Count > 0 ? OneCCacheCleaner.GetSize(selectedKind, infobases) : 0L;
            var removedBases = OneCCacheCleaner.Clear(infobases, selectedKind);
            var removedOrphans = cleanOrphans ? OneCCacheCleaner.ClearOrphans(selectedKind, Infobases) : 0;

            var resultParts = new List<string>();
            if (infobases.Count > 0)
            {
                var baseLabel = infobases.Count == 1
                    ? string.Format(LocalizationManager.T("Main.CacheBaseOne"), infobases[0].Name)
                    : string.Format(LocalizationManager.T("Main.CacheBaseMany"), infobases.Count);

                if (removedBases > 0)
                    resultParts.Add(string.Format(LocalizationManager.T("Main.CacheCleaned"), kindLabel, baseLabel, removedBases, Infobase.FormatSize(basesSize)));
                else
                    resultParts.Add(string.Format(LocalizationManager.T("Main.CacheNotFound"), kindLabel, baseLabel));
            }

            if (cleanOrphans)
            {
                if (removedOrphans > 0)
                    resultParts.Add(string.Format(LocalizationManager.T("Main.CacheOrphanRemoved"), removedOrphans, Infobase.FormatSize(orphanSize)));
                else
                    resultParts.Add(LocalizationManager.T("Main.CacheOrphanNone"));
            }

            _dialog.ShowInfo(string.Join("\n\n", resultParts), LocalizationManager.T("Main.ClearCacheDlgTitle"));
        }
        catch (Exception ex)
        {
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrCacheClear"), ex.Message),
                LocalizationManager.T("Main.CacheErrorTitle"));
        }
    }

    /// <summary>Возвращает читаемое описание типа кеша.</summary>
    private static string CacheKindLabel(OneCCacheKind kind)
    {
        return kind switch
        {
            OneCCacheKind.Program => LocalizationManager.T("Main.CacheKindProgram"),
            OneCCacheKind.User => LocalizationManager.T("Main.CacheKindUser"),
            _ => LocalizationManager.T("Main.CacheKindAll")
        };
    }

    private void RaiseCommandCanExecuteChanged()
    {
        (LaunchEnterpriseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (LaunchConfiguratorCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (EditInfobaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteInfobaseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ToggleFavoriteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (TogglePinCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (CopyConnectionStringCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (OpenInfobaseFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (QuickClearCacheCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearCacheCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearProgramCacheCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearUserCacheCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (ClearCacheBothCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RepositoryBatchUpdateCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RepositoryBrowserCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    // ======================= Пакетное обновление из хранилищ (0.3.9.88) =======================

    private System.Windows.Input.ICommand? _repositoryBatchUpdateCommand;

    /// <summary>
    /// Команда «Обновление из хранилищ…»: окно-чеклист баз с заполненным хранилищем
    /// конфигурации и последовательный прогон через конфигуратор в пакетном режиме.
    /// Активна, если хотя бы у одной базы заполнен адрес хранилища.
    /// </summary>
    public System.Windows.Input.ICommand RepositoryBatchUpdateCommand =>
        _repositoryBatchUpdateCommand ??= new RelayCommand(_ => ExecuteRepositoryBatchUpdate(),
            _ => _allInfobases.Any(b => b.Repository.HasServer));

    private void ExecuteRepositoryBatchUpdate()
    {
        var withRepo = _allInfobases.Where(b => b.Repository.HasServer).ToList();
        if (withRepo.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("RepoUpdate.NoBases"),
                LocalizationManager.T("RepoUpdate.Title"));
            return;
        }

        // Безопасность: подтверждение перед запуском конфигуратора по нескольким базам.
        if (!_dialog.Confirm(
                string.Format(LocalizationManager.T("RepoUpdate.ConfirmFormat"), withRepo.Count),
                LocalizationManager.T("RepoUpdate.Title")))
            return;

        var window = new Workspace1C.RepositoryBatchUpdateWindow(withRepo);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Массовые операции по списку баз (issue #91) =======================

    private System.Windows.Input.ICommand? _batchDumpDtCommand;

    /// <summary>
    /// Команда «Массовая выгрузка в .dt…» (issue #91): окно-чеклист баз и
    /// последовательная выгрузка каждой выбранной базы в файл .dt через ibcmd.
    /// </summary>
    public System.Windows.Input.ICommand BatchDumpDtCommand =>
        _batchDumpDtCommand ??= new RelayCommand(_ => ExecuteBatchOperations(BatchOperationKind.DumpDt),
            _ => _allInfobases.Any());

    private System.Windows.Input.ICommand? _batchLoadCfCommand;

    /// <summary>
    /// Команда «Загрузить конфигурацию из .cf по списку баз…» (issue #91): окно-чеклист
    /// баз и последовательная загрузка .cf в каждую выбранную базу через ibcmd.
    /// </summary>
    public System.Windows.Input.ICommand BatchLoadCfCommand =>
        _batchLoadCfCommand ??= new RelayCommand(_ => ExecuteBatchOperations(BatchOperationKind.LoadConfig),
            _ => _allInfobases.Any());

    private void ExecuteBatchOperations(BatchOperationKind kind)
        => ExecuteBatchDumpCore(_allInfobases.ToList(), kind);

    /// <summary>
    /// Массовая выгрузка выделенных баз в DT/CF с шаблоном имени файла (issue #92):
    /// пункт «Выгрузить в DT\CF…» блока «Для выделенных (N)…». Вид операции,
    /// каталог и шаблон читаются из настроек и сохраняются обратно после окна.
    /// </summary>
    public void ExecuteBatchDumpForSelected()
        => ExecuteBatchDumpCore(BatchOperationTargets.ToList(), kind: null);

    /// <summary>
    /// Общая точка открытия окна массовых операций: подтверждение, подстановка
    /// сохранённых каталога/шаблона/вида выгрузки (issue #92) и сохранение их
    /// после закрытия окна.
    /// </summary>
    private void ExecuteBatchDumpCore(IReadOnlyList<Infobase> bases, BatchOperationKind? kind)
    {
        if (bases.Count == 0)
        {
            _dialog.ShowInfo(LocalizationManager.T("BatchOps.NoBases"),
                LocalizationManager.T("BatchOps.Title"));
            return;
        }

        var effectiveKind = kind ?? BatchOperationsService.ParseDumpKind(_settings.BatchDumpKind);

        // Безопасность: подтверждение перед массовой операцией по нескольким базам.
        if (!_dialog.Confirm(
                string.Format(LocalizationManager.T("BatchOps.ConfirmFormat"),
                    effectiveKind switch
                    {
                        BatchOperationKind.DumpDt => LocalizationManager.T("BatchOps.KindDumpDt"),
                        BatchOperationKind.DumpCf => LocalizationManager.T("BatchOps.KindDumpCf"),
                        _ => LocalizationManager.T("BatchOps.KindLoadCf")
                    }),
                LocalizationManager.T("BatchOps.Title")))
            return;

        var window = new Workspace1C.BatchOperationsWindow(bases, effectiveKind,
            _settings.BatchDumpDirectory, _settings.BatchDumpTemplate,
            // Формат метки времени токена {Дата} — общая настройка выгрузок (issue #92).
            _settings.ExportTimestampFormat);
        window.ShowDialogSync(OwnerWindow());

        // Сохраняем настройки массовой выгрузки (шаблон, каталог, вид DT/CF).
        var vm = window.ViewModel;
        _settings.BatchDumpTemplate = string.IsNullOrWhiteSpace(vm.Template)
            ? BatchOperationsService.DefaultDumpTemplate : vm.Template.Trim();
        if (vm.IsDumpKind)
        {
            _settings.BatchDumpDirectory = vm.PathText?.Trim() ?? string.Empty;
            _settings.BatchDumpKind = BatchOperationsService.DumpKindToString(vm.Kind);
        }
        SaveSettingsSilently();
    }

    // ======================= Обозреватель хранилища конфигурации (0.3.9.128) =======================

    private System.Windows.Input.ICommand? _repositoryBrowserCommand;

    /// <summary>
    /// Команда «Хранилище конфигурации…»: окно обозревателя хранилища конфигурации
    /// выбранной базы (подключение, список версий, состав версии). Активна, только если
    /// у выбранной базы заполнен адрес хранилища (<see cref="CanOpenRepositoryBrowser"/>);
    /// CanExecute пересчитывается при смене выделения через <see cref="RaiseCommandCanExecuteChanged"/>
    /// (вызывается из сеттера SelectedInfobase).
    /// </summary>
    public System.Windows.Input.ICommand RepositoryBrowserCommand =>
        _repositoryBrowserCommand ??= new RelayCommand(_ => ExecuteRepositoryBrowser(),
            _ => CanOpenRepositoryBrowser(SelectedInfobase));

    private void ExecuteRepositoryBrowser()
    {
        if (SelectedInfobase is not { Repository.HasServer: true } infobase)
            return;

        // persistChanges: выгрузка .cf добавляет запись в историю запусков базы — сохраняем список.
        var window = new Workspace1C.RepositoryBrowserWindow(infobase, () => SaveSilently());
        window.ShowDialogSync(OwnerWindow());
    }

    /// <summary>
    /// Доступен ли обозреватель хранилища для выбранной базы: у базы должен быть
    /// заполнен адрес хранилища конфигурации. Чистый предикат — тестируется отдельно.
    /// </summary>
    public static bool CanOpenRepositoryBrowser(Infobase? infobase) =>
        infobase is { Repository.HasServer: true };

    // ======================= Центр обслуживания (0.3.9.89) =======================

    private System.Windows.Input.ICommand? _maintenanceCenterCommand;

    /// <summary>
    /// Команда «Центр обслуживания…»: окно-дашборд со сводкой состояния всех баз
    /// (доступность, последняя копия, размер, кэш, конфигурация, возраст данных,
    /// проверка обновлений). Работает со всем списком, поэтому активна всегда.
    /// </summary>
    public System.Windows.Input.ICommand MaintenanceCenterCommand =>
        _maintenanceCenterCommand ??= new RelayCommand(_ => ExecuteMaintenanceCenter());

    private void ExecuteMaintenanceCenter()
    {
        var window = new Workspace1C.MaintenanceCenterWindow(
            _allInfobases.ToList(),
            () => CheckAvailabilityCommand.Execute(null),
            ib => FindInListCommand.Execute(ib),
            MaintenanceFreeSpaceWarningGb);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Инспектор процессов 1С (0.3.9.93) =======================

    private System.Windows.Input.ICommand? _processInspectorCommand;

    /// <summary>
    /// Команда «Инспектор процессов…»: таблица всех запущенных процессов платформы 1С
    /// (база/режим/пользователь/время старта/PID/строка подключения) с автообновлением
    /// и завершением выбранного процесса. Двойной клик по известной базе — переход
    /// к ней в главном окне. Работает со всем списком, поэтому активна всегда.
    /// </summary>
    public System.Windows.Input.ICommand ProcessInspectorCommand =>
        _processInspectorCommand ??= new RelayCommand(_ => ExecuteProcessInspector());

    private void ExecuteProcessInspector()
    {
        var window = new Workspace1C.ProcessInspectorWindow(
            _allInfobases.ToList(),
            ib => FindInListCommand.Execute(ib));
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Монитор серверов 1С (0.3.9.124) =======================

    /// <summary>
    /// Открытое окно «Серверы 1С» (issue #3, комментарий 7OH от 2026-10-17): окно
    /// НЕ модальное, но при повторном запуске команды активируется уже открытое.
    /// Static — MainViewModel регистрируется как transient (по окну), а окно монитора
    /// должно жить независимо от пересоздания ViewModel.
    /// </summary>
    private static Workspace1C.ServerMonitorWindow? _serverMonitorWindow;

    private System.Windows.Input.ICommand? _serverMonitorCommand;

    /// <summary>
    /// Команда «Серверы 1С…»: встроенный монитор серверов 1С через утилиту rac
    /// (0.3.9.124, цикл 0.3.9.123–0.3.9.126) — подключение к серверу (адрес/порт/
    /// логин/пароль), просмотр кластеров, рабочих процессов, сеансов, соединений,
    /// блокировок и информации о кластере. Пароль администратора кластера не
    /// сохраняется на диск (решение планирования). Активна всегда — сервер 1С
    /// не привязан к конкретной базе списка.
    /// </summary>
    public System.Windows.Input.ICommand ServerMonitorCommand =>
        _serverMonitorCommand ??= new RelayCommand(_ => ExecuteServerMonitor());

    /// <summary>
    /// Показывает окно монитора НЕМОДАЛЬНО (issue #3): главное окно остаётся доступным.
    /// При повторном вызове команды активируется уже открытое окно (в т.ч. свёрнутое),
    /// новое окно не создаётся. Состояние окна (максимизировано) запоминается самим окном.
    /// </summary>
    private void ExecuteServerMonitor()
    {
        var existing = _serverMonitorWindow;
        if (existing is { IsVisible: true })
        {
            if (existing.WindowState == Avalonia.Controls.WindowState.Minimized)
                existing.WindowState = Avalonia.Controls.WindowState.Normal;
            existing.Activate();
            existing.Topmost = true;
            existing.Topmost = false;
            return;
        }

        // Кнопки «Импорт»/«Миграция» под списком баз (issue #27, #41) открывают
        // «Импорт из кластера 1С» / «Миграцию между кластерами 1С» с сервером, текущим
        // на момент нажатия.
        var window = new Workspace1C.ServerMonitorWindow(
            (server, clusterId) => ExecuteImportClusterInfobases(server, clusterId, autoConnect: true),
            (server, clusterId) => ExecuteMigrateClusterInfobases(server, clusterId));
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_serverMonitorWindow, window))
                _serverMonitorWindow = null;
        };
        _serverMonitorWindow = window;

        // Позиция/размер (issue #3, 7OH 19.10): окно не должно выходить за рабочую область
        // экрана ни вправо/вниз, ни влево/вверх; на экране меньше окна размер ужимается.
        // Для развёрнутого окна позиция задаётся системой — вмешиваться не нужно.
        if (window.WindowState != Avalonia.Controls.WindowState.Maximized)
            PositionServerMonitorWindow(window);

        window.Show();
    }

    /// <summary>
    /// Ужимает окно «Серверы 1С» до рабочей области экрана главного окна и клампит его
    /// позицию во все 4 стороны (issue #3, 7OH 19.10: окошко уезжало за экран). Рабочая
    /// область — экран окна-владельца (<c>Screens.ScreenFromWindow</c>), иначе — первичный
    /// экран. Координаты Avalonia задаются в физических пикселях (<c>Position</c> —
    /// PixelPoint, <c>WorkingArea</c> — PixelRect), размеры окна — в DIP; пересчёт через
    /// <c>Scaling</c>. Логика клампинга — чистый <see cref="Workspace1C.WindowSizeMath"/>.
    /// </summary>
    private static void PositionServerMonitorWindow(Workspace1C.ServerMonitorWindow window)
    {
        try
        {
            var owner = OwnerWindow();
            // Экраны доступны через окно (TopLevel.Screens); владельца предпочитаем,
            // иначе — экраны самого окна (до Show они уже доступны).
            var screens = owner?.Screens ?? window.Screens;
            var screen = owner is not null ? screens.ScreenFromWindow(owner) : null;
            screen ??= screens.Primary;
            if (screen is null || screen.Scaling <= 0)
                return;

            var wa = screen.WorkingArea;
            var scaling = screen.Scaling;
            var workLeft = wa.X / scaling;
            var workTop = wa.Y / scaling;
            var workRight = (wa.X + wa.Width) / scaling;
            var workBottom = (wa.Y + wa.Height) / scaling;

            // Окно больше рабочей области (1400х760) — ужимаем с запасом 16 DIP.
            const double workAreaSpare = 16;
            window.Width = Workspace1C.WindowSizeMath.ClampToWorkArea(
                window.Width, Math.Max(window.MinWidth, workRight - workLeft - workAreaSpare));
            window.Height = Workspace1C.WindowSizeMath.ClampToWorkArea(
                window.Height, Math.Max(window.MinHeight, workBottom - workTop - workAreaSpare));

            // По умолчанию — по центру главного окна (как в WPF); если владелец недоступен
            // (например, свёрнут в трей) — по центру рабочей области. Желаемая позиция
            // зажимается левым/верхним краем (центрирование большего главного окна даёт
            // отрицательные координаты), FitLeft/FitTop не дают уйти за правый/нижний край.
            double desiredLeft;
            double desiredTop;
            if (owner is { IsVisible: true })
            {
                desiredLeft = owner.Position.X / scaling + (owner.Width - window.Width) / 2;
                desiredTop = owner.Position.Y / scaling + (owner.Height - window.Height) / 2;
            }
            else
            {
                desiredLeft = workLeft + (workRight - workLeft - window.Width) / 2;
                desiredTop = workTop + (workBottom - workTop - window.Height) / 2;
            }
            var left = Workspace1C.WindowSizeMath.FitLeft(
                Math.Max(workLeft, desiredLeft), window.Width, workLeft, workRight);
            var top = Workspace1C.WindowSizeMath.FitTop(
                Math.Max(workTop, desiredTop), window.Height, workTop, workBottom);
            var position = new Avalonia.PixelPoint(
                (int)Math.Round(left * scaling),
                (int)Math.Round(top * scaling));
            if (position != window.Position)
                window.Position = position;
        }
        catch
        {
            // Позиционирование — вспомогательная возможность; сбой не мешает показу окна.
        }
    }

    // ================== Диагностика сети до сервера 1С (0.3.9.232, функция 12) ==================

    private System.Windows.Input.ICommand? _networkDiagnosticsCommand;

    /// <summary>
    /// Команда «Диагностика подключения…»: окно проверки сетевой доступности сервера
    /// 1С (функция 12, цикл 0.3.9.229–233) — DNS-резолв, ICMP-пинг, TCP-проверка
    /// портов с задержками и выводы по проблемам. Доступна для клиент-серверной и
    /// веб-базы (для файловой скрыта); стартовые хост/порт — из настроек подключения.
    /// </summary>
    public System.Windows.Input.ICommand NetworkDiagnosticsCommand =>
        _networkDiagnosticsCommand ??= new RelayCommand(_ => ExecuteNetworkDiagnostics(),
            _ => NetworkDiagnosticsTargets.FromInfobase(SelectedInfobase) is not null);

    private void ExecuteNetworkDiagnostics()
    {
        var target = NetworkDiagnosticsTargets.FromInfobase(SelectedInfobase);
        if (target is null)
            return;

        var portsStore = AppServices.GetRequiredService<IServerPortsStore>();
        var vm = new NetworkDiagnosticsViewModel(
            AppServices.GetRequiredService<INetworkDiagnosticsService>(),
            target,
            action => Avalonia.Threading.Dispatcher.UIThread.Post(action),
            portsStore: portsStore,
            availableServers: NetworkDiagnosticsServerSources.Merge(
                AvailableServers(), portsStore, target.Host));

        new Workspace1C.NetworkDiagnosticsWindow(vm)
            .ShowDialogSync(OwnerWindow());
    }

    // ======================= Импорт баз из кластера 1С (0.3.9.174) =======================

    private System.Windows.Input.ICommand? _importClusterInfobasesCommand;

    /// <summary>
    /// Команда «Импорт из кластера 1С…»: импорт информационных баз из кластера сервера
    /// 1С через утилиту rac (цикл 0.3.9.172–0.3.9.175) — окно с выбором сервера из
    /// справочника «Серверы 1С» (issue #27: адрес/порт/логин/пароль — из записи, вместо
    /// четырёх полей), чеклистом баз кластера (дубликаты по строке подключения сняты
    /// и помечены), сводкой «будет добавлено» и созданием недостающих групп по имени
    /// кластера при группировке. Активна всегда — кластер не привязан к конкретной
    /// базе списка (как ServerMonitorCommand). Из окна «Серверы 1С» открывается с
    /// выбранным сервером и автоподключением (issue #27).
    /// </summary>
    public System.Windows.Input.ICommand ImportClusterInfobasesCommand =>
        _importClusterInfobasesCommand ??= new RelayCommand(_ => ExecuteImportClusterInfobases());

    /// <summary>
    /// Открывает окно «Импорт из кластера 1С». Команда меню вызывает без параметров
    /// (как раньше); из окна «Серверы 1С» передаётся <paramref name="initialServer"/>
    /// и <paramref name="autoConnect"/> = true (issue #27) — окно подставляет сервер
    /// и сразу подключается. Логика применения результата — в общем
    /// <see cref="ApplyClusterImportResult"/> (единая для WPF и Avalonia).
    /// </summary>
    private void ExecuteImportClusterInfobases(
        ManagementServer? initialServer = null, Guid? initialClusterId = null, bool autoConnect = false)
    {
        var window = new Workspace1C.ClusterImportWindow(
            AppServices.GetRequiredService<IRacClient>(),
            AppServices.GetRequiredService<IInfobaseRepository>(),
            _allInfobases.ToList(),
            initialServer,
            autoConnect,
            initialClusterId);
        if (!window.ShowDialogSync(OwnerWindow()))
            return;

        ApplyClusterImportResult(window.SelectedBases);
    }

    // ======================= Миграция между кластерами 1С (issue #41) =======================

    private System.Windows.Input.ICommand? _migrateClusterInfobasesCommand;

    /// <summary>
    /// Команда «Миграция между кластерами 1С…»: перенос информационных баз между
    /// кластерами серверов 1С (issue #41, переработка по комментарию 7OH) — отдельное
    /// окно с двухпанельной раскладкой «Источник»/«Приёмник», статусами источника и
    /// приёмника, опциями «Создавать группу по имени кластера» и «Не удалять в старом
    /// кластере». Активна всегда — кластер не привязан к конкретной базе списка.
    /// </summary>
    public System.Windows.Input.ICommand MigrateClusterInfobasesCommand =>
        _migrateClusterInfobasesCommand ??= new RelayCommand(_ => ExecuteMigrateClusterInfobases());

    /// <summary>
    /// Открывает окно «Миграция между кластерами 1С». Команда меню вызывает без параметров;
    /// из окна «Серверы 1С» передаётся <paramref name="initialSourceServer"/> и выбранный
    /// кластер <paramref name="initialSourceClusterId"/> (issue #41) — окно подставляет
    /// сервер-источник и сразу подключается. Логика применения результата — в общем
    /// <see cref="ApplyClusterMigrationResult"/> (единая для WPF и Avalonia).
    /// </summary>
    private void ExecuteMigrateClusterInfobases(
        ManagementServer? initialSourceServer = null, Guid? initialSourceClusterId = null)
    {
        var window = new Workspace1C.ClusterMigrationWindow(
            AppServices.GetRequiredService<IRacClient>(),
            _allInfobases.ToList(),
            AppServices.GetRequiredService<IServerListStore>().Load(),
            initialSourceServer,
            initialSourceClusterId);
        // Окно НЕ закрывается автоматически по завершении миграции (issue #41, комментарий
        // 7OH от 2026-10-26 «закрывать окно не надо») — чтобы увидеть ошибки и при
        // необходимости повторить перенос на другой сервер. Результат применяется после
        // ручного закрытия окна (кнопка «Отмена»/крестик); без переноса Result == null —
        // ApplyClusterMigrationResult выходит рано.
        window.ShowDialogSync(OwnerWindow());
        ApplyClusterMigrationResult(window.Result);
    }

    /// <summary>
    /// Применяет результат импорта (общий для WPF и Avalonia, issue #27): добавляет
    /// новые базы в список, недостающие группы по именам импортируемых баз (имя
    /// кластера при группировке) — как корневые, сохраняет список/группы, пересобирает
    /// дерево и показывает сводку. Страховка от дубликатов: окно уже фильтровало по
    /// строкам подключения, но список мог измениться — лишнее не добавляем.
    /// </summary>
    private void ApplyClusterImportResult(IReadOnlyList<Infobase> selected)
    {
        if (selected.Count == 0)
            return;

        var added = selected
            .Where(b => !RacInfobaseMapper.IsDuplicate(_allInfobases, b))
            .ToList();
        if (added.Count == 0)
            return;

        _allInfobases.AddRange(added);
        SyncFavoriteHotkeys();

        var existingGroupNames = new HashSet<string>(
            _groups.Select(g => (g.Name ?? "").Trim()), StringComparer.OrdinalIgnoreCase);
        var groupsToAdd = added
            .Select(b => (b.Group ?? "").Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(existingGroupNames.Add)
            .Select(name => new Group { Name = name })
            .ToList();
        if (groupsToAdd.Count > 0)
            _groups.AddRange(groupsToAdd);

        SelectedInfobase = null;
        var saved = SaveSilently();
        saved &= SaveGroupsSilently();
        RebuildTree();

        if (!saved)
        {
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrLoadFailed"),
                    LocalizationManager.T("Main.SaveFailedHint")),
                LocalizationManager.T("Main.LoadErrorTitle"));
            return;
        }

        StatusBarInfo = string.Format(LocalizationManager.T("ClusterImport.SuccessFormat"), added.Count);
        _logger.Info($"Импорт из кластера 1С: добавлено {added.Count} баз, " +
                     $"пропущено дубликатов {selected.Count - added.Count}");
    }

    /// <summary>
    /// Применяет результат миграции баз между кластерами (issue #41): добавляет перенесённые
    /// базы в список приложения (в новой группе — имя кластера приёмника, как в импорте),
    /// а при подтверждении пользователем «Хотите заменить строку подключения?» заменяет
    /// сервер/порт в базах списка, имена которых совпадают с перенесёнными (по Ref, без
    /// учёта регистра; только клиент-серверные базы). Сохраняет список и показывает сводку
    /// в статус-строке.
    /// </summary>
    private void ApplyClusterMigrationResult(ClusterMigrationResult? result)
    {
        if (result is null || result.MigratedBaseNames.Count == 0)
            return;

        var changed = false;

        // 1) Перенесённые базы добавляются в новую группу (имя кластера — приёмник).
        var added = (result.AddedInfobases ?? Array.Empty<Infobase>())
            .Where(b => !RacInfobaseMapper.IsDuplicate(_allInfobases, b))
            .ToList();
        if (added.Count > 0)
        {
            _allInfobases.AddRange(added);
            SyncFavoriteHotkeys();

            var existingGroupNames = new HashSet<string>(
                _groups.Select(g => (g.Name ?? "").Trim()), StringComparer.OrdinalIgnoreCase);
            var groupsToAdd = added
                .Select(b => (b.Group ?? "").Trim())
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(existingGroupNames.Add)
                .Select(name => new Group { Name = name })
                .ToList();
            if (groupsToAdd.Count > 0)
                _groups.AddRange(groupsToAdd);
            changed = true;
        }

        // 2) При подтверждении — замена сервера/порта в существующих базах с теми же именами.
        var replaced = 0;
        if (result.ReplaceConnectionStrings)
        {
            replaced = ClusterMigrationService.ReplaceServerAndPort(
                _allInfobases, result.MigratedBaseNames, result.NewServer, result.NewPort);
            changed |= replaced > 0;
        }

        if (!changed)
            return;

        SelectedInfobase = null;
        var saved = SaveSilently();
        saved &= SaveGroupsSilently();
        RebuildTree();

        if (!saved)
        {
            _dialog.ShowError(
                string.Format(LocalizationManager.T("Main.ErrLoadFailed"),
                    LocalizationManager.T("Main.SaveFailedHint")),
                LocalizationManager.T("Main.LoadErrorTitle"));
            return;
        }

        if (replaced > 0)
            StatusBarInfo = string.Format(LocalizationManager.T("ClusterMigration.ReplacedCountFormat"), replaced);
        _logger.Info($"Миграция баз между кластерами: добавлено {added.Count} баз, " +
                     $"заменено строк подключения у {replaced} (новый сервер: {result.NewServer}:{result.NewPort}, issue #41)");
    }

    // ======================= Статистика использования баз (0.3.9.95) =======================

    private System.Windows.Input.ICommand? _usageStatisticsCommand;

    /// <summary>
    /// Команда «Статистика использования…»: аналитика по истории запусков всех баз
    /// (число запусков, первый/последний запуск, дней с последнего запуска, сводка,
    /// распределение по дням недели). Работает со всем списком, поэтому активна всегда.
    /// </summary>
    public System.Windows.Input.ICommand UsageStatisticsCommand =>
        _usageStatisticsCommand ??= new RelayCommand(_ => ExecuteUsageStatistics());

    private void ExecuteUsageStatistics()
    {
        var window = new Workspace1C.UsageStatisticsWindow(_allInfobases.ToList());
        window.ShowDialogSync(OwnerWindow());
    }

    /// <summary>Предупреждение в журнал из окна: журнал живёт во вьюмодели.</summary>
    public void LogWarning(string message) => _logger.Warn(message);

    // ======================= Сравнение конфигураций (0.3.9.99) =======================

    private System.Windows.Input.ICommand? _configDiffCommand;

    /// <summary>
    /// Команда «Сравнение конфигураций…»: сравнение конфигурации выбранной базы
    /// с эталонным .cf или двух .cf между собой; результат — отчёт об отличиях
    /// по типам метаданных с экспортом CSV/TXT (0.3.9.99, функция №9).
    /// Работает со всем списком, поэтому активна всегда.
    /// </summary>
    public System.Windows.Input.ICommand ConfigDiffCommand =>
        _configDiffCommand ??= new RelayCommand(_ => ExecuteConfigDiff());

    private void ExecuteConfigDiff()
    {
        var window = new Workspace1C.ConfigDiffSetupWindow(
            _allInfobases.ToList(),
            SelectedInfobase,
            FindInstalledPlatformVersions(),
            _settings.LastFileCreatePlatformVersion ?? string.Empty);
        window.ShowDialogSync(OwnerWindow());
    }

    // ======================= Обозреватель метаданных (0.3.9.133) =======================

    private System.Windows.Input.ICommand? _metadataExplorerCommand;

    /// <summary>
    /// Команда «Обозреватель метаданных…»: окно просмотра дерева метаданных конфигурации
    /// (подсистемы → типы → объекты) без интерактивного конфигуратора — источник (база
    /// или файл .cf) выгружается в XML через /DumpConfigToFiles (0.3.9.132–0.3.9.136).
    /// Источник выбирается в окне, поэтому активна всегда.
    /// </summary>
    public System.Windows.Input.ICommand MetadataExplorerCommand =>
        _metadataExplorerCommand ??= new RelayCommand(_ => ExecuteMetadataExplorer());

    private void ExecuteMetadataExplorer()
    {
        var window = new Workspace1C.MetadataExplorerWindow(
            _allInfobases.ToList(),
            SelectedInfobase);
        window.ShowDialogSync(OwnerWindow());
    }
}
#endif
