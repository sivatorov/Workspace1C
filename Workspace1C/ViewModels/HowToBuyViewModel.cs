using Workspace1C.Localization;
using Workspace1C.Services.Licensing;

namespace Workspace1C.ViewModels;

/// <summary>
/// ViewModel окна «Как купить ПРО-версию»: код компьютера и статус последнего действия
/// (копирование кода, открытие ссылок). Общая для WPF и Avalonia; контакты покупки —
/// константы <see cref="ProFeatures"/> (Telegram-канал, Issues, README).
/// </summary>
public sealed class HowToBuyViewModel : ViewModelBase
{
    private string _statusText = string.Empty;

    public HowToBuyViewModel()
    {
        MachineCode = MachineIdProvider.Current;
    }

    /// <summary>Код компьютера для выпуска ключа (совпадает с окном «Лицензия»).</summary>
    public string MachineCode { get; }

    /// <summary>Код компьютера или пояснение, если определить его не удалось.</summary>
    public string MachineCodeText => string.IsNullOrEmpty(MachineCode)
        ? LocalizationManager.T("Pro.MachineCodeUnavailable")
        : MachineCode;

    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    /// <summary>Отмечает, что код компьютера скопирован в буфер (вызывается окном).</summary>
    public void ReportCodeCopied() => StatusText = LocalizationManager.T("Pro.CodeCopied");

    /// <summary>Отмечает неудачу открытия ссылки (браузер не найден).</summary>
    public void ReportLinkOpenFailed() => StatusText = LocalizationManager.T("Settings.About.LinkOpenFailed");
}
