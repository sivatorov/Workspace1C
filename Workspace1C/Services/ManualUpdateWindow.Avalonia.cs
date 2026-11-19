#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C.Services;

/// <summary>
/// Модальное окно «Ручное обновление» (Linux/Avalonia). Показывается, когда
/// самообновление недоступно (запуск из пакета AppImage или установка в системный
/// каталог, например через deb): объясняет пользователю, почему автоматически
/// заменить исполняемый файл нельзя, и предлагает кликабельную ссылку на страницу
/// выпуска GitHub, откуда новую версию можно скачать и установить вручную (issue #225).
/// </summary>
internal sealed class ManualUpdateWindowAvalonia : Window
{
    public ManualUpdateWindowAvalonia(string message, string linkUrl, string title)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.Full;

        ThemeBrushes.Bind(this, TemplatedControl.BackgroundProperty, "ContentBackgroundColorBrush");

        // Иконка информации, как у обычного сообщения.
        var messageIcon = Workspace1C.IconHelper.MakeIcon("IconInfo", UiMetrics.IconHeader);
        messageIcon.Margin = new Thickness(0, 0, 12, 0);
        messageIcon.VerticalAlignment = VerticalAlignment.Top;

        // Пояснение, почему самообновление недоступно.
        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeBrushes.Bind(messageBlock, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");

        var body = new Grid { Margin = new Thickness(4, 8, 4, 8) };
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        body.Children.Add(messageIcon);
        Grid.SetColumn(messageBlock, 1);
        body.Children.Add(messageBlock);

        var content = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(16)
        };
        content.Children.Add(body);

        // Кликабельная ссылка на страницу выпуска (если ссылка доступна).
        if (!string.IsNullOrWhiteSpace(linkUrl))
        {
            var link = new TextBlock
            {
                Text = LocalizationManager.T("Update.OpenReleasePage"),
                TextDecorations = TextDecorations.Underline,
                Cursor = new Cursor(StandardCursorType.Hand),
                TextWrapping = TextWrapping.Wrap,
                // Прижимаем ширину к тексту, чтобы проверка попадания по Bounds
                // (как в LinkBlock окна настроек) считалась корректно.
                HorizontalAlignment = HorizontalAlignment.Left
            };
            ThemeBrushes.Bind(link, TextBlock.ForegroundProperty, "AccentBrush");
            link.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left)
                    return;
                var point = e.GetPosition(link);
                if (point.X < 0 || point.Y < 0
                    || point.X > link.Bounds.Width || point.Y > link.Bounds.Height)
                    return;

                if (!OneCLauncher.OpenUrl(linkUrl))
                {
                    // Браузер не открылся — копируем адрес в буфер обмена, чтобы
                    // пользователь всё равно смог перейти на страницу выпуска.
                    try
                    {
                        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                        if (clipboard is not null)
                            _ = clipboard.SetTextAsync(linkUrl);
                    }
                    catch { /* копирование не должно ронять окно */ }
                }
            };
            content.Children.Add(link);
        }

        // Кнопка «ОК» — единая акцентная кнопка ModernButton (issue #26):
        // заливка AccentBrush, контрастный текст TextOnAccentBrush, как во всех
        // диалогах приложения. Прежняя жёстко-зелёная #16A34A убрана.
        Button okButton = new()
        {
            Content = new TextBlock
            {
                Text = LocalizationManager.T("Common.Ok"),
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            },
            MinWidth = 100,
            IsDefault = true
        };
        okButton.Styled(ControlThemes.ModernButton);
        okButton.FontWeight = FontWeight.SemiBold;
        okButton.Click += (_, _) => Close();

        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttonsPanel.Children.Add(okButton);
        content.Children.Add(buttonsPanel);

        Content = content;
    }
}
#endif