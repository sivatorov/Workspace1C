#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C.Services;

/// <summary>
/// Всплывающее окно сообщения в стиле Material Design (Linux/Avalonia).
/// Реализует <see cref="MaterialMessageKind"/> через программно построенное
/// окно с иконкой, текстом и кнопками (аналогично WPF-версии MaterialMessageWindow).
/// </summary>
internal sealed class MaterialMessageWindowAvalonia : Window
{
    public bool Confirmed { get; private set; } = true;

    /// <summary>Ответ дан кнопкой окна, а не закрытием через оконный менеджер.</summary>
    private bool _answered;

    /// <summary>Кнопка по умолчанию — для фокуса при открытии и клавиш Space/Enter (issue #100).</summary>
    private Button _okButton = null!;

    /// <param name="markdownBody">
    /// Необязательный текст «Что нового» в формате markdown. Показывается под
    /// основным сообщением с рендером стилей (заголовки, жирный, списки). При
    /// пустом значении выводится заглушка о недоступном описании.
    /// </param>
    public MaterialMessageWindowAvalonia(
        string message, string title, MaterialMessageKind kind, string? markdownBody = null)
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.Full;

        ThemeBrushes.Bind(this, TemplatedControl.BackgroundProperty, "ContentBackgroundColorBrush");

        // Иконка типа сообщения.
        var iconKey = kind switch
        {
            MaterialMessageKind.Warning => "IconWarning",
            MaterialMessageKind.Error => "IconError",
            MaterialMessageKind.Question => "IconUnknown",
            _ => "IconInfo"
        };
        var messageIcon = Workspace1C.IconHelper.MakeIcon(iconKey, 28);
        messageIcon.Margin = new Thickness(0, 0, 12, 0);
        messageIcon.VerticalAlignment = VerticalAlignment.Top;

        // Текст.
        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        ThemeBrushes.Bind(messageBlock, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");

        // Тело: сетка, чтобы длинный текст переносился.
        var body = new Grid { Margin = new Thickness(4, 8, 4, 8) };
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        body.Children.Add(messageIcon);
        Grid.SetColumn(messageBlock, 1);
        body.Children.Add(messageBlock);

        // Кнопки. Подтверждение («Да»/«ОК») — единая акцентная кнопка ModernButton
        // (issue #26): заливка AccentBrush, контрастный текст TextOnAccentBrush —
        // как во всех остальных диалогах приложения; прежняя жёстко-зелёная #16A34A
        // с белым текстом убрана (Windows-версия переведена на тот же контракт).
        var okText = kind == MaterialMessageKind.Question
            ? LocalizationManager.T("Common.Yes")
            : LocalizationManager.T("Common.Ok");
        Button okButton = new()
        {
            Content = new TextBlock
            {
                Text = okText,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            },
            MinWidth = 100,
            IsDefault = true
        };
        okButton.Styled(ControlThemes.ModernButton);
        okButton.Click += (_, _) => { _answered = true; Confirmed = true; Close(); };
        _okButton = okButton;

        // issue #100 (Linux): диалог сообщений при открытии обязан забрать фокус на
        // кнопку по умолчанию — иначе клавиатура оставалась у владельца, и пробел
        // «нажимал» меню перекрытого окна вместо кнопки «ОК». На Windows фокус
        // устанавливает WPF (IsDefault), на Linux/X11 требуется явная установка
        // после показа окна (аналогично паттерну #299).
        Opened += (_, _) => Dispatcher.UIThread.Post(() => _okButton.Focus());

        // Порядок кнопок согласован с WPF-версией: «Да» (подтверждение) слева,
        // «Нет» (отмена) справа.
        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttonsPanel.Children.Add(okButton);

        if (kind == MaterialMessageKind.Question)
        {
            // Отмена — вторичная кнопка SecondaryButton (карточка + контур + текст темы),
            // а не жёсткая тёмная надпись на светлой подложке (issue #26).
            Button cancelButton = new()
            {
                Content = LocalizationManager.T("Common.No"),
                MinWidth = 90,
                IsCancel = true
            };
            cancelButton.Styled(ControlThemes.SecondaryButton);
            cancelButton.Click += (_, _) => { _answered = true; Confirmed = false; Close(); };

            // Закрытие вопроса крестиком оконного менеджера равнозначно ответу «Нет».
            // Иначе окно возвращает согласие, которого пользователь не давал.
            Closing += (_, _) =>
            {
                if (!_answered)
                    Confirmed = false;
            };
            buttonsPanel.Children.Add(cancelButton);
        }

        var content = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(16)
        };
        content.Children.Add(body);

        // «Что нового»: рендерим markdown описания релиза со стилями (заголовки,
        // жирный, списки и т.п.) под основным сообщением, если описание задано.
        if (markdownBody is not null)
        {
            var effective = string.IsNullOrWhiteSpace(markdownBody)
                ? LocalizationManager.T("Update.NoDescription")
                : markdownBody;

            var whatsNewHeading = new TextBlock
            {
                Text = LocalizationManager.T("Update.WhatsNew"),
                FontWeight = FontWeight.SemiBold,
                FontSize = 12
            };
            ThemeBrushes.Bind(whatsNewHeading, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");
            content.Children.Add(whatsNewHeading);

            content.Children.Add(new ScrollViewer
            {
                MaxHeight = 180,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = MarkdownRenderer.ToStackPanel(effective)
            });
        }

        content.Children.Add(buttonsPanel);

        Content = content;
    }

    /// <summary>
    /// ESC закрывает окно сообщения (issue #47), как кнопка «Нет»/«Отмена»:
    /// для подтверждений результат — «Нет», для информационных окон — просто закрытие.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && !e.Handled)
        {
            _answered = true;
            Confirmed = false;
            e.Handled = true;
            Close();
            return;
        }

        // issue #100 (Linux): Space/Enter нажимают кнопку по умолчанию диалога, даже
        // если фокус не на кнопке (например, клавиатура осталась у перекрытого окна).
        // Если фокус на кнопке — не перехватываем: Space/Enter обрабатывает сама
        // кнопка, иначе нажатие задваивается. Чистая логика — ModalKeysResolver.
        if (!e.Handled && ModalKeysResolver.ShouldActivateDefaultButton(
                TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Button, e.Key.ToString(),
                e.KeyModifiers != KeyModifiers.None))
        {
            _answered = true;
            Confirmed = true;
            e.Handled = true;
            Close();
            return;
        }

        base.OnKeyDown(e);
    }
}
#endif