using System;
using System.Collections.Generic;

namespace Workspace1C.Models;

/// <summary>
/// Пара «значение/текст» для списка «СУБД» в мастере «Развернуть тестовую копию»
/// (issue #79, v1.0.1.32, репорт 7OH: «СУБД — почему-то является полем ввода, а не
/// списком выбора»). Значения — типы, поддерживаемые rac/ibcmd
/// («infobase create --dbms»): MSSQLServer, PostgreSQL, IBMDB2, OracleDatabase,
/// MySQL, MariaDB (в договорённости с планом v1.0.1.32 — по образцу списка диалогов
/// создания базы кластера + MySQL/MariaDB). Переопределение <see cref="ToString"/> —
/// по образцу <see cref="TestDeployEngineOption"/>: в выпадающем списке показывается
/// человекочитаемый текст (например, «MS SQL Server»), а не CLR-имя значения.
/// </summary>
public sealed class TestDeployDbmsOption
{
    /// <param name="value">Значение параметра rac «--dbms» (хранится в сценарии).</param>
    /// <param name="text">Локализованный текст для отображения.</param>
    public TestDeployDbmsOption(string value, string text)
    {
        Value = value;
        Text = text;
    }

    /// <summary>Значение «--dbms» (сохраняется в <see cref="TestDeployScenario.Dbms"/>).</summary>
    public string Value { get; }

    /// <summary>Текст для отображения пользователю.</summary>
    public string Text { get; }

    /// <summary>Отображаемый текст вместо CLR-имени значения (issue #79).</summary>
    public override string ToString() => Text;

    /// <summary>Поддерживаемые значения «--dbms» (по образцу списков диалогов создания базы кластера).</summary>
    public static readonly string[] Values =
    {
        RacDbms.MssqlServer,
        RacDbms.PostgreSql,
        RacDbms.IbmDb2,
        RacDbms.OracleDatabase,
        "MySQL",
        "MariaDB"
    };

    /// <summary>
    /// Опции списка с человекочитаемыми подписями через <paramref name="translate"/>.
    /// Если текущее значение (<paramref name="currentValue"/>, например подставленное
    /// rac-резолвом или из профиля СУБД) не входит в список — оно добавляется последней
    /// опцией, чтобы не терять ввод пользователя (issue #79).
    /// Public — используется окнами обеих платформ (WPF и Avalonia) и юнит-тестами.
    /// </summary>
    /// <param name="currentValue">Текущее значение поля СУБД (может быть пустым/неизвестным).</param>
    /// <param name="translate">Функция локализации (ключ → текст).</param>
    public static IReadOnlyList<TestDeployDbmsOption> BuildOptions(
        string? currentValue, Func<string, string> translate)
    {
        var options = new List<TestDeployDbmsOption>(Values.Length + 1);
        foreach (var value in Values)
        {
            var text = translate(KeyFor(value));
            options.Add(new TestDeployDbmsOption(
                value, string.IsNullOrWhiteSpace(text) ? value : text));
        }

        var current = (currentValue ?? string.Empty).Trim();
        if (current.Length > 0 && IndexOf(options, current) < 0)
            options.Add(new TestDeployDbmsOption(current, current));
        return options;
    }

    /// <summary>Индекс опции по значению (без учёта регистра); -1 — не найдено. Internal — юнит-тесты.</summary>
    public static int IndexOf(IReadOnlyList<TestDeployDbmsOption> options, string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Value, normalized, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>Ключ локализации подписи по значению «--dbms».</summary>
    private static string KeyFor(string value) => value switch
    {
        RacDbms.MssqlServer => "TestDeploy.DbmsMssqlServer",
        RacDbms.PostgreSql => "TestDeploy.DbmsPostgreSql",
        RacDbms.IbmDb2 => "TestDeploy.DbmsIbmDb2",
        RacDbms.OracleDatabase => "TestDeploy.DbmsOracleDatabase",
        "MySQL" => "TestDeploy.DbmsMySql",
        "MariaDB" => "TestDeploy.DbmsMariaDb",
        _ => value
    };
}
