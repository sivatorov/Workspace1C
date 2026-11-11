using Xunit;

namespace Workspace1C.Tests;

/// <summary>Имя общей коллекции тестов статического состояния OneCCacheCleaner.</summary>
public static class OneCCacheCleanerStaticState
{
    public const string Name = "OneCCacheCleanerStaticState";
}

/// <summary>
/// Все тесты, работающие со статическим состоянием <see cref="Workspace1C.Services.OneCCacheCleaner"/>
/// (SetAdditionalRoots — глобальные дополнительные корни кэша, кэш реестровых корней на сеанс),
/// исполняются последовательно в одной коллекции. Параллельный доступ разных классов
/// к общим корням кэша давал флакующие падения (roots одного теста попадали в другой).
/// </summary>
[CollectionDefinition(OneCCacheCleanerStaticState.Name)]
public sealed class OneCCacheCleanerStaticStateCollection
{
}
