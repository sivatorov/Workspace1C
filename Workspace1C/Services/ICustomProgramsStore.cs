using System.Collections.Generic;
using Workspace1C.Models;

namespace Workspace1C.Services;

/// <summary>
/// Хранилище пользовательских программ группы «Мои программы» (issue #91):
/// единый читаемый JSON-файл <c>custom_programs.json</c> в каталоге данных профиля
/// (рядом с <c>settings.json</c>), по образцу <see cref="ICustomActionsStore"/>.
/// Файл можно править вручную и переносить между установками. Запись — атомарная
/// (временный файл + замена), JSON с отступами и читаемой кириллицей.
/// </summary>
public interface ICustomProgramsStore
{
    /// <summary>Полный путь к файлу списка программ.</summary>
    string FilePath { get; }

    /// <summary>
    /// Загружает все программы. Битый/нечитаемый файл не роняет загрузку: возвращается
    /// пустой список, сам файл на диске не трогается.
    /// </summary>
    IReadOnlyList<CustomProgram> LoadAll();

    /// <summary>Сохраняет одну программу (замена по Id либо добавление в конец).</summary>
    void Save(CustomProgram program);

    /// <summary>Сохраняет весь список программ целиком (порядок, перемещение, удаление).</summary>
    void SaveAll(IEnumerable<CustomProgram> programs);

    /// <summary>Удаляет программу по Id (no-op, если записи нет).</summary>
    void Delete(string id);

    /// <summary>Возвращает программу по Id либо <c>null</c>.</summary>
    CustomProgram? Get(string id);
}
