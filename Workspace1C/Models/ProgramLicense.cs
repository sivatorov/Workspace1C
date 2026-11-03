using System;
using System.Globalization;

namespace Workspace1C.Models;

/// <summary>
/// Установленная программная лицензия 1С — строка разбора вывода утилит
/// «ring license list» / «licensetool» (п.5 «Мониторинг лицензий», выпуск 1.0.0.87).
/// Вывод утилит зависит от платформы и локали окружения, поэтому парсер
/// <see cref="Services.RingLicenseParser"/> допускает оба формата (таблицу и блоки)
/// и заполняет только распознанные поля; остальная информация остаётся в
/// <see cref="RawText"/> для отображения «как есть».
/// </summary>
public sealed class ProgramLicense
{
    /// <summary>Серийный номер программной лицензии (например <c>12345678-1234-1234-1234-123456789012</c>); пусто — не распознан.</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>Тип лицензии по выводу утилиты (например «клиентская», «сервер», «server», «client», «CONFIG-FULL»); пусто — не распознан.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Версия платформы/сборки, для которой выдана лицензия (8.3.x и т.п.); пусто — не распознано.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Модуль/назначение лицензии (строка вывода утилиты).</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Пользователь/владелец лицензии (строка вывода утилиты).</summary>
    public string User { get; set; } = string.Empty;

    /// <summary>Состояние лицензии (строка вывода утилиты: «active», «used» и т.п.); пусто — не распознано.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Срок действия лицензии (если утилита его выводит); null — не распознан.</summary>
    public DateTime? ExpiryDate { get; set; }

    /// <summary>
    /// Количество лицензий/рабочих мест, которое даёт серийный номер
    /// (0 — утилита не вывела; для расчёта сводки считается 1).
    /// </summary>
    public int LicenseCount { get; set; }

    /// <summary>Сырая строка вывода утилиты (для диагностики и отображения «как есть»).</summary>
    public string RawText { get; set; } = string.Empty;

    /// <summary>Владелец лицензии (организация/ФИО) из файла *.lic (issue #83, v1.0.1.26); пусто — не распознан.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Регистрационный номер лицензии из файла *.lic (issue #83, v1.0.1.26); пусто — не распознан.</summary>
    public string RegNumber { get; set; } = string.Empty;

    /// <summary>Имя компьютера, для которого выдана лицензия, из файла *.lic (issue #83, v1.0.1.26); пусто — не распознано.</summary>
    public string Computer { get; set; } = string.Empty;

    /// <summary>Дата выдачи лицензии из файла *.lic (issue #83, v1.0.1.26); null — не распознана.</summary>
    public DateTime? IssueDate { get; set; }

    /// <summary>Полный путь к файлу *.lic (issue #83, v1.0.1.27); пусто — файл не найден/лицензия только из вывода ring.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Однострочная выжимка файла *.lic для подсказки по строке таблицы (issue #83, v1.0.1.26); пусто — файл недоступен.</summary>
    public string RawDetails { get; set; } = string.Empty;

    /// <summary>
    /// ПОЛНОЕ содержимое файла *.lic с переводами строк (issue #83, v1.0.1.28):
    /// показывается в окне деталей многострочно, целиком (RawDetails остаётся
    /// однострочной выжимкой для тултипа строки таблицы). Пусто — файл не читался.
    /// </summary>
    public string RawFullText { get; set; } = string.Empty;
}
