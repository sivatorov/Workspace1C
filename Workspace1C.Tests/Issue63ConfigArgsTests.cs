using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Issue #63 (1.0.1.6): службы Apache, созданные обёрткой 1С/Apache monitor, держат
/// ключи <c>-d</c>/<c>-f</c> не в ImagePath, а в реестровом параметре
/// <c>HKLM\SYSTEM\CurrentControlSet\Services\<служба>\Parameters\ConfigArgs</c>
/// (REG_MULTI_SZ). Тестируется чистая 5-аргументная перегрузка
/// <see cref="ApacheInstanceDetector.ParseWindowsServiceImagePath"/>(serviceName,
/// displayName, description, imagePath, configArgs) — без реестра.
/// </summary>
public sealed class Issue63ConfigArgsTests
{
    private const string ServiceName = "Apache2.4";
    private const string DisplayName = "Apache/2.4.57 (Win64)";
    private const string ImagePath = "\"c:\\Apache\\Apache24\\bin\\httpd.exe\" -k runservice";

    // ---- Кейс 1: ConfigArgs одной строкой (вариант из комментариев 7OH) ----

    [Fact]
    public void ConfigArgs_AsSingleString_ParsesServerRootAndConf()
    {
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            ServiceName, DisplayName, "",
            ImagePath, "-d c:\\Apache\\Apache24 -f conf\\httpd27.conf");

        Assert.NotNull(instance);
        Assert.Equal("c:\\Apache\\Apache24", instance.ServerRoot, ignoreCase: true);
        Assert.Equal("c:\\Apache\\Apache24\\conf\\httpd27.conf", instance.ConfPath, ignoreCase: true);
    }

    // ---- Кейс 2: ConfigArgs в формате REG_MULTI_SZ (склейка элементов пробелом) ----

    [Fact]
    public void ConfigArgs_AsMultiSzJoin_ParsesSameResult()
    {
        // REG_MULTI_SZ: [ "-d", "c:\Apache\Apache24", "-f", "conf\httpd27.conf" ]
        var joined = string.Join(" ", new[]
        {
            "-d", "c:\\Apache\\Apache24", "-f", "conf\\httpd27.conf"
        });

        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            ServiceName, DisplayName, "", ImagePath, joined);

        Assert.NotNull(instance);
        Assert.Equal("c:\\Apache\\Apache24", instance.ServerRoot, ignoreCase: true);
        Assert.Equal("c:\\Apache\\Apache24\\conf\\httpd27.conf", instance.ConfPath, ignoreCase: true);
    }

    // ---- Кейс 3: null/пустой ConfigArgs — прежнее поведение (регресс) ----

    [Fact]
    public void ConfigArgs_Null_KeepsOldBehavior()
    {
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            ServiceName, DisplayName, "", ImagePath, null);

        Assert.NotNull(instance);
        // ServerRoot выведен из каталога exe (bin\..\..), конфиг — дефолтный httpd.conf.
        Assert.Equal("c:\\Apache\\Apache24", instance.ServerRoot, ignoreCase: true);
        Assert.EndsWith("conf\\httpd.conf", instance.ConfPath, StringComparison.OrdinalIgnoreCase);
        // RawCommandLine без ConfigArgs — как раньше (старый формат строки службы).
        Assert.Equal(ImagePath, instance.RawCommandLine);
    }

    [Fact]
    public void ConfigArgs_Empty_KeepsOldBehavior()
    {
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            ServiceName, DisplayName, "", ImagePath, "   ");

        Assert.NotNull(instance);
        Assert.Equal(ImagePath, instance.RawCommandLine);
    }

    // ---- Кейс 4: незакрытая кавычка в ImagePath — токенизация-фолбэк ----

    [Fact]
    public void ConfigArgs_UnclosedQuoteInImagePath_StillParsesSwitches()
    {
        var brokenImagePath = "\"c:\\Apache\\Apache24\\bin\\httpd.exe -k runservice";

        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            ServiceName, DisplayName, "",
            brokenImagePath, "-d c:\\Apache\\Apache24 -f conf\\httpd27.conf");

        Assert.NotNull(instance);
        Assert.Equal("c:\\Apache\\Apache24", instance.ServerRoot, ignoreCase: true);
        Assert.Equal("c:\\Apache\\Apache24\\conf\\httpd27.conf", instance.ConfPath, ignoreCase: true);
    }

    // ---- Кейс 5: конфликт -d/-f в ImagePath и ConfigArgs — приоритет ConfigArgs ----

    [Fact]
    public void ConfigArgs_ConflictWithImagePath_ConfigArgsWins()
    {
        // Конфликт (документировано в XML-доке перегрузки): -d/-f есть в обоих
        // источниках → приоритет у ConfigArgs как у фактических аргументов службы.
        var imagePathWithSwitches =
            "\"c:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Old\\Root -f conf\\old.conf -k runservice";

        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            ServiceName, DisplayName, "",
            imagePathWithSwitches, "-d c:\\Apache\\Apache24 -f conf\\httpd27.conf");

        Assert.NotNull(instance);
        Assert.Equal("c:\\Apache\\Apache24", instance.ServerRoot, ignoreCase: true);
        Assert.Equal("c:\\Apache\\Apache24\\conf\\httpd27.conf", instance.ConfPath, ignoreCase: true);
        Assert.DoesNotContain("Old", instance.ServerRoot, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Лог CM_PUBLISH: RawCommandLine содержит оба источника ----

    [Fact]
    public void ConfigArgs_NonEmpty_RawCommandLineShowsBothSources()
    {
        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            ServiceName, DisplayName, "",
            ImagePath, "-d c:\\Apache\\Apache24 -f conf\\httpd27.conf");

        Assert.NotNull(instance);
        // Пользователь видит в логе CM_PUBLISH, откуда пришли параметры службы.
        Assert.Contains("ImagePath=", instance.RawCommandLine, StringComparison.Ordinal);
        Assert.Contains("httpd.exe", instance.RawCommandLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ConfigArgs=", instance.RawCommandLine, StringComparison.Ordinal);
        Assert.Contains("-d c:\\Apache\\Apache24", instance.RawCommandLine, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-f conf\\httpd27.conf", instance.RawCommandLine, StringComparison.OrdinalIgnoreCase);
    }

    // ---- ConfigArgs без -d/-f: ключи ImagePath сохраняются ----

    [Fact]
    public void ConfigArgs_WithoutSwitches_KeepsImagePathSwitches()
    {
        var imagePath = "\"c:\\Apache\\Apache24\\bin\\httpd.exe\" -d c:\\Apache\\Apache24 -k runservice";

        var instance = ApacheInstanceDetector.ParseWindowsServiceImagePath(
            ServiceName, DisplayName, "", imagePath, "-k runservice");

        Assert.NotNull(instance);
        // ConfigArgs не содержит -d → ключ -d из ImagePath сохранён.
        Assert.Equal("c:\\Apache\\Apache24", instance.ServerRoot, ignoreCase: true);
    }
}
