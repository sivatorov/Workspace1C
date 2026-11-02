using System;
using System.Collections.Generic;
using Workspace1C.Localization;
using Workspace1C.Models;

namespace Workspace1C.ViewModels;

/// <summary>
/// Строка вкладки «Лицензии» окна «Серверы 1С» (п.5 «Мониторинг лицензий»,
/// выпуск 1.0.0.87): занятая сеансом лицензия по данным rac «session list --licenses».
/// Форматирование для отображения — сеансы без информации о лицензии (обычная
/// «session list» или без параметра) не попадают в коллекцию. Чистый .NET.
/// </summary>
public sealed class RacLicensedSessionRow
{
    private readonly RacSessionInfo _session;
    private readonly string _infobaseName;

    public RacLicensedSessionRow(RacSessionInfo session, string infobaseName = "")
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _infobaseName = infobaseName;
    }

    /// <summary>Идентификатор сеанса.</summary>
    public Guid SessionId => _session.Id;

    /// <summary>Имя пользователя сеанса.</summary>
    public string User => _session.User;

    /// <summary>Имя компьютера клиента.</summary>
    public string Host => _session.Host;

    /// <summary>Клиентское приложение (app-id).</summary>
    public string AppId => _session.AppId;

    /// <summary>Имя информационной базы сеанса («—» при неизвестной).</summary>
    public string InfobaseName => string.IsNullOrWhiteSpace(_infobaseName) ? "—" : _infobaseName;

    /// <summary>Тип лицензии сеанса (поле «lic-type»: server/client/web и т.п.).</summary>
    public string LicType => string.IsNullOrWhiteSpace(_session.LicType) ? "—" : _session.LicType;

    /// <summary>
    /// Описание лицензии для таблицы (issue #83, v1.0.1.25, 7OH: «а какие есть?»):
    /// тип (soft/HASP) + представление rac («Клиент 8100000001 1 польз.») или серия,
    /// с пометкой, если лицензию получил сам клиент, а не сервер 1С.
    /// </summary>
    public string LicenseDescription
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_session.LicType))
                parts.Add(_session.LicType.Trim());
            if (!string.IsNullOrWhiteSpace(_session.LicensePresentation))
                parts.Add(_session.LicensePresentation.Trim());
            else if (!string.IsNullOrWhiteSpace(_session.LicenseSeries))
                parts.Add(_session.LicenseSeries.Trim());
            if (_session.LicenseIssuedByServer == false)
                parts.Add(LocalizationManager.T("ServerMonitor.Licenses.IssuedByClient"));
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }
    }

    /// <summary>Максимальное число лицензий, доступное сеансу (0 — не указано).</summary>
    public string MaxLicensesText => _session.MaxLicenses > 0
        ? _session.MaxLicenses.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "—";

    /// <summary>
    /// Число занятых сеансом лицензий: явное поле «licenses»; если rac его не вернул,
    /// но тип лицензии есть — считается 1 (один сеанс = одна лицензия).
    /// </summary>
    public int LicensesUsed => _session.Licenses > 0 ? _session.Licenses : 1;

    /// <summary>Текст занятых лицензий (например «1»).</summary>
    public string LicensesUsedText => LicensesUsed.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Тип лицензии для сортировки (пустой — «—» идёт последним).</summary>
    public string LicTypeSort => string.IsNullOrWhiteSpace(_session.LicType) ? "~" : _session.LicType;

    /// <summary>«Занято лицензий» (для заголовка колонки данных сеанса).</summary>
    public string LicensesCaption => LocalizationManager.T("ServerMonitor.Licenses.Columns.Used");
}
