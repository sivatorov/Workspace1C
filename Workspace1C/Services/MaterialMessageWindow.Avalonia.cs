#if LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Controls;
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

    /// <summary>Нажата кнопка пропуска («Пропустить эту версию», issue #116).</summary>
    public bool SkipClicked { get; private set; }

    /// <summary>Ответ дан кнопкой окна, а не закрытием через оконный менеджер.</summary>
    private bool _answered;

    /// <summary>Кнопка по умолчанию — для фокуса при открытии и клавиш Space/Enter (issue #100).</summary>
    private Button _okButton = null!;

    /// <param name="markdownBody">
    /// Необязательный текст «Что нового» в формате markdown. Показывается под
    /// основным сообщением с рендером стилей (заголовки, жирный, списки). При
    /// пустом значении выводится заглушка о недоступном описании.
    /// </param>
    /// <param name="skipButtonText">
    /// Необязательный текст третьей кнопки пропуска (issue #116): задаётся для
    /// диалога обновления, чтобы пользователь мог «пропустить эту версию». При
    /// пустом значении кнопка не добавляется.
    /// </param>
    /// <param name="okButtonText">
    /// Необязательный текст основной кнопки (issue #116, 1.0.1.82): в диалоге
    /// обновления вместо «Да» показывается «Обновить».
    /// </param>
    /// <param name="notesLinkText">
    /// Необязательный текст ссылки «Что нового» (issue #116, 1.0.1.82): показывается
    /// под сообщением в ручных режимах, когда блок описания скрыт.
    /// </param>
    /// <param name="onNotesLink">Действие по клику на ссылку «Что нового» (диалог не закрывается).</param>
    /// <param name="fitContent">
    /// Issue #116 (17.11.2026 14:07): подгонять окно под содержимое по обеим осям.
    /// Для диалога обновления — true: окно сразу открывается нужного размера и не
    /// меняет его скачком. Для обычных сообщений остаётся прежняя фиксированная
    /// ширина и высота по содержимому.
    /// </param>
    public MaterialMessageWindowAvalonia(
        string message, string title, MaterialMessageKind kind, string? markdownBody = null,
        string? skipButtonText = null, string? okButtonText = null,
        string? notesLinkText = null, Action? onNotesLink = null, bool fitContent = false)
    {
        Title = title;
        if (fitContent)
        {
            // Ширина и высота — по содержимому в разумных пределах; CanResize=false
            // запрещает менять размер мышью. Никакого ручного измерения после показа:
            // Avalonia считает размер до первого отображения, скачка нет.
            // Issue #139 (1.0.1.84): MaxHeight ограничивает окно до первого кадра —
            // длинное описание релиза не открывает окно выше экрана.
            MinWidth = 440;
            MaxWidth = 760;
            MaxHeight = 640;
            SizeToContent = SizeToContent.WidthAndHeight;
        }
        else
        {
            Width = 440;
            SizeToContent = SizeToContent.Height;
        }
        // CanResize=false запрещает перетаскивание границ оконным менеджером
        // (issue #116 v1.0.1.54 — окна обновления не должны растягиваться).
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.Full;

        // Issue #139 (1.0.1.84): после открытия ограничиваем окно рабочей областью
        // монитора — оконный менеджер Linux мог изменить ширину, и кнопки обрезались.
        Opened += (_, _) => ClampToScreen();

        ThemeBrushes.Bind(this, TemplatedControl.BackgroundProperty, "ContentBackgroundColorBrush");

        // Иконка типа сообщения.
        var iconKey = kind switch
        {
            MaterialMessageKind.Warning => "IconWarning",
            MaterialMessageKind.Error => "IconError",
            MaterialMessageKind.Question => "IconUnknown",
            _ => "IconInfo"
        };
        var messageIcon = Workspace1C.IconHelper.MakeIcon(iconKey, UiMetrics.IconHeader);
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
        var okText = !string.IsNullOrWhiteSpace(okButtonText)
            ? okButtonText
            : kind == MaterialMessageKind.Question
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
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        okButton.Styled(ControlThemes.ModernButton);
        okButton.FontWeight = FontWeight.SemiBold;
        okButton.Click += (_, _) => { _answered = true; Confirmed = true; Close(); };
        _okButton = okButton;

        // issue #100 (Linux): диалог сообщений при открытии обязан забрать фокус на
        // кнопку по умолчанию — иначе клавиатура оставалась у владельца, и пробел
        // «нажимал» меню перекрытого окна вместо кнопки «ОК». На Windows фокус
        // устанавливает WPF (IsDefault), на Linux/X11 требуется явная установка
        // после показа окна (аналогично паттерну #299).
        Opened += (_, _) => Dispatcher.UIThread.Post(() => _okButton.Focus());

        // Порядок кнопок согласован с WPF-версией: «Да» (подтверждение) слева,
        // «Нет» (отмена) справа. Issue #139 (1.0.1.84): WrapPanel вместо
        // горизонтального StackPanel — при узкой ширине кнопки переносятся,
        // а не обрезаются справа.
        var buttonsPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttonsPanel.Children.Add(okButton);

        if (kind == MaterialMessageKind.Question)
        {
            // Кнопка пропуска (issue #116) — вторичная SecondaryButton, между
            // «Да» и «Нет»; Esc и закрытие крестиком пропуском не считаются.
            if (!string.IsNullOrWhiteSpace(skipButtonText))
            {
                Button skipButton = new()
                {
                    Content = skipButtonText,
                    MinWidth = 120,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                skipButton.Styled(ControlThemes.SecondaryButton);
                skipButton.Click += (_, _) =>
                {
                    _answered = true;
                    SkipClicked = true;
                    Confirmed = false;
                    Close();
                };
                buttonsPanel.Children.Add(skipButton);
            }

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

        // Issue #116 (1.0.1.82): ссылка «Что нового» — открывает стандартный диалог
        // изменений новой версии, не закрывая этот диалог.
        if (!string.IsNullOrWhiteSpace(notesLinkText))
        {
            Button notesLink = new()
            {
                Content = notesLinkText,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 0,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0)
            };
            notesLink.Styled(ControlThemes.SecondaryButton);
            notesLink.Click += (_, _) => onNotesLink?.Invoke();
            content.Children.Add(notesLink);
        }

        content.Children.Add(buttonsPanel);

        Content = content;
    }

    /// <summary>
    /// Issue #139 (1.0.1.84): ограничивает окно рабочей областью монитора.
    /// Размер по содержимому (<see cref="SizeToContent.WidthAndHeight"/>) сохраняется,
    /// но окно не должно оказаться шире/выше экрана — иначе кнопки и текст
    /// обрезаются. <see cref="Screen.WorkingArea"/> задана в физических пикселях,
    /// поэтому переводим её в DIP через масштаб окна. Для диалогов с фиксированной
    /// шириной (обычные сообщения, диалог перезапуска) ограничиваем саму ширину.
    /// </summary>
    private void ClampToScreen()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen is null)
            return;

        var scaling = RenderScaling > 0 ? RenderScaling : screen.Scaling;
        if (scaling <= 0)
            scaling = 1.0;

        var workArea = screen.WorkingArea;
        var clamped = UpdateDialogSizePolicy.Clamp(
            double.IsNaN(Width) ? MaxWidth : Width,
            double.IsNaN(Height) ? MaxHeight : Height,
            workArea.Width / scaling,
            workArea.Height / scaling);

        MaxWidth = clamped.Width;
        if (SizeToContent == SizeToContent.WidthAndHeight)
        {
            MaxHeight = clamped.Height;
            return;
        }

        // Issue #139 (1.0.1.84): фиксированная ширина/высота (fitContent=false)
        // тоже не должна выходить за рабочую область.
        if (!double.IsNaN(Width) && Width > clamped.Width)
            Width = clamped.Width;
        if (!double.IsNaN(Height) && Height > clamped.Height)
            Height = clamped.Height;
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
