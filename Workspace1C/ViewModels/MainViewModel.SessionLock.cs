#if WINDOWS
using System.Windows.Input;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C.ViewModels;

/// <summary>
/// Команды функций №19 (временная блокировка приложения паролем) и №20
/// (блокировка сеансов файловой ИБ без открытия «1С:Предприятия», CTRL+ALT+L)
/// StartManager для Windows/WPF. Частичный класс <see cref="MainViewModel"/>.
/// </summary>
public partial class MainViewModel
{
    private ICommand? _showSessionLockCommand;
    private ICommand? _lockAppCommand;
    private ICommand? _changeAppLockCommand;
    private string _hotkeySessionLock = "Ctrl+Alt+L";
    private string _hotkeyLockApp = "";
    // Пароль временной блокировки приложения (PBKDF2-хэш), функция №19.
    private string _appLockPasswordHash = "";
    // Активная блокировка сохраняется в настройках: блокировка переживает
    // закрытие из трея и перезапуск приложения (issue #294).
    private bool _appLockActive;

    /// <summary>Приложение заблокировано (окно спрятано/свернуто, требуется пароль).</summary>
    public event EventHandler? AppLockEngaged;

    /// <summary>Окно ввода пароля закрыто, а блокировка ещё активна (issue #294).</summary>
    public event EventHandler? AppLockPromptDismissed;

    /// <summary>Горячая клавиша «Блокировка сеансов ИБ» (по умолчанию Ctrl+Alt+L).</summary>
    public string HotkeySessionLock
    {
        get => _hotkeySessionLock;
        set
        {
            if (SetProperty(ref _hotkeySessionLock, NormalizeHotkey(value, "Ctrl+Alt+L")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Горячая клавиша «Временная блокировка приложения» (по умолчанию не задана).</summary>
    public string HotkeyLockApp
    {
        get => _hotkeyLockApp;
        set
        {
            if (SetProperty(ref _hotkeyLockApp, NormalizeHotkey(value, "")))
                ScheduleSaveSettings();
        }
    }

    /// <summary>Команда открытия окна «Блокировка сеансов информационной базы» (Ctrl+Alt+L).</summary>
    public ICommand ShowSessionLockCommand =>
        _showSessionLockCommand ??= new RelayCommand(
            ExecuteShowSessionLock,
            () => SelectedInfobase is not null);

    /// <summary>Команда временной блокировки приложения паролем.</summary>
    public ICommand LockAppCommand =>
        _lockAppCommand ??= new RelayCommand(ExecuteLockApp);

    /// <summary>Команда смены пароля блокировки приложения (issue #294).</summary>
    public ICommand ChangeAppLockCommand =>
        _changeAppLockCommand ??= new RelayCommand(ExecuteChangeAppLock);

    private void ExecuteShowSessionLock()
    {
        var infobase = SelectedInfobase;
        if (infobase is null)
            return;
        // Окно открывается модально относительно главного окна (issue #20): без владельца
        // диалог не до конца модальный — главное окно остаётся активным и может перекрыть
        // диалог. Тот же приём, что у AppLockWindow (issue #294).
        var win = new Workspace1C.SessionLockWindow(infobase)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        win.ShowDialog();
    }

    private void ExecuteLockApp()
    {
        // Если пароль ещё не задан — сначала предложить его установить.
        // При отмене установки блокировка не запускается (issue #294).
        if (!HasAppLockPassword)
        {
            var setupWin = new Workspace1C.AppLockWindow(this, setupMode: true);
            // Окно блокировки открывается модально относительно главного окна (issue #294):
            // без владельца повторный запуск приложения ставил главное окно поверх диалога,
            // и блокировка визуально «не срабатывала».
            setupWin.Owner = System.Windows.Application.Current.MainWindow;
            setupWin.ShowDialog();
            if (!HasAppLockPassword)
                return;
        }

        LockNow();
    }

    /// <summary>
    /// Смена пароля блокировки приложения (issue #294): диалог запрашивает текущий
    /// пароль, новый и его повтор. Если пароль ещё не задан, окно работает в режиме
    /// установки. Сама блокировка этой командой не включается.
    /// </summary>
    private void ExecuteChangeAppLock()
    {
        if (!HasAppLockPassword)
        {
            var setupWin = new Workspace1C.AppLockWindow(this, setupMode: true)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            setupWin.ShowDialog();
            return;
        }

        var win = new Workspace1C.AppLockWindow(this, changeMode: true)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        win.ShowDialog();
    }

    /// <summary>
    /// Включает блокировку приложения (issue #294): интерфейс закрывается оверлеем,
    /// состояние сохраняется в настройках, а окно сворачивается, чтобы не мешать
    /// работать. Пароль запрашивается при попытке открыть окно (из трея, повторным
    /// запуском) и после перезапуска приложения; снять блокировку можно только
    /// верным паролем.
    /// </summary>
    public void LockNow()
    {
        if (!HasAppLockPassword)
            return;

        // Блокировка завершает доступ к секретам: сеансовый пароль учётной записи
        // (ключ шифрования паролей, issue #163) очищается и потребуется снова.
        AppServices.TryGetService<IProfileService>()?.ClearPasswordSession();

        IsAppLocked = true;
        _appLockActive = true;
        // Сохраняем сразу (не по debounce): блокировка обязана пережить и аварийное
        // завершение, и запуск второй копии приложения.
        SaveSettings();
        AppLockEngaged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Снимает блокировку приложения (после верного пароля, issue #294).</summary>
    public void UnlockApp()
    {
        IsAppLocked = false;
        if (_appLockActive)
        {
            _appLockActive = false;
            SaveSettings();
        }
    }

    /// <summary>
    /// Восстанавливает блокировку после запуска приложения (issue #294): если в
    /// прошлом сеансе приложение было заблокировано, окно открывается с оверлеем
    /// и окном ввода пароля. Без пароля (сброшен в файле настроек) флаг гасится.
    /// </summary>
    public void RestoreAppLockOnStartup()
    {
        if (!_appLockActive)
            return;
        if (!HasAppLockPassword)
        {
            _appLockActive = false;
            SaveSettings();
            return;
        }

        IsAppLocked = true;
        ShowAppUnlockDialog();
    }

    private Workspace1C.AppLockWindow? _unlockWindow;

    /// <summary>
    /// Показывает окно ввода пароля в немодальном режиме с владельцем — главное окно
    /// остаётся недоступным из-за оверлея и перехвата ввода (issue #294). Повторный
    /// вызов активирует уже открытое окно, а не создаёт новое. Если окно закрыто,
    /// а блокировка ещё активна, главному окну сообщается об этом (AppLockPromptDismissed) —
    /// оно сворачивается, чтобы не мешать работать до ввода верного пароля.
    /// </summary>
    public void ShowAppUnlockDialog()
    {
        if (_unlockWindow != null)
        {
            _unlockWindow.Activate();
            return;
        }

        var win = new Workspace1C.AppLockWindow(this, setupMode: false);
        _unlockWindow = win;
        win.UnlockSucceeded += (_, _) => UnlockApp();
        win.Closed += (_, _) =>
        {
            _unlockWindow = null;
            // Закрытие без верного пароля: окно приложения прячется обратно
            // (в трей или свернуто), блокировка остаётся активной (issue #294).
            if (IsAppLocked)
                AppLockPromptDismissed?.Invoke(this, EventArgs.Empty);
        };
        win.Owner = System.Windows.Application.Current.MainWindow;
        win.Show();
    }

    /// <summary>Пароль блокировки приложения (PBKDF2-хэш) из настроек.</summary>
    public bool HasAppLockPassword => !string.IsNullOrEmpty(_appLockPasswordHash);

    /// <summary>Задаёт/меняет пароль блокировки приложения.</summary>
    public void SetAppLockPassword(string? password)
    {
        _appLockPasswordHash = PasswordHasher.Hash(password ?? string.Empty);
        ScheduleSaveSettings();
    }

    /// <summary>Проверяет пароль для снятия блокировки приложения.</summary>
    public bool VerifyAppLockPassword(string password) =>
        PasswordHasher.Verify(password, _appLockPasswordHash);
}
#endif