using System;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Регресс-тест issue #63 (v100113, пункты 18–20 комментов): дубль строки
/// <c>LoadModule _1cws_module</c> в выбранном для публикации файле алиасов при наличии
/// LoadModule в httpd.conf (include-цепочка) — подчистка. Код не менялся с 1.0.1.11 —
/// тест фиксирует контракт <see cref="WebPublishConfPostProcessor"/>: сначала
/// <see cref="WebPublishConfPostProcessor.DeduplicateLoadModule"/> схлопывает повторы
/// (webinst дописывает строку в -confpath), затем
/// <see cref="WebPublishConfPostProcessor.RemoveLoadModuleIfPresentInChain"/> удаляет
/// ВСЕ вхождения в файле алиасов, поскольку модуль уже подключён в цепочке.
/// </summary>
public sealed class WebPublishLoadModuleDedupRegressionTests
{
    private const string ModuleLine =
        "LoadModule _1cws_module \"C:/Apache/Apache24/modules/wsap24.dll\"";

    [Fact]
    public void AliasDuplicate_IsDeduplicated_ThenRemoved_WhenMarkerInHttpdConf()
    {
        // Файл алиасов после webinst: строка модуля продублирована webinst'ом.
        var aliasWithDuplicate =
            "Alias /base1 \"C:/www/base1\"\n" +
            ModuleLine + "\n" +
            "<Directory \"C:/www/base1\">\n" +
            "    AllowOverride All\n" +
            "</Directory>\n" +
            ModuleLine + "\n";

        // httpd.conf (include-цепочка) уже подключает модуль.
        var httpdConf = "# 1C web-server module\n" + ModuleLine + "\n";

        // Шаг 1: схлопывание дубликатов (оставляем первое вхождение).
        var (deduped, removedDuplicates) = WebPublishConfPostProcessor.DeduplicateLoadModule(aliasWithDuplicate);
        Assert.Equal(1, removedDuplicates);
        Assert.Equal(1, CountOccurrences(deduped, WebPublishConfPostProcessor.LoadModuleMarker));

        // Шаг 2: маркер есть в include-цепочке → в файле алиасов подчистка полностью.
        var (cleaned, removedLines) = WebPublishConfPostProcessor.RemoveLoadModuleIfPresentInChain(
            deduped, new[] { httpdConf });
        Assert.Equal(1, removedLines);
        Assert.DoesNotContain(WebPublishConfPostProcessor.LoadModuleMarker, cleaned, StringComparison.OrdinalIgnoreCase);
        // Остальное содержимое файла алиасов сохранено.
        Assert.Contains("Alias /base1", cleaned, StringComparison.Ordinal);
        Assert.Contains("<Directory \"C:/www/base1\">", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void AliasFile_KeepsLoadModule_WhenMarkerNotInChain()
    {
        var alias = "Alias /base1 \"C:/www/base1\"\n" + ModuleLine + "\n";
        var (cleaned, removedLines) = WebPublishConfPostProcessor.RemoveLoadModuleIfPresentInChain(
            alias, new[] { "# httpd.conf без модуля 1С\n" });

        Assert.Equal(0, removedLines);
        Assert.Equal(alias, cleaned);
    }

    private static int CountOccurrences(string text, string marker)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += marker.Length;
        }
        return count;
    }
}
