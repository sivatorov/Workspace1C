using System.Collections.Generic;
using System.Linq;
using Workspace1C.Localization;
using Workspace1C.Services.Licensing;

namespace Workspace1C.ViewModels;

/// <summary>Строка списка функций ПРО в окне «Лицензия».</summary>
public sealed class ProFeatureRow
{
    public ProFeatureRow(ProFeature feature, bool available, bool highlighted)
    {
        Feature = feature;
        Title = LocalizationManager.T(ProFeatures.TitleKey(feature));
        Available = available;
        Highlighted = highlighted;
        Mark = available ? "✓" : "—";
    }

    public ProFeature Feature { get; }
    public string Title { get; }
    public bool Available { get; }
    public bool Highlighted { get; }
    public string Mark { get; }
    public string Display => (Highlighted ? "▶ " : "   ") + Mark + "  " + Title;
}

/// <summary>
/// ViewModel окна «Лицензия» (docs/pro/PRO-EDITION.md, раздел 4): редакция, код компьютера,
/// ввод ключа, список функций ПРО. Общая для WPF и Avalonia.
/// </summary>
public sealed class LicenseViewModel : ViewModelBase
{
    private string _keyText = string.Empty;
    private string _statusText = string.Empty;
    private string _editionText = string.Empty;
    private string _ownerText = string.Empty;
    private IReadOnlyList<ProFeatureRow> _features = new List<ProFeatureRow>();

    public LicenseViewModel(ProFeature? requested = null)
    {
        Requested = requested;
        MachineCode = MachineIdProvider.Current;
        RequestedText = requested is { } f
            ? string.Format(LocalizationManager.T("Pro.RequestedFormat"), LocalizationManager.T(ProFeatures.TitleKey(f)))
            : string.Empty;
        Refresh();
    }

    /// <summary>Функция, из-за которой открыто окно (подсвечивается в списке).</summary>
    public ProFeature? Requested { get; }

    /// <summary>«Функция «…» доступна в ПРО» — пусто, если окно открыто из меню.</summary>
    public string RequestedText { get; }

    /// <summary>Код компьютера для покупки ключа.</summary>
    public string MachineCode { get; }

    /// <summary>Текст пояснения про код компьютера (или ошибка его получения).</summary>
    public string MachineCodeText => string.IsNullOrEmpty(MachineCode)
        ? LocalizationManager.T("Pro.MachineCodeUnavailable")
        : MachineCode;

    public string KeyText { get => _keyText; set => SetProperty(ref _keyText, value); }

    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    public string EditionText { get => _editionText; private set => SetProperty(ref _editionText, value); }

    public string OwnerText { get => _ownerText; private set => SetProperty(ref _ownerText, value); }

    public IReadOnlyList<ProFeatureRow> Features { get => _features; private set => SetProperty(ref _features, value); }

    /// <summary>Текст для списка функций одной строкой на функцию (простые окна без DataGrid).</summary>
    public string FeaturesText => string.Join("\n", Features.Select(r => r.Display));

    /// <summary>Сборка без модулей ПРО: новые функции 2.x недоступны даже с ключом.</summary>
    public bool IsCommunityBuild => !EditionService.HasProModules;

    public string CommunityBuildText => IsCommunityBuild ? LocalizationManager.T("Pro.CommunityBuild") : string.Empty;

    /// <summary>Проверяет и сохраняет введённый ключ.</summary>
    public bool Activate()
    {
        var result = EditionService.Activate(KeyText);
        StatusText = LocalizationManager.T(LicenseVerifier.StatusTextKey(result.Status));
        if (result.Status == LicenseStatus.WrongMajorVersion && result.Key is not null)
            StatusText = string.Format(LocalizationManager.T("Pro.Status.WrongMajorVersionFormat"), result.Key.Payload.Major);
        Refresh();
        if (result.IsValid)
            KeyText = string.Empty;
        return result.IsValid;
    }

    /// <summary>Удаляет ключ с этого компьютера.</summary>
    public void Deactivate()
    {
        EditionService.Deactivate();
        StatusText = LocalizationManager.T("Pro.Deactivated");
        Refresh();
    }

    private void Refresh()
    {
        var current = EditionService.Current;
        EditionText = LocalizationManager.T(current.IsValid ? "Pro.EditionPro" : "Pro.EditionFree");
        OwnerText = current.IsValid && current.Key is not null
            ? string.Format(LocalizationManager.T("Pro.OwnerFormat"),
                current.Key.Payload.Owner, current.Key.Payload.Major, current.Key.Payload.Issued)
            : string.Empty;
        if (string.IsNullOrEmpty(StatusText) && current.Status is not (LicenseStatus.None or LicenseStatus.Valid))
            StatusText = LocalizationManager.T(LicenseVerifier.StatusTextKey(current.Status));
        Features = ProFeatures.All
            .Select(f => new ProFeatureRow(f, EditionService.Has(f), Requested == f))
            .ToList();
        OnPropertyChanged(nameof(FeaturesText));
    }
}
