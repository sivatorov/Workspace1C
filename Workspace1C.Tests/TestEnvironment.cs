using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Коллекция xUnit с отключённой параллельностью для тестов, разделяющих ГЛОБАЛЬНОЕ
/// состояние <see cref="Workspace1C.Services.TraceFlags"/> (ConfigDirectoryOverride,
/// EnvReader, кэш). TraceFlagsTests переключают каталог/окружение на время каждого теста,
/// а тесты входа портала (OneCUpdatesLoginFlow) зависят от INFO-диагностики CM_REDIRECT —
/// с 0.3.9.316 явный false в trace.json выключает флаг ВСЕГДА, поэтому параллельное
/// изменение каталога конфига ломало бы их. Классы коллекции выполняются последовательно.
/// </summary>
[CollectionDefinition("TraceFlagsState", DisableParallelization = true)]
public sealed class TraceFlagsStateCollection { }

/// <summary>
/// Коллекция с отключённой параллельностью для тестов, разделяющих ГЛОБАЛЬНОЕ
/// состояние подавления модальных окон (<see cref="Workspace1C.Services.UiDialogs"/>)
/// и подмен <see cref="Workspace1C.Services.UiExceptionGuard"/>. Тесты этой коллекции
/// выполняются последовательно относительно остальных коллекций — иначе подмена
/// DialogShower/флага в одном тесте влияла бы на проверки другого.
/// </summary>
[CollectionDefinition("UiDialogsState", DisableParallelization = true)]
public sealed class UiDialogsStateCollection { }

/// <summary>
/// Коллекция xUnit с отключённой параллельностью для тестов, разделяющих ГЛОБАЛЬНОЕ
/// состояние <see cref="Workspace1C.Services.FeatureDataStore.DirectoryOverride"/>:
/// классы, которые на время теста подменяют каталог данных профиля (FeatureDataStore),
/// выполняются последовательно — иначе чужие подмены каталога перемежаются между
/// «сохранить» и «прочитать», и JSON-хранилище читается/пишется в чужих каталогах
/// (тест вида «Save → Load» получает значение по умолчанию).
/// </summary>
[CollectionDefinition("LocalDataStoreState", DisableParallelization = true)]
public sealed class LocalDataStoreStateCollection { }

/// <summary>
/// Коллекция с отключённой параллельностью для тестов, подменяющих статический
/// <see cref="Workspace1C.Services.ScriptLaunchContextResolver.ResolverOverride"/>.
/// Без сериализации один тест сбрасывает override другого, и резолвер начинает
/// обращаться к реальной платформе/профилю (падение или пустой контекст).
/// </summary>
[CollectionDefinition("ScriptLaunchContextState", DisableParallelization = true)]
public sealed class ScriptLaunchContextStateCollection { }

/// <summary>
/// Коллекция статиков общих авторизаций (<see cref="OneCLauncher.CommonAuthorization"/>,
/// CommonEnterpriseAuthorization/CommonConfiguratorAuthorization): выполняется БЕЗ
/// параллели с другими коллекциями.
/// <para>
/// Причина: <see cref="OneCLauncher.BuildArguments"/> вызывает
/// <see cref="OneCLauncher.EnsureCommonAuthLoaded"/>, который при первом вызове и
/// доступных сервисах читает настройки профиля и перезаписывает статики. Если
/// параллельно другой тест сконфигурировал AppServices (например,
/// <c>Issue79TestDeployWindowConstructionTests</c>), значение, выставленное тестом
/// аргументов, затирается — тест падал редко и в разных прогонах.
/// </para>
/// <para>
/// Фикстура заранее помечает общие авторизации загруженными
/// (<see cref="OneCLauncher.MarkCommonAuthLoaded"/>), поэтому ленивая загрузка из
/// профиля в тестах не выполняется вовсе — используются значения, выставленные
/// тестом, и результат детерминирован.
/// </para>
/// </summary>
[CollectionDefinition("CommonAuthStatics", DisableParallelization = true)]
public sealed class CommonAuthStaticsCollection : ICollectionFixture<CommonAuthStaticsFixture> { }

/// <summary>См. <see cref="CommonAuthStaticsCollection"/>.</summary>
public sealed class CommonAuthStaticsFixture
{
    public CommonAuthStaticsFixture() => OneCLauncher.MarkCommonAuthLoaded();
}

/// <summary>
/// Глобальная подготовка тестового процесса (выполняется один раз при загрузке сборки,
/// до всех тестов). INFO-диагностика редиректов/входа портала 1С (issue #347, флаг
/// CM_REDIRECT в trace.json) нужна тестам входа (<see cref="OneCUpdatesLoginFlowTests"/>)
/// для проверки записей в журнале.
/// Каталог конфига флагов изолируется в отдельный временный каталог тестового процесса,
/// чтобы тесты не зависели от пользовательского trace.json в системном каталоге данных.
/// В этот каталог СРАЗУ пишется конфиг с явным <c>"CM_REDIRECT": true</c>: новая семантика
/// гейта (0.3.9.316) трактует явное false как «выключено ВСЕГДА», а любой тест, который
/// первым вызовет TraceFlags, иначе создал бы файл с дефолтным false и «закрыл» бы
/// диагностику для последующих тестов входа после сброса кэша. env CM_REDIRECT=1 остаётся
/// как страховка (запасной способ включения).
/// </summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Init()
    {
        // Тестовый прогон не должен показывать РЕАЛЬНЫЕ модальные окна.
        // Причина: часть тестов выполняет боевой код (AsyncRelayCommand,
        // UiExceptionGuard, ViewModel-ы с AppServices после Configure) и при
        // искусственном исключении («boom», «ошибка после await») вызывала
        // WpfDialogService → material-окно падало на MTA-потоке xUnit, а резервный
        // MessageBox открывался по-настоящему и блокировал прогон до нажатия кнопки.
        // Заглушки, переданные явно (RecordingDialogs, UiExceptionGuard.DialogShower),
        // продолжают работать — тесты показа диалогов остаются валидными.
        UiDialogs.SuppressModalDialogs = true;

        try { Environment.SetEnvironmentVariable("CM_REDIRECT", "1"); }
        catch { /* env недоступен — часть тестов входа потеряет INFO-записи */ }

        // Тестовые ERROR/WARN не должны попадать в пользовательский
        // %APPDATA%\Workspace1C\logs\app-YYYYMMDD.log: такие записи («имитация»,
        // «boom») путают диагностику реальных сбоев. Журнал тестового процесса
        // уводится во временный каталог.
        try
        {
            var logDir = Path.Combine(Path.GetTempPath(), $"cm_tests_logs_{Guid.NewGuid():N}");
            Directory.CreateDirectory(logDir);
            FileAppLogger.DirectoryOverride = logDir;
        }
        catch
        {
            // Временный каталог не создался — тесты продолжат писать в штатный журнал.
        }

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), $"cm_traceflags_testenv_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            // Явно включаем CM_REDIRECT в тестовом конфиге (см. комментарий класса) —
            // формат совместим с TraceFlagsFormat.Serialize (стабильный порядок ключей).
            File.WriteAllText(
                Path.Combine(dir, TraceFlagsFormat.ConfigFileName),
                TraceFlagsFormat.Serialize(new Dictionary<string, bool>
                {
                    [TraceFlags.RedirectFlag] = true
                }));
            TraceFlags.ConfigDirectoryOverride = dir;
        }
        catch
        {
            // Каталог не создался — тесты входа продолжат работу со старым поведением
            // (чтение из платформенного каталога), возможна зависимость от пользовательского конфига.
        }
    }
}