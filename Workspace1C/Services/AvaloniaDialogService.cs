#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Workspace1C.Localization;

namespace Workspace1C.Services
{
    /// <summary>
    /// Avalonia-версия диалогов (Linux). Реализует <see cref="IDialogService"/> через
    /// собственное модальное окно сообщения (Avalonia не имеет MessageBox из коробки).
    /// Методы интерфейса синхронные, поэтому модальный показ выполняется с помощью
    /// вложенного цикла сообщений <see cref="Dispatcher.PushFrame"/>.
    /// </summary>
    public sealed class AvaloniaDialogService : IDialogService
    {
        public void ShowInfo(string message, string title = "")
            => ShowMessage(message, DefaultTitle(title, "Common.Information"), MessageWindowKind.Info);

        public void ShowWarning(string message, string title = "")
            => ShowMessage(message, DefaultTitle(title, "Common.Warning"), MessageWindowKind.Warning);

        public void ShowError(string message, string title = "")
            => ShowMessage(message, DefaultTitle(title, "Common.Error"), MessageWindowKind.Error);

        public bool Confirm(string message, string title = "")
        {
            var win = new MaterialMessageWindowAvalonia(message, DefaultTitle(title, "Common.Confirm"), MaterialMessageKind.Question);
            return ShowModalSync(win);
        }

        /// <summary>
        /// Запрашивает подтверждение с показом описания релиза «Что нового» в формате
        /// markdown (заголовки, жирный, списки). Используется автообновлением на Linux.
        /// </summary>
        /// <param name="summary">Краткий текст вопроса (версии и приглашение скачать).</param>
        /// <param name="markdownBody">Описание релиза в markdown; <c>null</c> скрывает секцию.</param>
        /// <param name="title">Заголовок окна.</param>
        /// <returns>True, если пользователь подтвердил действие.</returns>
        public bool ConfirmUpdate(string summary, string? markdownBody, string title = "")
        {
            var win = new MaterialMessageWindowAvalonia(
                summary, DefaultTitle(title, "Common.Confirm"), MaterialMessageKind.Question, markdownBody);
            return ShowModalSync(win);
        }

        /// <summary>
        /// Как <see cref="ConfirmUpdate"/>, но с третьей кнопкой «Пропустить эту версию»
        /// (issue #116): пользователь может отказаться от этой версии — она больше не
        /// будет предлагаться, пока не выйдет более новая.
        /// </summary>
        /// <param name="summary">Краткий текст вопроса (версии и приглашение скачать).</param>
        /// <param name="markdownBody">Описание релиза в markdown; <c>null</c> скрывает секцию.</param>
        /// <param name="skipButtonText">Локализованный текст кнопки пропуска.</param>
        /// <param name="title">Заголовок окна.</param>
        /// <returns>Кнопка, которой пользователь ответил (Esc/крестик — Cancelled).</returns>
        public UpdateDialogResult ConfirmUpdateWithSkip(
            string summary, string? markdownBody, string skipButtonText, string title = "")
        {
            var win = new MaterialMessageWindowAvalonia(
                summary, DefaultTitle(title, "Common.Confirm"), MaterialMessageKind.Question,
                markdownBody, skipButtonText);
            ShowModalSync(win);
            return win.SkipClicked
                ? UpdateDialogResult.Skipped
                : win.Confirmed ? UpdateDialogResult.Confirmed : UpdateDialogResult.Cancelled;
        }

        /// <summary>
        /// Показывает модальное окно с пояснением и кликабельной ссылкой на страницу
        /// выпуска. Используется автообновлением, когда самообновление недоступно
        /// (запуск из пакета AppImage или установка в системный каталог) — вместо
        /// обычного сообщения с текстом ссылки показывается гиперссылка, открывающая
        /// страницу в браузере по умолчанию (issue #225).
        /// </summary>
        /// <param name="message">Пояснение, почему самообновление недоступно.</param>
        /// <param name="linkUrl">Адрес страницы выпуска GitHub. Пустая строка скрывает ссылку.</param>
        /// <param name="title">Заголовок окна.</param>
        public void ShowManualUpdate(string message, string linkUrl, string title = "")
        {
            var win = new ManualUpdateWindowAvalonia(
                message, linkUrl, DefaultTitle(title, "Update.NewVersionAvailable"));
            ShowModalSync(win);
        }

        public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null)
            => RunSync(() => PickFileAsync(DefaultTitle(title, "Dialog.OpenFile"), filter, initialDirectory));

        public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null)
            => RunSync(() => PickSaveFileAsync(DefaultTitle(title, "Dialog.SaveFile"), defaultFileName, filter, initialDirectory));

        public string? OpenFolderDialog(string title = "", string? initialDirectory = null)
            => RunSync(() => PickFolderAsync(DefaultTitle(title, "Dialog.SelectFolder"), initialDirectory));

        /// <summary>Подставляет локализованный заголовок по умолчанию, если переданный пуст.</summary>
        private static string DefaultTitle(string title, string fallbackKey) =>
            string.IsNullOrWhiteSpace(title) ? LocalizationManager.T(fallbackKey) : title;

        // ---- Файловые диалоги через Avalonia StorageProvider ----

        private async Task<string?> PickFileAsync(string title, string filter, string? initialDirectory)
        {
            var provider = CurrentStorageProvider();
            if (provider is null)
                return null;

            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = BuildFileTypes(filter)
            };
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                options.SuggestedStartLocation = await TryGetFolder(provider, initialDirectory);

            var files = await provider.OpenFilePickerAsync(options);
            var file = files.FirstOrDefault();
            return file?.TryGetLocalPath();
        }

        private async Task<string?> PickSaveFileAsync(string title, string defaultFileName, string filter, string? initialDirectory)
        {
            var provider = CurrentStorageProvider();
            if (provider is null)
                return null;

            var options = new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = string.IsNullOrWhiteSpace(defaultFileName) ? "file" : defaultFileName,
                FileTypeChoices = BuildFileTypes(filter)
            };

            // Расширение берётся из предложенного имени: без него имя, введённое
            // пользователем без точки, сохранится без расширения, тогда как
            // в версии для Windows диалог его дописывает сам.
            var suggestedExtension = Path.GetExtension(options.SuggestedFileName);
            if (!string.IsNullOrWhiteSpace(suggestedExtension))
                options.DefaultExtension = suggestedExtension.TrimStart('.');
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                options.SuggestedStartLocation = await TryGetFolder(provider, initialDirectory);

            var file = await provider.SaveFilePickerAsync(options);
            return file?.TryGetLocalPath();
        }

        private async Task<string?> PickFolderAsync(string title, string? initialDirectory)
        {
            var provider = CurrentStorageProvider();
            if (provider is null)
                return null;

            var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
            if (!string.IsNullOrWhiteSpace(initialDirectory))
                options.SuggestedStartLocation = await TryGetFolder(provider, initialDirectory);

            var folders = await provider.OpenFolderPickerAsync(options);
            var folder = folders.FirstOrDefault();
            return folder?.TryGetLocalPath();
        }

        private static IStorageProvider? CurrentStorageProvider()
        {
            if (CurrentOwner() is TopLevel topLevel)
                return topLevel.StorageProvider;
            // Спрятанное окно не годится и здесь: у невидимого окна нет
            // площадки, а файловый диалог просят и при работе из трея.
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { IsVisible: true } main)
                return TopLevel.GetTopLevel(main)?.StorageProvider;
            return null;
        }

        private static async Task<IStorageFolder?> TryGetFolder(IStorageProvider provider, string path)
        {
            try { return await provider.TryGetFolderFromPathAsync(path); }
            catch { return null; }
        }

        /// <summary>
        /// Разбирает фильтр вида "Конфигурация (*.cf)|*.cf|Все файлы (*.*)|*.*" в список
        /// типов для StorageProvider. Пустое значение → все файлы.
        /// </summary>
        private static IReadOnlyList<FilePickerFileType> BuildFileTypes(string filter)
        {
            var result = new List<FilePickerFileType>();
            if (string.IsNullOrWhiteSpace(filter))
            {
                result.Add(FilePickerFileTypes.All);
                return result;
            }

            var parts = filter.Split('|');
            for (var i = 0; i + 1 < parts.Length; i += 2)
            {
                var label = parts[i].Trim();
                var patterns = parts[i + 1]
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                // Для фильтра «все файлы» (*.*) показываем локализованное название,
                // сам паттерн (расширения) не трогаем.
                if (patterns.Any(p => p.Trim() == "*.*"))
                    label = LocalizationManager.T("Common.AllFiles");
                result.Add(new FilePickerFileType(label) { Patterns = patterns });
            }
            if (result.Count == 0)
                result.Add(FilePickerFileTypes.All);
            return result;
        }

        /// <summary>
        /// Выполняет асинхронную операцию StorageProvider синхронно, попутно обрабатывая
        /// очередь задач Avalonia (аналогично <see cref="ShowModalSync"/>), чтобы не было
        /// взаимоблокировки на UI-потоке.
        /// </summary>
        private static T RunSync<T>(Func<Task<T>> taskFactory)
        {
            var task = taskFactory();
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false,
                    TaskScheduler.FromCurrentSynchronizationContext());
                try
                {
                    Dispatcher.UIThread.PushFrame(frame);
                }
                finally
                {
                    // Снимаем кадр при любом исходе, чтобы диалоговая очередь не зависла,
                    // если задача завершилась с ошибкой до того, как продолжится цикл.
                    frame.Continue = false;
                }
            }
            return task.GetAwaiter().GetResult();
        }

        private static void ShowMessage(string message, string title, MessageWindowKind kind)
            => ShowModalSync(new MaterialMessageWindowAvalonia(message, title, (MaterialMessageKind)kind));

        /// <summary>
        /// Показывает окно модально и блокирует вызывающий поток до его закрытия,
        /// попутно обрабатывая очередь задач Avalonia (эмуляция синхронного ShowDialog).
        /// </summary>
        private static bool ShowModalSync(Window window)
        {
            var owner = CurrentOwner();
            bool result = true;
            // Кадр снимается и по скрытию окна, а не только по закрытию: Window.Hide()
            // события Closed не даёт, а прячет окно вместе с дочерними. Пока диалог
            // открыт, такое скрытие приходит со стороны: базу запускают из меню трея,
            // и при настройке «после запуска уйти в трей» главное окно прячется вместе
            // с диалогом. Раньше кадр в этом случае крутился бы вечно, и приложение
            // замирало целиком.
            DispatcherFrame frame = new();
            var opened = false;
            var hiddenWithoutClose = false;
            var closed = false;

            void StopFrame() => frame.Continue = false;
            void OnOpened(object? sender, EventArgs e) => opened = true;
            void OnClosed(object? sender, EventArgs e)
            {
                closed = true;
                result = window is MaterialMessageWindowAvalonia mw ? mw.Confirmed : true;
                StopFrame();
            }

            void OnVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property != Visual.IsVisibleProperty || !Equals(e.NewValue, false) || closed)
                    return;

                // Окно спрятали со стороны, ответа пользователь не давал. Возвращаем
                // отказ: положительный ответ здесь означал бы согласие на удаление базы
                // или снятие сеансов, которого никто не подтверждал. Скрытое окно потом
                // закрывается, иначе оно остаётся в списке окон приложения и держит
                // процесс живым при ShutdownMode.OnLastWindowClose.
                result = false;
                hiddenWithoutClose = true;
                StopFrame();
            }

            window.Opened += OnOpened;
            window.Closed += OnClosed;
            window.PropertyChanged += OnVisibilityChanged;

            // Владелец пригоден только видимый и с измеренной геометрией. На Linux/X11
            // модальный показ относительно неотрисованного владельца (нулевая геометрия)
            // и центрирование по нему способны вызывать нативный abort при открытии
            // диалога (issue #168). С непригодным владельцем диалог открывается по
            // центру экрана и без привязки модальности к окну.
            var validOwner = owner is { IsVisible: true } o && HasUsableBounds(o);

            try
            {
                if (validOwner)
                {
                    // validOwner гарантирует ненулевого владельца; оператор ! — только
                    // для анализатора nullability, чтобы не давать ложное предупреждение.
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    _ = window.ShowDialog(owner!);
                }
                else
                {
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    window.Show();
                }

                Dispatcher.UIThread.PushFrame(frame);
            }
            catch
            {
                // Запасной путь показа: простой немодальный показ по центру экрана.
                // Нативный сбой Avalonia при открытии диалога не должен оставлять окно
                // неоткрытым и гасить вложенный цикл сообщений (issue #168: «не падает,
                // но и не открывается»).
                frame.Continue = false;
                try
                {
                    // Запасному показу нужен свой кадр: прежний уже остановлен, и
                    // PushFrame на нём вернулся бы сразу, не дождавшись ответа.
                    frame = new DispatcherFrame();
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    if (!window.IsVisible)
                        window.Show();

                    // Ждать имеет смысл только когда окно действительно открылось:
                    // IsVisible выставляется до разметки содержимого, и окно, упавшее
                    // на разметке, ожиданием уже не дождаться.
                    if (opened)
                        Dispatcher.UIThread.PushFrame(frame);
                }
                catch
                {
                    frame.Continue = false;
                    try { if (window.IsVisible) window.Hide(); } catch { /* ignore */ }
                }
            }
            finally
            {
                // Снимаем кадр при любом исходе и прячем неоткрытое окно: сбой показа
                // не должен оставлять висящий вложенный цикл сообщений.
                frame.Continue = false;
                try { if (window.IsVisible) window.Hide(); } catch { /* ignore */ }

                if (hiddenWithoutClose && !closed)
                {
                    try { window.Close(); } catch { /* ignore */ }
                }

                window.Opened -= OnOpened;
                window.Closed -= OnClosed;
                window.PropertyChanged -= OnVisibilityChanged;
            }

            return result;
        }

        /// <summary>
        /// Признак того, что окно имеет измеренную ненулевую геометрию и пригодно
        /// в качестве владельца модального диалога. На Linux/X11 центрирование по
        /// владельцу с нулевым размером и показ диалога поверх него могут давать
        /// нативный abort при открытии окна (issue #168).
        /// </summary>
        private static bool HasUsableBounds(Window w)
        {
            try
            {
                if (w.Bounds.Width > 0 && w.Bounds.Height > 0)
                    return true;
            }
            catch
            {
                // Bounds может быть недоступен у ещё не показанного окна.
            }
            // Явно заданная ширина/высота тоже считается пригодной геометрией.
            return w.Width > 0 && w.Height > 0;
        }

        private static Window? CurrentOwner()
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return null;

            // Владельцем берём активное окно: диалог, вызванный из модального
            // окна настроек, иначе принадлежал бы уже заблокированному главному
            // окну, и модальность с фокусом на Linux вели бы себя странно.
            foreach (var window in desktop.Windows)
            {
                if (window.IsActive && window.IsVisible)
                    return window;
            }

            // Спрятанное в трей окно остаётся в списке, но владельцем быть
            // не может: показ диалога поверх невидимого окна роняет приложение.
            // Без владельца диалог открывается по центру экрана, это ниже.
            return desktop.MainWindow is { IsVisible: true } main ? main : null;
        }
    }

    internal enum MessageWindowKind { Info, Warning, Error, Question }

    /// <summary>Ответ в диалоге обновления с кнопкой пропуска (issue #116).</summary>
    // public, а не internal: используется как возвращаемый тип public-метода
    // AvaloniaDialogService.ConfirmUpdateWithSkip (CS0050 в Linux/Avalonia-сборке).
    public enum UpdateDialogResult
    {
        /// <summary>Пользователь согласился (кнопка «Да»).</summary>
        Confirmed,
        /// <summary>Пользователь пропустил версию (кнопка «Пропустить эту версию»).</summary>
        Skipped,
        /// <summary>Пользователь отказался (кнопка «Нет», Esc или крестик окна).</summary>
        Cancelled
    }
}
#endif