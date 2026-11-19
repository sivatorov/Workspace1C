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

    /// <summary>
    /// Issue #139 (1.0.1.95): диалог с явным размером по содержимому (окно
    /// обновления и подтверждение перезапуска). Высота считается в
    /// <see cref="ApplyFitContentSize"/>, а не через <c>SizeToContent</c> по обеим осям.
    /// </summary>
    private readonly bool _fitContent;

    // Оценка неклиентской области до первого показа (рамка + заголовок), DIP.
    // После открытия окна заменяется точной разницей Bounds/ClientSize; здесь
    // только начальное приближение, чтобы первый кадр не был нулевым. Значения
    // оформления собраны в одном месте, а не «на глаз» по коду.
    private const double FallbackChromeWidth = 16;
    private const double FallbackChromeHeight = 40;

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
        _fitContent = fitContent;
        if (fitContent)
        {
            // Issue #139 (1.0.1.95): явный размер по содержимому. Авторазмер по
            // обеим осям давал ту же ошибку, что и WPF: WrapPanel измерялся с
            // бесконечной шириной (одна строка), затем ширина клампилась, кнопки
            // переносились, а высота уже была посчитана — низ обрезался.
            // Ширина/высота вычисляются в ApplyFitContentSize при конечной ширине,
            // тело прокручивается, панель кнопок закреплена снизу.
            MinWidth = 440;   // UpdateDialogSizePolicy.MinWidth
            MaxWidth = 760;   // UpdateDialogSizePolicy.MaxWidth
            MaxHeight = 640;  // UpdateDialogSizePolicy.MaxHeight
            MinHeight = 140;  // UpdateDialogSizePolicy.MinHeight
            SizeToContent = SizeToContent.Manual;
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

        var bodyContent = new StackPanel { Spacing = 16 };
        bodyContent.Children.Add(body);

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
            bodyContent.Children.Add(whatsNewHeading);

            bodyContent.Children.Add(new ScrollViewer
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
            bodyContent.Children.Add(notesLink);
        }

        if (_fitContent)
        {
            // Issue #139 (1.0.1.95): тело прокручивается, панель кнопок закреплена
            // снизу и не обрезается. Высота окна считается явно, а не авторазмером
            // по обеим осям (тот не учитывал перенос кнопок WrapPanel).
            var bodyScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = bodyContent
            };

            var dock = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(16)
            };
            buttonsPanel.Margin = new Thickness(0, 16, 0, 0);
            DockPanel.SetDock(buttonsPanel, Dock.Bottom);
            dock.Children.Add(buttonsPanel);
            dock.Children.Add(bodyScroll);
            Content = dock;

            // Первичный размер — до первого кадра; после Opened уточняется
            // фактической неклиентской областью и рабочей областью монитора.
            ApplyFitContentSize();
        }
        else
        {
            bodyContent.Margin = new Thickness(16);
            bodyContent.Children.Add(buttonsPanel);
            Content = bodyContent;
        }
    }

    /// <summary>
    /// Issue #139: ограничивает окно рабочей областью монитора.
    /// Для <c>fitContent</c> (окно обновления, подтверждение перезапуска) размер
    /// считается явно в <see cref="ApplyFitContentSize"/>: ширина и высота по
    /// фактическому содержимому при конечной ширине, кламп в <c>[Min*, Max*]</c> и
    /// рабочую область с отступами. Для диалогов с фиксированной шириной (обычные
    /// сообщения) ограничиваем саму ширину/высоту.
    /// <see cref="Screen.WorkingArea"/> задана в физических пикселях, поэтому
    /// переводим её в DIP через масштаб окна.
    /// </summary>
    private void ClampToScreen()
    {
        if (_fitContent)
        {
            ApplyFitContentSize();
            return;
        }

        var (workWidth, workHeight) = GetWorkAreaDip();
        if (workWidth <= 0 || workHeight <= 0)
            return;

        var clamped = UpdateDialogSizePolicy.Clamp(
            double.IsNaN(Width) ? MaxWidth : Width,
            double.IsNaN(Height) ? MaxHeight : Height,
            workWidth,
            workHeight);

        MaxWidth = clamped.Width;

        // Issue #139 (1.0.1.84): фиксированная ширина/высота (fitContent=false)
        // тоже не должна выходить за рабочую область.
        if (!double.IsNaN(Width) && Width > clamped.Width)
            Width = clamped.Width;
        if (!double.IsNaN(Height) && Height > clamped.Height)
            Height = clamped.Height;
    }

    /// <summary>
    /// Issue #139 (1.0.1.95): явный размер окна с содержимым по ширине. Ширина
    /// измеряется без ограничения и клампится политикой; высота — уже при итоговой
    /// ширине, поэтому <c>WrapPanel</c> переносит кнопки как в реальной раскладке.
    /// Если тело выше доступной высоты, прокручивается только оно (ScrollViewer в
    /// <c>Content</c>), панель кнопок остаётся видимой.
    /// </summary>
    private void ApplyFitContentSize()
    {
        if (!_fitContent || Content is not Control content)
            return;

        try
        {
            var (workWidth, workHeight) = GetWorkAreaDip();

            // 1. Желаемая ширина по содержимому (политика клампит до MaxWidth).
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var width = UpdateDialogSizePolicy.Clamp(
                content.DesiredSize.Width + ChromeWidth(), 0, workWidth, 0).Width;

            // 2. Желаемая высота при итоговой ширине — с учётом переноса кнопок.
            content.Measure(new Size(
                Math.Max(0, width - ChromeWidth()), double.PositiveInfinity));
            var height = UpdateDialogSizePolicy.ClampWindowHeight(
                content.DesiredSize.Height + ChromeHeight(), workHeight);

            MaxWidth = UpdateDialogSizePolicy.Clamp(
                UpdateDialogSizePolicy.MaxWidth, 0, workWidth, 0).Width;

            // MinHeight не должен быть больше MaxHeight (крошечный экран) —
            // иначе окно нельзя корректно ограничить рабочей областью.
            var maxHeight = UpdateDialogSizePolicy.ClampWindowHeight(
                UpdateDialogSizePolicy.MaxHeight, workHeight);
            MinHeight = Math.Min(UpdateDialogSizePolicy.MinHeight, maxHeight);
            MaxHeight = maxHeight;

            Width = width;
            Height = height;
        }
        catch
        {
            // Сбой измерения не должен мешать показу диалога.
        }
    }

    /// <summary>Рабочая область монитора в DIP (0×0 — неизвестна).</summary>
    private (double Width, double Height) GetWorkAreaDip()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return (0, 0);

        var scaling = RenderScaling > 0 ? RenderScaling : screen.Scaling;
        if (scaling <= 0)
            scaling = 1.0;

        var area = screen.WorkingArea;
        return (area.Width / scaling, area.Height / scaling);
    }

    /// <summary>
    /// Ширина неклиентской области окна, DIP. После показа известна точно как
    /// разница между габаритом и клиентской шириной; до показа — оценка
    /// <see cref="FallbackChromeWidth"/>.
    /// </summary>
    private double ChromeWidth()
        => Bounds.Width > ClientSize.Width && ClientSize.Width > 0
            ? Bounds.Width - ClientSize.Width
            : FallbackChromeWidth;

    /// <summary>
    /// Высота неклиентской области окна, DIP. После показа — точная разница
    /// габарита и клиентской высоты; до показа — оценка
    /// <see cref="FallbackChromeHeight"/>.
    /// </summary>
    private double ChromeHeight()
        => Bounds.Height > ClientSize.Height && ClientSize.Height > 0
            ? Bounds.Height - ClientSize.Height
            : FallbackChromeHeight;

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
