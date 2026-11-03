#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C;

/// <summary>
/// Окно «Лицензия <серийный номер>» (issue #83, v1.0.1.27) — Avalonia/Linux-версия
/// окна <see cref="LicenseDetailsWindow"/> (строится кодом, стиль — ControlThemes):
/// читабельная информация из файла *.lic из <see cref="RingLicenseParser.BuildReadableDetails"/>
/// (без начального длинного ключа) + кнопки «Открыть папку» и «Закрыть».
/// </summary>
public sealed class LicenseDetailsWindow : ModalWindowBase
{
    private readonly ProgramLicense _license;

    /// <summary>Создаёт окно деталей по модели лицензии.</summary>
    public LicenseDetailsWindow(ProgramLicense license)
    {
        _license = license;
        Title = string.Format(
            LocalizationManager.T("ServerMonitor.Licenses.DetailsTitle"), license.SerialNumber);
        Width = 600;
        Height = 500;
        // Минимальные размеры изменяемого окна (issue #55, тест-страж layout-аудита).
        MinWidth = 460;
        MinHeight = 380;
        FontSize = 13;

        var root = new StackPanel { Margin = new Thickness(16) };

        var scroll = new ScrollViewer
        {
            MaxHeight = 380,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var details = new TextBlock
        {
            Text = RingLicenseParser.BuildReadableDetails(license),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, DejaVu Sans Mono, monospace")
        };
        ThemeBrushes.Bind(details, TextBlock.ForegroundProperty, "TextPrimaryBrush");
        scroll.Content = details;
        root.Children.Add(scroll);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var openFolderButton = new Button
        {
            MinWidth = 140,
            Height = 34,
            Content = new ContentControl
            {
                Content = LocalizationManager.T("ServerMonitor.Licenses.OpenFolder")
            }
        };
        openFolderButton.Styled(ControlThemes.DialogConfirmButton);
        openFolderButton.Click += (_, _) =>
        {
            if (!LicenseFolderOpener.TryOpenFolderWithFile(_license.FilePath))
                new Services.AvaloniaDialogService().ShowInfo(
                    string.Format(
                        LocalizationManager.T("ServerMonitor.Licenses.FileNotFound"), _license.FilePath),
                    Title);
        };
        buttons.Children.Add(openFolderButton);

        var closeButton = new Button
        {
            MinWidth = 110,
            Height = 34,
            Content = new ContentControl { Content = LocalizationManager.T("Common.Close") }
        };
        closeButton.Styled(ControlThemes.DialogCancelButton);
        closeButton.Click += (_, _) => Close();
        buttons.Children.Add(closeButton);

        root.Children.Add(buttons);
        Content = root;
    }
}
#endif
