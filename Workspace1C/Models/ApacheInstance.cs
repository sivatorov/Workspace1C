using System;
using System.Collections.Generic;
using Workspace1C.Services;

namespace Workspace1C.Models;

/// <summary>
/// Установленный инстанс веб-сервера Apache (issue #63): служба/процесс, корневой
/// каталог, файл конфигурации и включаемые файлы. Используется окном
/// «Публикация на веб-сервере» для автозаполнения конфигурации webinst.
/// </summary>
public sealed class ApacheInstance
{
    /// <summary>Идентификатор: имя службы (Windows) или имя процесса/версия (Linux).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Короткое имя: служба httpd (Windows) или «Apache» (Linux).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Описание: DisplayName/Description службы или строка версии.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Файл конфигурации Apache (httpd.conf / apache2.conf) — основной.</summary>
    public string ConfPath { get; set; } = string.Empty;

    /// <summary>Корневой каталог Apache (ServerRoot).</summary>
    public string ServerRoot { get; set; } = string.Empty;

    /// <summary>Каталог публикации по умолчанию (htdocs / DocumentRoot).</summary>
    public string Htdocs { get; set; } = string.Empty;

    /// <summary>
    /// DocumentRoot из конфигурации (issue #63): точный каталог публикации по умолчанию
    /// (может включать подкаталог — например «ServerRoot\htdocs\27»). Пусто — используется
    /// <see cref="Htdocs"/>. Заполняется детектором из директивы DocumentRoot.
    /// </summary>
    public string DocumentRoot { get; set; } = string.Empty;

    /// <summary>Порты директив Listen конфига (issue #63); пусто — порт 80.</summary>
    public IReadOnlyList<int> ListenPorts { get; set; } = System.Array.Empty<int>();

    /// <summary>Вид инстанса (Apache 2.4 / Apache 2.2) для webinst.</summary>
    public WebServerKind Kind { get; set; } = WebServerKind.Apache24;

    /// <summary>Файлы, подключаемые директивами Include/IncludeOptional из конфига (абсолютные пути).</summary>
    public IReadOnlyList<string> IncludeFiles { get; set; } = System.Array.Empty<string>();

    /// <summary>Каталоги-маски Include с wildcard «*» (абсолютные пути).</summary>
    public IReadOnlyList<string> IncludeDirs { get; set; } = System.Array.Empty<string>();

    /// <summary>
    /// Include-цели файлов алиасов 1С («1c8Aliases*.conf»), встреченные в include-графе
    /// (issue #63, комментарий 7OH от 2026-10-26: «Его можно же найти в строках с
    /// include?»). Содержат нераскрытые <c>${…}</c>, если переменная не определена ни в
    /// одном файле графа, — по шаблону и номеру инстанса VM находит используемый файл
    /// алиасов рядом с конфигом (fallback), даже когда Include-граф не прочитал файл.
    /// С версии 1.0.0.98 нераскрытые <c>${…}</c> дополнительно раскрываются по
    /// финальному словарю <see cref="Defines"/> после стабилизации графа.
    /// </summary>
    public IReadOnlyList<string> AliasTargetPatterns { get; set; } = System.Array.Empty<string>();

    /// <summary>
    /// Финальный словарь переменных <c>Define</c> конфигурации (issue #63): значения
    /// после стабилизации include-графа (поздние определения переопределяют ранние).
    /// Заполняется детектором в <c>PopulateIncludes</c>; VM использует его для
    /// раскрытия шаблонов и подбора номера инстанса при поиске файла алиасов
    /// («Define OneCPort 27» → суффикс «27»). Пусто — Define в конфиге не найдены.
    /// </summary>
    public IReadOnlyDictionary<string, string> Defines { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Исходная строка запуска службы Apache (Windows: значение <c>ImagePath</c> из
    /// реестра; Linux: пустая) — issue #63, комментарий 7OH от 2026-10-29: «Добавляем
    /// в отладку исходные данные службы». Сохраняется как есть (без нормализации),
    /// чтобы по логам CM_PUBLISH было видно, что реально записано в службе, и чтобы
    /// VM-фаллбэк мог извлечь имя конфига (<c>-f conf\httpd27.conf</c>), даже если
    /// <see cref="ConfPath"/> по какой-то причине не извлёкся.
    /// </summary>
    public string RawCommandLine { get; set; } = string.Empty;
}