#if LINUX
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Workspace1C.Services;

/// <summary>
/// Глобальная горячая клавиша «показать главное окно» на Linux (issue #82): XGrabKey
/// на корневом окне X11 + фоновый поток с XNextEvent. Работает в X11-сессиях
/// (включая XWayland); в чистом Wayland глобальные захваты требуют portal/композитор —
/// тогда открытие дисплея не удаётся и регистрация честно отключается (настройка
/// сохраняется, но не срабатывает — в лог пишется причина).
/// </summary>
public sealed class GlobalHotkeyServiceAvalonia : IGlobalHotkeyService
{
    private const int KeyPressEvent = 2;
    private const int GrabModeAsync = 1;

    private const uint ShiftMask = (1u << 0);
    private const uint LockMask = (1u << 1);
    private const uint ControlMask = (1u << 2);
    private const uint Mod1Mask = (1u << 3);
    private const uint Mod2Mask = (1u << 4);
    private const uint Mod4Mask = (1u << 5);

    private const int EventBufferSize = 256;

    private IntPtr _display;
    private Thread? _thread;
    private volatile bool _running;
    private string? _gesture;
    // Копия keycode фиксируется при регистрации (до старта потока) — по ней
    // фильтруются события KeyPress из цикла X11.
    private volatile uint _keycodeGuard;

    public GlobalHotkeyServiceAvalonia()
    {
        _display = IntPtr.Zero;
    }

    public string? RegisteredGesture => _gesture;

    public Action? HotkeyPressed { get; set; }

    public void SetGesture(string? gesture)
    {
        Stop();
        _gesture = GlobalHotkeyLogic.Normalize(gesture);
        if (_gesture is null || _gesture.Length == 0)
        {
            _gesture = null;
            return;
        }

        if (!HotkeyMatcher.TryParse(_gesture, out var keyName, out var modifiers))
        {
            _gesture = null;
            return;
        }

        // Открываем дисплей навсегда — фоновая нить читает события. В X11/XWayland
        // Display доступен; на Wayland (без XWayland) XOpenDisplay вернёт null.
        _display = XOpenDisplay(null);
        if (_display == IntPtr.Zero)
        {
            _gesture = null; // среда не даёт глобальный захват — честно отключаем
            return;
        }

        var keysym = KeyNameToKeysym(keyName);
        if (keysym == 0)
        {
            _gesture = null;
            return;
        }

        var keycode = XKeysymToKeycode(_display, keysym);
        if (keycode == 0)
        {
            _gesture = null;
            return;
        }
        _keycodeGuard = keycode;

        var modifiersMask = ToX11Modifiers(modifiers);
        // Модификаторы компоновки (CapsLock/NumLock) не должны мешать сочетанию:
        // регистрируем базовую маску и её варианты с Lock/Mod2 (классика XGrabKey).
        var mask1 = (uint)modifiersMask;
        var mask2 = (uint)(modifiersMask | LockMask);
        var mask3 = (uint)(modifiersMask | Mod2Mask);
        var mask4 = (uint)(modifiersMask | LockMask | Mod2Mask);

        var root = XRootWindow(_display, XDefaultScreen(_display));
        if (root == IntPtr.Zero)
        {
            _gesture = null;
            return;
        }

        var grabbed = XGrabKey(_display, keycode, mask1, root, 0, GrabModeAsync, GrabModeAsync) == 0
            && XGrabKey(_display, keycode, mask2, root, 0, GrabModeAsync, GrabModeAsync) == 0
            && XGrabKey(_display, keycode, mask3, root, 0, GrabModeAsync, GrabModeAsync) == 0
            && XGrabKey(_display, keycode, mask4, root, 0, GrabModeAsync, GrabModeAsync) == 0;

        if (!grabbed)
        {
            _gesture = null; // клавиша занята другой программой/сервером
            return;
        }

        XFlush(_display);
        _running = true;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "GlobalHotkeyX11" };
        _thread.Start();
    }

    private void MessageLoop()
    {
        var buffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal(EventBufferSize);
            while (_running && _display != IntPtr.Zero)
            {
                // XPending>0 защищает от блокировки без таймаута при отлучённом сервере.
                while (_running && XPending(_display) > 0)
                {
                    XNextEvent(_display, buffer);
                    var type = Marshal.ReadInt32(buffer, 0);
                    if (type != KeyPressEvent)
                        continue;
                    // Marshal.ReadUInt32 не существует: keycode в XKeyEvent —
                    // unsigned char, читаем байт через ReadInt32 и приводим.
                    var keycode = (uint)Marshal.ReadInt32(buffer, KeycodeOffset());
                    if (keycode != _keycodeGuard)
                        continue;
                    var pressed = HotkeyPressed;
                    if (pressed is not null)
                    {
                        // Нажатие пришло из фонового потока X11 — действие окна обязано
                        // быть синхронизировано на UI-поток тем, кто его зарегистрировал.
                        try { pressed(); } catch { /* действие не должно ронять цикл */ }
                    }
                }
                if (_running)
                {
                    // v100114 (п.4.2 плана): вместо активного ожидания Thread.Sleep(10) —
                    // poll() на сокете X-соединения: поток спит в ядре до прихода данных
                    // (нажатие хоткея будит его мгновенно) или до контрольного таймаута
                    // (чтобы своевременно увидеть сброс _running в Stop/закрытие дисплея).
                    XFlush(_display);
                    var pollFd = new PollFd { Fd = XConnectionNumber(_display), Events = PollIn };
                    _ = poll(ref pollFd, (UIntPtr)1, PollTimeoutMs);
                }
            }
        }
        catch
        {
            // Сбой цикла (дисплей закрыт): глобальная клавиша перестаёт работать,
            // но приложение продолжает. Настройка остаётся — следующий SetGesture
            // (переключение/повторное открытие) перерегистрирует захват.
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }
    }

    private void Stop()
    {
        _running = false;
        if (_thread is { IsAlive: true })
        {
            try { _thread.Join(300); } catch { /* уже завершился */ }
        }
        _thread = null;
        if (_display != IntPtr.Zero)
        {
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        Stop();
        HotkeyPressed = null;
        GC.SuppressFinalize(this);
    }

    private static uint ToX11Modifiers(HotkeyModifiers modifiers)
    {
        uint result = 0;
        if ((modifiers & HotkeyModifiers.Control) != 0) result |= ControlMask;
        if ((modifiers & HotkeyModifiers.Shift) != 0) result |= ShiftMask;
        if ((modifiers & HotkeyModifiers.Alt) != 0) result |= Mod1Mask;
        if ((modifiers & HotkeyModifiers.Windows) != 0) result |= Mod4Mask;
        // Нет модификаторов в сочетании XGrabKey бессмыслен, но HotkeyMatcher
        // требует клавишу с модификаторами для глобальных — по умолчанию Control.
        return result == 0 ? ControlMask : result;
    }

    /// <summary>Смещение keycode в XKeyEvent в зависимости от разрядности процесса.</summary>
    private static int KeycodeOffset()
    {
        // XKeyEvent: int type(0); unsigned long serial(после pad); ... keycode после state.
        // При IntPtr 8: 84, при 4: 44 (стандартное выравнивание X11 ABI на 32-бит).
        return IntPtr.Size == 8 ? 84 : 44;
    }

    /// <summary>Имя клавиши HotkeyMatcher → X11 keysym. Спец-имена — через XStringToKeysym.</summary>
    private static uint KeyNameToKeysym(string keyName)
    {
        var upper = keyName.ToUpperInvariant();
        return upper switch
        {
            "ESC" => XStringToKeysym("Escape"),
            "ENTER" => XStringToKeysym("Return"),
            "SPACE" => (uint)' ',
            "BACK" => XStringToKeysym("BackSpace"),
            "DELETE" => XStringToKeysym("Delete"),
            "HOME" => XStringToKeysym("Home"),
            "END" => XStringToKeysym("End"),
            "INSERT" => XStringToKeysym("Insert"),
            "PAGEUP" => XStringToKeysym("Prior"),
            "PAGEDOWN" => XStringToKeysym("Next"),
            "UP" => XStringToKeysym("Up"),
            "DOWN" => XStringToKeysym("Down"),
            "LEFT" => XStringToKeysym("Left"),
            "RIGHT" => XStringToKeysym("Right"),
            "TAB" => XStringToKeysym("Tab"),
            _ => upper is { Length: 1 } && char.IsAsciiLetterOrDigit(upper[0])
                ? (uint)char.ToUpperInvariant(upper[0]) // ASCII keysyms совпадают с латинскими кодами
                : XStringToKeysym(keyName)
        };
    }

    // ---- libX11 (X11 # 6) ----

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(string? name);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XRootWindow(IntPtr display, int screen);

    [DllImport("libX11.so.6")]
    private static extern uint XKeysymToKeycode(IntPtr display, uint keysym);

    [DllImport("libX11.so.6")]
    private static extern int XGrabKey(IntPtr display, uint keycode, uint modifiers,
        IntPtr grabWindow, int ownerEvents, int pointerMode, int keyboardMode);

    [DllImport("libX11.so.6")]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern uint XStringToKeysym(string name);

    [DllImport("libX11.so.6")]
    private static extern int XConnectionNumber(IntPtr display);

    // ---- libc: poll() для событийного ожидания событий X11 (п.4.2 плана v100114) ----

    [DllImport("libc", SetLastError = true)]
    private static extern int poll(ref PollFd fds, UIntPtr nfds, int timeout);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    /// <summary>Запрашиваем события POLLIN (есть данные для чтения из сокета X11).</summary>
    private const short PollIn = 0x001;

    /// <summary>Контрольный таймаут poll, мс: только для реакции на Stop/закрытие дисплея.</summary>
    private const int PollTimeoutMs = 500;
}
#endif