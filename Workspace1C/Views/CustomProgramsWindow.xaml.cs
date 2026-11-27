#if WINDOWS
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Мои программы» (issue #91, Windows/WPF): список пользовательских программ
/// группы под кнопками запуска конфигуратора — добавление/удаление, перемещение
/// вверх-вниз, имя, исполняемый файл, аргументы, рабочая папка, иконка из файла
/// (<c>.ico</c>/<c>.png</c>) и переключатель отображения «списком / иконками».
/// После закрытия вызывающий код (MainViewModel) применяет режим и перечитывает список.
/// </summary>
public partial class CustomProgramsWindow : Window
{
    /// <summary>
    /// Issue #158 (1.0.1.123): открытое окно редактора (не более одного). Повторное
    /// открытие активирует существующее окно, а не создаёт второе.
    /// </summary>
    private static CustomProgramsWindow? _openInstance;

    private readonly CustomProgramsViewModel _vm;
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

    /// <param name="viewMode">Текущий режим отображения группы из настроек.</param>
    public CustomProgramsWindow(string? viewMode)
    {
        InitializeComponent();
        _vm = new CustomProgramsViewModel(AppServices.TryGetService<ICustomProgramsStore>(), viewMode);
        DataContext = _vm;
        Owner = Application.Current?.MainWindow;
        // issue #146 (1.0.1.106): высота окна не должна выходить за рабочую область.
        ApplyWorkAreaLimits();
    }

    /// <summary>
    /// Issue #146 (1.0.1.118): рекомендованные размеры окна берутся из профиля
    /// <see cref="DialogWorkAreaPolicy.CustomPrograms"/>. Issue #178 (1.0.1.129):
    /// высота увеличена до 700 DIP — после добавления блока «Запускать от имени
    /// администратора» прежние 600 DIP не вмещали правую форму и при открытии
    /// появлялась прокрутка. Минимальная высота 400 DIP позволяет уменьшать окно,
    /// а на маленьком экране размер клампится по рабочей области (прокрутка —
    /// аварийный вариант).
    /// </summary>
    private void ApplyWorkAreaLimits()
    {
        var workArea = SystemParameters.WorkArea;
        var size = DialogWorkAreaPolicy.Recommend(
            DialogWorkAreaPolicy.CustomPrograms,
            workArea.Height);
        MaxHeight = size.MaxHeight;
        MinHeight = size.MinHeight;
        Height = size.Height;
    }

    /// <summary>Выбранный в окне режим отображения группы (для сохранения в настройках).</summary>
    public string ViewMode => _vm.ViewMode;

    /// <summary>Открыт ли редактор «Мои программы» прямо сейчас (issue #158).</summary>
    public static bool IsOpen => CustomProgramsEditorGuard.IsOpen;

    /// <summary>
    /// Issue #158 (1.0.1.123): единственная точка открытия редактора. Повторный вызов
    /// при уже открытом окне активирует существующее и не создаёт второе. Слот
    /// освобождается в <c>finally</c> и в <see cref="OnClosed"/>, поэтому ошибка
    /// сохранения/удаления не «залипает» навсегда.
    /// </summary>
    /// <param name="owner">Окно-владелец (настройки или главное окно).</param>
    /// <param name="viewMode">Текущий режим отображения группы из настроек.</param>
    /// <returns>Открытое (или уже открытое) окно редактора — для чтения <see cref="ViewMode"/>.</returns>
    public static CustomProgramsWindow ShowEditor(Window? owner, string? viewMode)
    {
        if (CustomProgramsEditorGuard.IsOpen && _openInstance is { } existing)
        {
            try { existing.Activate(); } catch { /* активация не критична */ }
            return existing;
        }

        var win = new CustomProgramsWindow(viewMode);
        if (owner is not null)
            win.Owner = owner;

        if (!CustomProgramsEditorGuard.TryEnter())
            return _openInstance ?? win;

        _openInstance = win;
        try
        {
            win.ShowDialog();
        }
        finally
        {
            if (ReferenceEquals(_openInstance, win))
                _openInstance = null;
            CustomProgramsEditorGuard.Exit();
        }
        return win;
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        // Слот освобождает только «своё» окно: закрытие постороннего экземпляра
        // не должно открывать дорогу второму редактору.
        if (ReferenceEquals(_openInstance, this))
        {
            _openInstance = null;
            CustomProgramsEditorGuard.Exit();
        }
        base.OnClosed(e);
    }

    private static string T(string key) => LocalizationManager.T(key);

    /// <summary>Выбор исполняемого файла программы.</summary>
    private void OnBrowseFile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProgram is not { } program)
            return;
        var path = _dialogs.OpenFileDialog(
            T("Launch.CustomPrograms.FileDialogTitle"),
            T("Launch.CustomPrograms.FileDialogFilter"),
            SafeDirectory(program.FilePath));
        if (!string.IsNullOrWhiteSpace(path))
            program.FilePath = path!;
    }

    /// <summary>Выбор рабочей папки программы.</summary>
    private void OnBrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProgram is not { } program)
            return;
        var path = _dialogs.OpenFolderDialog(
            T("Launch.CustomPrograms.WorkingDirectoryDialogTitle"),
            SafeDirectory(program.WorkingDirectory));
        if (!string.IsNullOrWhiteSpace(path))
            program.WorkingDirectory = path!;
    }

    /// <summary>Выбор иконки программы из файла (.ico/.png).</summary>
    private void OnBrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedProgram is not { } program)
            return;
        var path = _dialogs.OpenFileDialog(
            T("Launch.CustomPrograms.IconDialogTitle"),
            T("Launch.CustomPrograms.IconDialogFilter"),
            SafeDirectory(program.IconPath));
        if (!string.IsNullOrWhiteSpace(path))
            program.IconPath = path!;
    }

    /// <summary>
    /// «Сохранить»: проверка обязательных полей, запись списка и закрытие окна.
    /// Issue #158 (1.0.1.121): при ошибке сохранения окно остаётся открытым и
    /// закрывается кнопкой «Отмена»; исключение записи не выходит наружу.
    /// </summary>
    private void OnOk_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.TrySave(out var error))
        {
            ShowSaveError(error);
            return;
        }
        CloseWithResult(true);
    }

    /// <summary>
    /// «Отмена»: закрытие без сохранения (изменения остаются только в памяти).
    /// Issue #158 (1.0.1.121): только <see cref="Window.Close()"/> — без установки
    /// <see cref="Window.DialogResult"/>, которая вне <c>ShowDialog</c> бросает
    /// <c>InvalidOperationException</c> и блокирует закрытие.
    /// </summary>
    private void OnCancel_Click(object sender, RoutedEventArgs e)
    {
        CloseSafely();
    }

    /// <summary>
    /// Esc закрывает редактор без сохранения (issue #158: паритет с Avalonia
    /// <c>ModalWindowBase.CloseOnEscape</c>; кнопка «Отмена» больше не <c>IsCancel</c>).
    /// </summary>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            CloseSafely();
        }
    }

    /// <summary>
    /// Устанавливает <see cref="Window.DialogResult"/> и закрывает окно. Вне модального
    /// показа свойство бросает <c>InvalidOperationException</c> — тогда закрываем штатно,
    /// чтобы окно гарантированно закрылось (issue #158).
    /// </summary>
    private void CloseWithResult(bool result)
    {
        try
        {
            DialogResult = result;
        }
        catch (InvalidOperationException)
        {
            // Окно показано немодально — результат не нужен, закрываем без исключения.
            CloseSafely();
        }
    }

    /// <summary>
    /// Issue #158 (1.0.1.123): гарантированное закрытие окна. Если синхронное
    /// <see cref="Window.Close()"/> сорвалось, окно закрывается ещё раз из очереди
    /// диспетчера, а ошибка пишется в журнал — окно не «залипает» на экране.
    /// </summary>
    private void CloseSafely()
    {
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            LogWindowError("Не удалось закрыть окно «Мои программы».", ex);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { Close(); }
                catch { /* окно уже закрыто или не открывалось */ }
            }), DispatcherPriority.Background);
        }
    }

    /// <summary>Запись ошибки окна в журнал; недоступность журнала не ломает UI.</summary>
    private static void LogWindowError(string message, Exception ex)
    {
        try
        {
            AppServices.TryGetService<IAppLogger>()?.Error($"[custom-programs] {message}", ex);
        }
        catch
        {
            // Журнал не должен ломать сценарий закрытия/удаления.
        }
    }

    /// <summary>Показ понятного сообщения об ошибке сохранения (issue #158).</summary>
    private void ShowSaveError(string error)
    {
        try
        {
            _dialogs.ShowWarning(error, T("Launch.CustomPrograms.SaveFailedTitle"));
        }
        catch (Exception ex)
        {
            AppServices.TryGetService<IAppLogger>()?.Error(
                "[custom-programs] Не удалось показать сообщение об ошибке сохранения.", ex);
        }
    }

    /// <summary>Каталог для начального выбора в диалоге (существующий, иначе пусто).</summary>
    private static string? SafeDirectory(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var dir = System.IO.Path.GetDirectoryName(path);
            return !string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir) ? dir : null;
        }
        catch
        {
            return null;
        }
    }
}
#endif
