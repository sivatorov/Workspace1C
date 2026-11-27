using System;
using System.IO;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #143 (1.0.1.127): «pgAdmin копию делает, а стартер — нет» чаще всего означает
/// несовпадение мажорной версии <c>pg_dump</c> и сервера PostgreSQL. Теперь версия
/// утилиты пишется в журнал (<c>pg_dump --version</c>), а в сообщение об ошибке при
/// «version mismatch» добавляется подсказка, что указывать нужно утилиту той же
/// мажорной версии.
/// </summary>
public sealed class Issue143VersionMismatchHintTests
{
    [Theory]
    [InlineData("pg_dump: error: aborting because of server version mismatch")]
    [InlineData("server version mismatch")]
    [InlineData("Server version mismatch: 15.4 vs 16.1")]
    [InlineData("несовпадение версий клиента и сервера")]
    public void LooksLikeVersionMismatch_DetectsCommonMessages(string output)
        => Assert.True(DbmsBackupService.LooksLikeVersionMismatch(output));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pg_dump: error: connection to server failed")]
    [InlineData("permission denied for database")]
    public void LooksLikeVersionMismatch_IgnoresOtherErrors(string? output)
        => Assert.False(DbmsBackupService.LooksLikeVersionMismatch(output));

    [Fact]
    public void FailureMessage_AppendsHintOnVersionMismatch()
    {
        var message = DbmsBackupService.BuildToolFailureMessage(
            "pg_dump", @"C:\backups\db.dump", 1,
            "pg_dump: error: aborting because of server version mismatch");

        Assert.Contains("pg_dump", message);
        // Подсказка локализована; проверяем, что сообщение стало длиннее вывода.
        Assert.True(message.Length > "pg_dump".Length + 40);
    }

    [Fact]
    public void VersionIsLoggedBeforeDump()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Workspace1C.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        var code = File.ReadAllText(Path.Combine(root!.FullName,
            "Workspace1C", "Services", "DbmsBackupService.cs"));

        Assert.Contains("LogToolVersionAsync", code);
        Assert.Contains("--version", code);
        Assert.Contains("Dbms.VersionMismatchHint", code);
    }
}
