using System;
using System.Collections.Generic;
using Workspace1C.Services;
using Xunit;

namespace Workspace1C.Tests;

/// <summary>
/// Регрессия «модальные окна во время прогона тестов»: боевой код (AsyncRelayCommand,
/// UiExceptionGuard, ViewModel-ы после AppServices.Configure) при искусственных
/// исключениях не должен открывать реальные MessageBox. Подавление включается один раз
/// в <see cref="TestEnvironment.Init"/>; явно переданные заглушки диалогов продолжают
/// работать, чтобы тесты показа ошибок оставались валидными.
/// </summary>
[Collection("UiDialogsState")]
public sealed class UiDialogsSuppressionTests : IDisposable
{
    private readonly bool _suppressBefore;

    public UiDialogsSuppressionTests()
    {
        _suppressBefore = UiDialogs.SuppressModalDialogs;
        UiExceptionGuard.ResetForTests();
        UiExceptionGuard.ErrorsLogPathProvider = () => null;
    }

    public void Dispose()
    {
        UiExceptionGuard.ResetForTests();
        UiDialogs.SuppressModalDialogs = _suppressBefore;
    }

    private sealed class FakeLogger : IAppLogger
    {
        public List<string> Messages { get; } = new();
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) => Messages.Add(message);
    }

    [Fact]
    public void TestProcess_SuppressesRealModalDialogs()
    {
        // ModuleInitializer тестовой сборки обязан выставить флаг до первого теста:
        // иначе прогон снова начнёт открывать реальные окна.
        Assert.True(UiDialogs.SuppressModalDialogs);
    }

    [Fact]
    public void Guard_NoDialogShower_DoesNotOpenRealDialog_WhenSuppressed()
    {
        UiDialogs.SuppressModalDialogs = true;
        var logger = new FakeLogger();
        UiExceptionGuard.LoggerProvider = () => logger;
        UiExceptionGuard.DialogShower = null;

        // Не должно ни бросить, ни уйти в AppServices/MessageBox: журнал ведётся,
        // диалог подавлен.
        UiExceptionGuard.Report(
            "ApplySelection",
            new InvalidOperationException("тестовая ошибка"),
            "target=base-1");

        Assert.Single(logger.Messages);
        Assert.Contains("тестовая ошибка", logger.Messages[0]);
    }

    [Fact]
    public void Guard_ExplicitDialogShower_StillCalled_WhenSuppressed()
    {
        // Явная заглушка имеет приоритет над подавлением: тесты, проверяющие
        // содержимое диалога, не должны зависеть от тестового режима.
        UiDialogs.SuppressModalDialogs = true;
        var dialogs = new List<(string Message, string Title)>();
        UiExceptionGuard.LoggerProvider = () => new FakeLogger();
        UiExceptionGuard.DialogShower = (message, title) => dialogs.Add((message, title));

        UiExceptionGuard.Report("ApplySelection", new InvalidOperationException("recycled"));

        var dialog = Assert.Single(dialogs);
        Assert.Contains("recycled", dialog.Message);
    }
}
