using System;
using System.Collections.Generic;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Тесты хелпера <see cref="UiExceptionGuard"/> (issue #96): перехват исключений
/// UI-обработчиков, полная запись в журнал (обработчик + стек + контекст) и
/// анти-спам повторных диалогов той же ошибки.
/// </summary>
public sealed class UiExceptionGuardTests : IDisposable
{
    private sealed class FakeLogger : IAppLogger
    {
        public List<string> Messages { get; } = new();
        public void Info(string message) { /* не используется guard'ом */ }
        public void Warn(string message) { /* не используется guard'ом */ }
        public void Error(string message, Exception? exception = null) => Messages.Add(message);
    }

    public UiExceptionGuardTests()
    {
        UiExceptionGuard.ResetForTests();
        // Тесты не должны писать в реальный errors.log каталога данных пользователя.
        UiExceptionGuard.ErrorsLogPathProvider = () => null;
    }

    public void Dispose()
    {
        UiExceptionGuard.ResetForTests();
    }

    // ======================= Логирование =======================

    [Fact]
    public void Guard_BodyThrows_DoesNotRethrowAndLogsHandlerAndContext()
    {
        var logger = new FakeLogger();
        var dialogs = new List<(string Message, string Title)>();
        UiExceptionGuard.LoggerProvider = () => logger;
        UiExceptionGuard.DialogShower = (message, title) => dialogs.Add((message, title));

        var ex = new InvalidOperationException("recycled container");

        // Исключение НЕ пробрасывается наружу.
        UiExceptionGuard.Guard(
            "OnInfobaseTree_PreviewMouseLeftButtonDown",
            () => throw ex,
            () => "target=base-1, ClickCount=1");

        // В журнал: имя обработчика, контекст, тип, сообщение и полный стек.
        Assert.Single(logger.Messages);
        var record = logger.Messages[0];
        Assert.Contains("OnInfobaseTree_PreviewMouseLeftButtonDown", record);
        Assert.Contains("target=base-1", record);
        Assert.Contains("ClickCount=1", record);
        Assert.Contains(typeof(InvalidOperationException).FullName!, record);
        Assert.Contains("recycled container", record);
        Assert.Contains(ex.StackTrace ?? string.Empty, record);

        // Диалог показан с содержательным текстом (не голым «Ошибка интерфейса»).
        var dialog = Assert.Single(dialogs);
        Assert.Contains(typeof(InvalidOperationException).FullName!, dialog.Message);
        Assert.Contains("recycled container", dialog.Message);
        Assert.Contains("OnInfobaseTree_PreviewMouseLeftButtonDown", dialog.Message);
    }

    [Fact]
    public void Guard_NoException_DoesNotLogOrShowDialog()
    {
        var logger = new FakeLogger();
        var dialogs = new List<(string Message, string Title)>();
        UiExceptionGuard.LoggerProvider = () => logger;
        UiExceptionGuard.DialogShower = (_, _) => dialogs.Add(("", ""));

        UiExceptionGuard.Guard("ApplySelection", () => { });

        Assert.Empty(logger.Messages);
        Assert.Empty(dialogs);
    }

    [Fact]
    public void Guard_EveryOccurrenceIsLogged_AntiSpamSilencesOnlyDialogs()
    {
        var logger = new FakeLogger();
        var dialogs = 0;
        UiExceptionGuard.LoggerProvider = () => logger;
        UiExceptionGuard.DialogShower = (_, _) => dialogs++;
        var tick = 0L;
        UiExceptionGuard.TickProvider = () => tick;

        // Три одинаковые ошибки подряд в пределах окна анти-спама.
        for (var i = 0; i < 3; i++)
        {
            UiExceptionGuard.Guard("SelectRow", () =>
                throw new NullReferenceException("detached row"));
        }

        // Все три случая в журнале — анти-спам глушит только диалоги.
        Assert.Equal(3, logger.Messages.Count);
        // Диалог — один.
        Assert.Equal(1, dialogs);
    }

    [Fact]
    public void Guard_DifferentException_ShowsSecondDialog()
    {
        var dialogs = 0;
        UiExceptionGuard.LoggerProvider = () => new FakeLogger();
        UiExceptionGuard.DialogShower = (_, _) => dialogs++;
        var tick = 0L;
        UiExceptionGuard.TickProvider = () => tick;

        UiExceptionGuard.Guard("SelectRow", () => throw new NullReferenceException("a"));
        UiExceptionGuard.Guard("SelectRow", () => throw new InvalidOperationException("b"));

        Assert.Equal(2, dialogs);
    }

    [Fact]
    public void Guard_SameErrorAfterAntiSpamWindow_ShowsSecondDialog()
    {
        var dialogs = 0;
        UiExceptionGuard.LoggerProvider = () => new FakeLogger();
        UiExceptionGuard.DialogShower = (_, _) => dialogs++;
        var tick = 0L;
        UiExceptionGuard.TickProvider = () => tick;

        UiExceptionGuard.Guard("SelectRow", () => throw new NullReferenceException("a"));

        tick = UiExceptionGuard.DialogRepeatWindowMs + 1;
        UiExceptionGuard.Guard("SelectRow", () => throw new NullReferenceException("a"));

        Assert.Equal(2, dialogs);
    }

    // ======================= Устойчивость диагностики =======================

    [Fact]
    public void Guard_LoggerUnavailable_StillShowsDialogAndDoesNotThrow()
    {
        var dialogs = 0;
        UiExceptionGuard.LoggerProvider = () => null;
        UiExceptionGuard.DialogShower = (_, _) => dialogs++;

        UiExceptionGuard.Guard("SelectRow", () => throw new NullReferenceException("no logger"));

        Assert.Equal(1, dialogs);
    }

    [Fact]
    public void Guard_LoggerThrows_StillShowsDialogAndDoesNotThrow()
    {
        var dialogs = 0;
        UiExceptionGuard.LoggerProvider = () => throw new IOException("disk full");
        UiExceptionGuard.DialogShower = (_, _) => dialogs++;

        UiExceptionGuard.Guard("SelectRow", () => throw new NullReferenceException("a"));

        Assert.Equal(1, dialogs);
    }

    [Fact]
    public void Guard_ContextProviderThrows_ExceptionIsNotMasked()
    {
        var logger = new FakeLogger();
        var dialogs = 0;
        UiExceptionGuard.LoggerProvider = () => logger;
        UiExceptionGuard.DialogShower = (_, _) => dialogs++;

        UiExceptionGuard.Guard(
            "SelectRow",
            () => throw new NullReferenceException("a"),
            () => throw new InvalidOperationException("context failed"));

        // Исключение контекста не маскирует исходную ошибку: guard отработал штатно.
        Assert.Single(logger.Messages);
        Assert.Equal(1, dialogs);
    }

    [Fact]
    public void Report_NullException_DoesNothing()
    {
        var dialogs = 0;
        UiExceptionGuard.LoggerProvider = () => new FakeLogger();
        UiExceptionGuard.DialogShower = (_, _) => dialogs++;

        UiExceptionGuard.Report("SelectRow", null!);

        Assert.Equal(0, dialogs);
    }

    // ======================= Запись журнала (полнота, issue #96) =======================

    [Fact]
    public void Report_LogRecordContainsVersionAndOs()
    {
        var logger = new FakeLogger();
        UiExceptionGuard.LoggerProvider = () => logger;

        UiExceptionGuard.Report("SelectRow", new NullReferenceException("a"), "target=base-9");

        var record = logger.Messages[0];
        // Версия приложения (VersionInfo.Display) и описание ОС — обязательные поля записи.
        Assert.Contains(VersionInfo.Display(), record);
        Assert.Contains("ОС:", record);
        // Ключи локализации не «протекают» в запись журнала (T-фоллбек недопустим).
        Assert.DoesNotContain("App.Fatal.", record);
    }
}

/// <summary>
/// Чистая логика разворота строки дерева до реальной базы (по образцу фикса #97):
/// <see cref="BatchSelectionHelper.Unwrap"/> и <see cref="BatchSelectionHelper.IsPinnedSection"/>
/// — единая точка для обеих платформ (WPF UnwrapInfobase и Avalonia ResolveRowBase
/// делегируют сюда). Регресс-сетка на типы данных строк (#97/#301/#314).
/// </summary>
public sealed class RowResolverTests
{
    [Fact]
    public void Unwrap_Infobase_ReturnsSameInstance()
    {
        var ib = new Infobase { Id = "regular-1" };

        Assert.True(ReferenceEquals(ib, BatchSelectionHelper.Unwrap(ib)));
    }

    [Fact]
    public void Unwrap_PinnedInfobaseItem_ReturnsWrappedBase()
    {
        var ib = new Infobase { Id = "pinned-1" };
        var wrapped = new PinnedInfobaseItem(ib);

        // Разворот обёртки узла «Закреплённые» до реальной базы (#97).
        Assert.True(ReferenceEquals(ib, BatchSelectionHelper.Unwrap(wrapped)));
    }

    [Fact]
    public void Unwrap_GroupNodeViewModel_ReturnsNull()
    {
        var group = new Workspace1C.ViewModels.GroupNodeViewModel(
            new Workspace1C.Models.Group { Id = "g1", Name = "Группа" });

        Assert.Null(BatchSelectionHelper.Unwrap(group));
    }

    [Fact]
    public void Unwrap_Null_ReturnsNull()
    {
        Assert.Null(BatchSelectionHelper.Unwrap(null));
    }

    [Fact]
    public void Unwrap_ForeignType_ReturnsNull()
    {
        // Чужой тип данных строки (осиротевший/служебный контейнер) — не база.
        Assert.Null(BatchSelectionHelper.Unwrap("not an infobase"));
        Assert.Null(BatchSelectionHelper.Unwrap(42));
    }

    // ======================= IsPinnedSection (issue #326) =======================

    [Fact]
    public void IsPinnedSection_PinnedInfobaseItem_True()
    {
        Assert.True(BatchSelectionHelper.IsPinnedSection(
            new PinnedInfobaseItem(new Infobase { Id = "pinned-1" })));
    }

    [Fact]
    public void IsPinnedSection_RegularRow_False()
    {
        Assert.False(BatchSelectionHelper.IsPinnedSection(new Infobase { Id = "regular-1" }));
        Assert.False(BatchSelectionHelper.IsPinnedSection(null));
        Assert.False(BatchSelectionHelper.IsPinnedSection(new Workspace1C.ViewModels.GroupNodeViewModel(null)));
    }

    // ======================= IsContainerForBase (issue #96) =======================

    [Fact]
    public void IsContainerForBase_ContainerStillShowsTarget_True()
    {
        var ib = new Infobase { Id = "base-1" };
        Assert.True(BatchSelectionHelper.IsContainerForBase(ib, ib));
        Assert.True(BatchSelectionHelper.IsContainerForBase(new PinnedInfobaseItem(ib), ib));
    }

    [Fact]
    public void IsContainerForBase_ContainerRecycled_False()
    {
        // Контейнер переиспущен виртуализацией и показывает ДРУГУЮ строку (#96).
        var target = new Infobase { Id = "base-1" };
        var other = new Infobase { Id = "base-2" };

        Assert.False(BatchSelectionHelper.IsContainerForBase(other, target));
        Assert.False(BatchSelectionHelper.IsContainerForBase(new PinnedInfobaseItem(other), target));
    }

    [Fact]
    public void IsContainerForBase_EmptyOrOrphanedContainer_False()
    {
        var target = new Infobase { Id = "base-1" };

        // DataContext отсутствует (контейнер осиротел) или нестринговый тип.
        Assert.False(BatchSelectionHelper.IsContainerForBase(null, target));
        Assert.False(BatchSelectionHelper.IsContainerForBase("foreign", target));
        // Цель null — проверка всегда false (подсвечивать нечего).
        Assert.False(BatchSelectionHelper.IsContainerForBase(target, null));
    }

    [Fact]
    public void IsContainerForBase_EqualButNotSameReference_False()
    {
        // Проверка по ССЫЛКЕ: модель строки — тот же экземпляр, что и цель (#96);
        // скриншот данных бессмыслен, иначе подсветка ушла бы на копию.
        var target = new Infobase { Id = "base-1" };
        var copy = new Infobase { Id = "base-1" };

        Assert.False(BatchSelectionHelper.IsContainerForBase(copy, target));
    }
}
