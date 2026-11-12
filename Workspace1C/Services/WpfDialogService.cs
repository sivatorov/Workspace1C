using System;
using System.Windows;
using Workspace1C.Localization;
using Microsoft.Win32;

namespace Workspace1C.Services;

/// <summary>
/// Реализация <see cref="IDialogService"/> в стиле Material Design.
/// Предупреждения, подтверждения и ошибки показываются через собственное
/// модальное окно <see cref="MaterialMessageWindow"/> (а не стандартный MessageBox),
/// чтобы единообразно выглядеть в обеих темах приложения.
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    public void ShowInfo(string message, string title = "") =>
        Show(message, title, MaterialMessageKind.Info);

    public void ShowWarning(string message, string title = "") =>
        Show(message, title, MaterialMessageKind.Warning);

    public void ShowError(string message, string title = "") =>
        Show(message, title, MaterialMessageKind.Error);

    public bool Confirm(string message, string title = "")
    {
        var resolvedTitle = DefaultTitle(title, "Common.Confirm");
        try
        {
            var win = new MaterialMessageWindow(message, resolvedTitle, MaterialMessageKind.Question);
            SetOwnerIfAvailable(win);
            win.ShowDialog();
            return win.Confirmed;
        }
        catch (Exception ex)
        {
            // issue #127: сбой показа material-диалога (Win32Exception 1816 «Недостаточно
            // квот для обработки команды», 1400 «Недопустимый дескриптор окна» — нехватка
            // desktop heap при долгой работе/многих окнах) не должен уводить процесс вниз.
            LogDialogFallback(resolvedTitle, ex);
            return TryFallbackMessageBox(message, resolvedTitle, MessageBoxButton.OKCancel,
                MessageBoxImage.Question, ex) == MessageBoxResult.OK;
        }
    }

    private static void Show(string message, string title, MaterialMessageKind kind)
    {
        var resolvedTitle = DefaultTitle(title, "Common.Information");
        try
        {
            var win = new MaterialMessageWindow(message, resolvedTitle, kind);
            SetOwnerIfAvailable(win);
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            // issue #127: сбой показа не должен уводить процесс вниз — fallback на
            // стандартный MessageBox (без собственного hwnd-рендера WPF, устойчивее
            // к нехватке desktop heap), при повторном сбое — только журнал.
            LogDialogFallback(resolvedTitle, ex);
            TryFallbackMessageBox(message, resolvedTitle, MessageBoxButton.OK,
                ToMessageBoxImage(kind), ex);
        }
    }

    /// <summary>
    /// Резервный показ сообщения через стандартный <see cref="System.Windows.MessageBox"/>
    /// после сбоя material-диалога. Повторный сбой не бросается наружу — он логируется,
    /// а метод возвращает <see cref="MessageBoxResult.None"/> (у вызывающего кода
    /// «пользователь не ответил»).
    /// </summary>
    private static MessageBoxResult TryFallbackMessageBox(
        string message, string title, MessageBoxButton buttons, MessageBoxImage image, Exception primary)
    {
        try
        {
            return System.Windows.MessageBox.Show(message, title, buttons, image);
        }
        catch (Exception fallback)
        {
            // Повторный сбой (issue #127): ни material-окно, ни MessageBox не показаны —
            // остаётся журнал; исключение наружу не бросается.
            LogDialogFailure(message, primary, fallback);
            return MessageBoxResult.None;
        }
    }

    /// <summary>Пишет предупреждение о переключении на резервный MessageBox (issue #127).</summary>
    private static void LogDialogFallback(string title, Exception ex)
    {
        try
        {
            AppServices.TryGetService<IAppLogger>()?.Warn(
                $"Диалог «{title}» не показан через MaterialMessageWindow " +
                $"({ex.GetType().Name}: {ex.Message}) — используется резервный MessageBox.");
        }
        catch
        {
            // Логирование не должно ронять операцию показа диалога.
        }
    }

    /// <summary>Пишет ошибку, когда не сработал и резервный MessageBox (issue #127).</summary>
    private static void LogDialogFailure(string message, Exception primary, Exception fallback)
    {
        try
        {
            AppServices.TryGetService<IAppLogger>()?.Error(
                $"Диалог «{message}» не удалось показать: MaterialMessageWindow — " +
                $"{primary.GetType().Name}: {primary.Message}; MessageBox — " +
                $"{fallback.GetType().Name}: {fallback.Message}.",
                fallback);
        }
        catch
        {
            // Логирование не должно ронять операцию показа диалога.
        }
    }

    /// <summary>Сопоставление вида material-сообщения иконке стандартного MessageBox.</summary>
    private static MessageBoxImage ToMessageBoxImage(MaterialMessageKind kind) => kind switch
    {
        MaterialMessageKind.Warning => MessageBoxImage.Warning,
        MaterialMessageKind.Error => MessageBoxImage.Error,
        MaterialMessageKind.Question => MessageBoxImage.Question,
        _ => MessageBoxImage.Information,
    };

    /// <summary>
    /// Назначает окну владельца только при реально существующем главном окне,
    /// отличном от самого диалога. Пока главное окно не создано, WPF считает главным
    /// первое созданное окно — это и есть только что созданный диалог, и назначение
    /// Owner самому себе бросает ArgumentException при раннем сбое запуска (issue #213).
    /// Диалог без владельца показывается нормально.
    /// </summary>
    private static void SetOwnerIfAvailable(Window win)
    {
        // Владелец — активное окно (issue #11, 7OH 17.10): сообщение из диалога (например,
        // «Управление службами») модально к нему, а не к главному окну; иначе — главное окно.
        Window? active = null;
        foreach (Window w in Application.Current.Windows)
        {
            if (w.IsActive && w.IsVisible && !ReferenceEquals(w, win))
            {
                active = w;
                break;
            }
        }
        var owner = active ?? Application.Current.MainWindow;
        win.Owner = owner is not null && !ReferenceEquals(owner, win) && owner.IsLoaded ? owner : null;
    }

    public string? OpenFileDialog(string title = "", string filter = "", string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = DefaultTitle(title, "Dialog.OpenFile"),
            Filter = BuildFilter(filter),
            InitialDirectory = NormalizeDirectory(initialDirectory),
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SaveFileDialog(string title = "", string defaultFileName = "", string filter = "", string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = DefaultTitle(title, "Dialog.SaveFile"),
            FileName = defaultFileName,
            Filter = BuildFilter(filter),
            InitialDirectory = NormalizeDirectory(initialDirectory),
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? OpenFolderDialog(string title = "", string? initialDirectory = null)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = DefaultTitle(title, "Dialog.SelectFolder"),
            SelectedPath = NormalizeDirectory(initialDirectory)
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    /// <summary>Подставляет локализованный заголовок по умолчанию, если переданный пуст.</summary>
    private static string DefaultTitle(string title, string fallbackKey) =>
        string.IsNullOrWhiteSpace(title) ? LocalizationManager.T(fallbackKey) : title;

    /// <summary>Приводит фильтр к формату OpenFileDialog (разделитель '|'). Пустое значение → все файлы.</summary>
    private static string BuildFilter(string filter) =>
        string.IsNullOrWhiteSpace(filter) ? $"{LocalizationManager.T("Common.AllFiles")}|*.*" : filter;

    /// <summary>Пустой/несуществующий каталог → пустая строка (диалог откроется в стандартном месте).</summary>
    private static string NormalizeDirectory(string? directory) =>
        string.IsNullOrWhiteSpace(directory) ? string.Empty : directory;
}
