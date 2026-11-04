using System;
using System.Linq;
using Workspace1C.Models;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты issue #79 (v1.0.1.32): список «СУБД» в мастере «Развернуть тестовую копию»
/// (<see cref="TestDeployDbmsOption"/> — вместо свободного ввода, репорт 7OH: «СУБД —
/// почему-то является полем ввода, а не списком выбора») и толерантный разбор
/// реального вывода rac 8.5.4 «infobase info» (формат «ключ␣␣␣: значение»).
/// </summary>
public class TestDeployDbmsOptionsTests
{
    /// <summary>Заглушка локализации: возвращает сам ключ (проверяется маппинг значений).</summary>
    private static string KeyTranslate(string key) => key;

    [Fact]
    public void BuildOptions_ContainsAllIbcmdSupportedTypes_InOrder()
    {
        // Список согласован с планом v1.0.1.32: типы, поддерживаемые ibcmd/rac.
        var options = TestDeployDbmsOption.BuildOptions(null, KeyTranslate);

        Assert.Equal(
            new[]
            {
                "MSSQLServer", "PostgreSQL", "IBMDB2", "OracleDatabase", "MySQL", "MariaDB"
            },
            options.Select(o => o.Value).ToArray());
        // Подписи — по ключам локализации (человекочитаемый текст, не CLR-имя).
        Assert.Equal("TestDeploy.DbmsMssqlServer", options[0].Text);
        Assert.Equal("TestDeploy.DbmsPostgreSql", options[1].Text);
    }

    [Fact]
    public void BuildOptions_KnownCurrentValue_DoesNotDuplicate()
    {
        var options = TestDeployDbmsOption.BuildOptions("PostgreSQL", KeyTranslate);

        Assert.Equal(6, options.Count);
        Assert.True(options.Count(o => o.Value == "PostgreSQL") == 1);
    }

    [Fact]
    public void BuildOptions_UnknownCurrentValue_IsAppendedNotLost()
    {
        // Значение из rac-резолва/профиля, которого нет в списке, добавляется
        // последней опцией — ввод пользователя не теряется (issue #79).
        var options = TestDeployDbmsOption.BuildOptions("ServerODBC", KeyTranslate);

        Assert.Equal(7, options.Count);
        var last = options[^1];
        Assert.Equal("ServerODBC", last.Value);
        Assert.Equal("ServerODBC", last.Text);
    }

    [Theory]
    [InlineData("mssqlserver", 0)]
    [InlineData("PostgreSQL ", 1)]
    [InlineData("IBMDB2", 2)]
    [InlineData("oracledatabase", 3)]
    [InlineData("MySQL", 4)]
    [InlineData("mariadb", 5)]
    [InlineData("нет такого", -1)]
    [InlineData("", -1)]
    [InlineData(null, -1)]
    public void IndexOf_IsCaseInsensitiveAndTrims(string? value, int expected)
    {
        var options = TestDeployDbmsOption.BuildOptions(null, KeyTranslate);

        Assert.Equal(expected, TestDeployDbmsOption.IndexOf(options, value));
    }

    // ---------- Толерантный разбор вывода rac 8.5.4 (issue #79) ----------

    /// <summary>
    /// Реальный вывод «infobase info» rac 8.5.4 (7OH, комментарий 6609833628): ключ
    /// отделён от двоеточия ВЫРАВНИВАНИЕМ пробелами («dbms  : MSSQLServer») — по этому
    /// образцу «вручную rac отдаёт все поля, а программа получает пустые».
    /// </summary>
    private const string Rac854InfoOutput = """
        infobase                            : 9f3c1a2b-4d5e-4f60-8a7b-1c2d3e4f5a6b
        name                                : Demo_8.3.27
        descr                               : Демонстрационная база
        dbms                                : MSSQLServer
        db-server                           : localhost
        db-name                             : Demo_8.3.27
        db-user                             : sa
        locale                              : ru_RU
        security-level                      : 0
        sessions-deny                       : off
        scheduled-jobs-deny                 : off
        """;

    [Fact]
    public void ToInfobaseInfo_Rac854AlignedColumns_RecognizesAllDbmsFields()
    {
        var info = RacOutputParser.ToInfobaseInfo(Rac854InfoOutput);

        Assert.NotNull(info);
        Assert.Equal("Demo_8.3.27", info!.Name);
        Assert.Equal("MSSQLServer", info.Dbms);
        Assert.Equal("localhost", info.DbServer);
        Assert.Equal("Demo_8.3.27", info.DbName);
        Assert.Equal("sa", info.DbUser);
    }

    [Fact]
    public void ParseInfo_KeyWithSpacesBeforeColon_IsTrimmed()
    {
        // Толерантность парсера: ключ — всё до ПЕРВОГО двоеточия при любом числе
        // пробелов/табуляций вокруг; значение может содержать двоеточие внутри.
        var props = RacOutputParser.ParseInfo("dbms\t\t  : MSSQLServer\nname  : base:test\n");

        Assert.Equal("MSSQLServer", props["dbms"]);
        Assert.Equal("base:test", props["name"]);
    }
}
