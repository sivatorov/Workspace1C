#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Services.Licensing;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Как купить ПРО-версию»: пошаговая инструкция покупки (код компьютера,
    /// контакты, оплата, получение и активация ключа). Avalonia/Linux; открывается
    /// кнопкой «Купить» окна «Лицензия» (см. docs/pro/PRO-EDITION.md).
    /// </summary>
    public sealed class HowToBuyWindow : ModalWindowBase
    {
        private static readonly string[] StepKeys =
        {
            "Pro.HowToBuy.Step1",
            "Pro.HowToBuy.Step2",
            "Pro.HowToBuy.Step3",
            "Pro.HowToBuy.Step4",
            "Pro.HowToBuy.Step5"
        };

        private readonly HowToBuyViewModel _vm;

        public HowToBuyWindow()
        {
            _vm = new HowToBuyViewModel();
            DataContext = _vm;
            Title = LocalizationManager.T("Pro.HowToBuy.Title");
            // Issue #165 (1.0.1.120): окно открывается по содержимому (720×720 DIP),
            // а не 620×600 — прежняя высота обрезала нижнюю панель кнопок на низких
            // экранах и при масштабировании 125/150 %.
            Width = 720;
            Height = 720;
            MinWidth = 560;
            MinHeight = DialogWorkAreaPolicy.HowToBuy.MinHeight;
            FontSize = 13;
            CanResize = true;

            // Issue #165: прокручивается только содержимое (строка *), а строка
            // статуса и нижняя панель кнопок — Auto. При Auto у ScrollViewer он
            // «просит» высоту всего текста и выталкивает кнопки за край окна.
            var root = AvaloniaUi.Rows(-1, 0, 0);
            root.Margin = new Thickness(16);

            var panel = new StackPanel();

            var price = AvaloniaUi.Text(LocalizationManager.T("Pro.HowToBuy.PriceHeader"), 13, bold: true);
            price.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(price);

            // Явно про major: для 1.x ключ выпускается с major=1 (ключи 2.x не подойдут).
            var majorNote = AvaloniaUi.Text(LocalizationManager.T("Pro.HowToBuy.MajorNote"), 12, secondary: true);
            majorNote.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(majorNote);

            for (var i = 0; i < StepKeys.Length; i++)
            {
                var step = AvaloniaUi.Text($"{i + 1}. {LocalizationManager.T(StepKeys[i])}", 13);
                step.Margin = new Thickness(0, 0, 0, 6);
                panel.Children.Add(step);
            }

            // Шаг 1: код компьютера + кнопка копирования (механизм окна «Лицензия»).
            var machine = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            machine.Children.Add(AvaloniaUi.Label("Pro.MachineCode"));
            var codeBox = new TextBox
            {
                Text = _vm.MachineCodeText,
                IsReadOnly = true,
                FontSize = ThemeManager.FontSizeTitle,
                FontFamily = new FontFamily(ThemeManager.DefaultMonospaceFontFamily),
                Width = 260
            };
            var copy = AvaloniaUi.Button("Pro.HowToBuy.CopyMachineCode", async (_, _) =>
            {
                try
                {
                    var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                    if (clipboard is not null)
                    {
                        await clipboard.SetTextAsync(_vm.MachineCode);
                        _vm.ReportCodeCopied();
                    }
                }
                catch
                {
                    // Буфер обмена недоступен (Wayland без менеджера буфера) — код можно выделить вручную.
                }
            }, 200);
            machine.Children.Add(AvaloniaUi.Wrap(codeBox, copy));
            panel.Children.Add(AvaloniaUi.Card(machine));

            // Контакты: Telegram-канал и Issues репозитория (без личных реквизитов),
            // прежняя GitHub-ссылка — справочная кнопка «Подробнее в README».
            var contacts = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            contacts.Children.Add(AvaloniaUi.Section("Pro.HowToBuy.ContactsHeader"));
            contacts.Children.Add(AvaloniaUi.Wrap(
                AvaloniaUi.Button("Pro.HowToBuy.OpenTelegram", (_, _) => OpenContact(ProFeatures.TelegramUrl), 160),
                AvaloniaUi.Button("Pro.HowToBuy.OpenIssues", (_, _) => OpenContact(ProFeatures.IssuesUrl), 150),
                AvaloniaUi.Button("Pro.HowToBuy.OpenReadme", (_, _) => OpenContact(ProFeatures.BuyUrl), 180)));
            panel.Children.Add(AvaloniaUi.Card(contacts));

            // Что остаётся бесплатным (public core 1.x) — чтобы покупка была осознанной.
            var free = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            free.Children.Add(AvaloniaUi.Section("Pro.License.WhatFreeHeader"));
            free.Children.Add(AvaloniaUi.Text(LocalizationManager.T("Pro.License.WhatFree"), 12, secondary: true));
            panel.Children.Add(AvaloniaUi.Card(free));

            var scroll = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            AvaloniaUi.Place(root, scroll, 0);

            AvaloniaUi.Place(root, AvaloniaUi.BoundText(nameof(HowToBuyViewModel.StatusText), 12, secondary: false), 1);

            // Шаг 5: активация ключа — окно «Лицензия» (как принято в проекте).
            var activate = AvaloniaUi.Button("Pro.Activate", (_, _) =>
            {
                new LicenseWindow().ShowDialogSync(this);
            }, 140, accent: true);
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = AvaloniaUi.ButtonRow(activate, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            AvaloniaUi.Place(root, buttons, 2);

            Content = root;

            // Issue #165: общая политика размеров — высота/минимум по содержимому с
            // клампом по рабочей области (на 1366×768, масштаб 100 % — окно 680 DIP,
            // при масштабе 150 % — 432 DIP), ширина тоже не выходит за экран.
            ApplyWorkAreaLimits();
        }

        /// <summary>
        /// Issue #165 (1.0.1.120): применяет <see cref="DialogWorkAreaPolicy.HowToBuy"/>
        /// к текущему монитору. Кнопки закреплены вне прокрутки, поэтому кламп высоты
        /// не скрывает их: прокручивается только текст инструкции.
        /// </summary>
        private void ApplyWorkAreaLimits()
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null || screen.Scaling <= 0)
                return;

            var dipWidth = screen.WorkingArea.Width / screen.Scaling;
            var dipHeight = screen.WorkingArea.Height / screen.Scaling;
            if (dipWidth <= 0 || dipHeight <= 0)
                return;

            var size = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.HowToBuy, dipHeight);
            MaxHeight = size.MaxHeight;
            MinHeight = size.MinHeight;
            Height = size.Height;

            var maxWidth = dipWidth - DialogWorkAreaPolicy.DefaultMargin;
            if (maxWidth <= 0)
                return;

            MaxWidth = maxWidth;
            if (MinWidth > maxWidth)
                MinWidth = maxWidth;
            Width = WindowSizeMath.ClampToWorkArea(Width, maxWidth);
        }

        private void OpenContact(string url)
        {
            if (!Services.OneCLauncher.OpenUrl(url))
                _vm.ReportLinkOpenFailed();
        }
    }
}
#endif
