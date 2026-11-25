#if WINDOWS
using System;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Services;
using Workspace1C.Services.Licensing;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Как купить ПРО-версию»: пошаговая инструкция покупки (код компьютера,
    /// контакты, оплата, получение и активация ключа). Windows/WPF; открывается кнопкой
    /// «Купить» окна «Лицензия» (см. docs/pro/PRO-EDITION.md).
    /// </summary>
    public sealed class HowToBuyWindow : Window
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
            // Issue #165 (1.0.1.120): окно открывается по содержимому (720×720 DIP),
            // а не 620×600 — прежняя высота обрезала нижнюю панель кнопок на низких
            // экранах и при масштабировании 125/150 %.
            WpfUi.SetupWindow(this, "Pro.HowToBuy.Title", 720, 720);

            // Issue #165: общая политика размеров — высота/минимум по содержимому с
            // клампом по рабочей области (на 1366×768, масштаб 100 % — окно 680 DIP,
            // при масштабе 150 % — 432 DIP, прокрутка остаётся аварийным режимом).
            var workArea = SystemParameters.WorkArea;
            var size = DialogWorkAreaPolicy.Recommend(DialogWorkAreaPolicy.HowToBuy, workArea.Height);
            MinHeight = size.MinHeight;
            MaxHeight = size.MaxHeight;
            Height = size.Height;

            // Issue #165: прокручивается только содержимое (строка *), а строка
            // статуса и нижняя панель кнопок — Auto. При Auto у ScrollViewer он
            // «просит» высоту всего текста и выталкивает кнопки за край окна.
            var root = WpfUi.Rows(-1, 0, 0);
            root.Margin = new Thickness(16);

            var panel = new StackPanel();

            var price = WpfUi.Text(Localization.LocalizationManager.T("Pro.HowToBuy.PriceHeader"), 13, bold: true);
            price.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(price);

            // Явно про major: для 1.x ключ выпускается с major=1 (ключи 2.x не подойдут).
            var majorNote = WpfUi.Text(Localization.LocalizationManager.T("Pro.HowToBuy.MajorNote"), 12, secondary: true);
            majorNote.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(majorNote);

            for (var i = 0; i < StepKeys.Length; i++)
            {
                var step = WpfUi.Text($"{i + 1}. {Localization.LocalizationManager.T(StepKeys[i])}", 13);
                step.Margin = new Thickness(0, 0, 0, 6);
                panel.Children.Add(step);
            }

            // Шаг 1: код компьютера + кнопка копирования (механизм окна «Лицензия»).
            var machine = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            machine.Children.Add(WpfUi.Label("Pro.MachineCode"));
            var codeBox = new TextBox
            {
                Text = _vm.MachineCodeText,
                IsReadOnly = true,
                FontSize = 18,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Width = 260,
                Margin = new Thickness(0, 4, 8, 0)
            };
            var copy = WpfUi.Button("Pro.HowToBuy.CopyMachineCode", (_, _) =>
            {
                try
                {
                    Clipboard.SetText(_vm.MachineCode);
                    _vm.ReportCodeCopied();
                }
                catch
                {
                    // буфер обмена занят другим приложением — код можно выделить вручную
                }
            }, 200);
            machine.Children.Add(WpfUi.Wrap(codeBox, copy));
            panel.Children.Add(WpfUi.Card(machine));

            // Контакты: Telegram-канал и Issues репозитория (без личных реквизитов),
            // прежняя GitHub-ссылка — справочная кнопка «Подробнее в README».
            var contacts = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            contacts.Children.Add(WpfUi.Section("Pro.HowToBuy.ContactsHeader"));
            contacts.Children.Add(WpfUi.Wrap(
                WpfUi.Button("Pro.HowToBuy.OpenTelegram", (_, _) => OpenContact(ProFeatures.TelegramUrl), 160),
                WpfUi.Button("Pro.HowToBuy.OpenIssues", (_, _) => OpenContact(ProFeatures.IssuesUrl), 150),
                WpfUi.Button("Pro.HowToBuy.OpenReadme", (_, _) => OpenContact(ProFeatures.BuyUrl), 180)));
            panel.Children.Add(WpfUi.Card(contacts));

            // Что остаётся бесплатным (public core 1.x) — чтобы покупка была осознанной.
            var free = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            free.Children.Add(WpfUi.Section("Pro.License.WhatFreeHeader"));
            free.Children.Add(WpfUi.Text(Localization.LocalizationManager.T("Pro.License.WhatFree"), 12, secondary: true));
            panel.Children.Add(WpfUi.Card(free));

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panel
            };
            WpfUi.Place(root, scroll, 0);

            WpfUi.Place(root, WpfUi.BoundText(nameof(HowToBuyViewModel.StatusText), 12, secondary: false), 1);

            // Шаг 5: активация ключа — окно «Лицензия» (как принято в проекте).
            var activate = WpfUi.Button("Pro.Activate", (_, _) =>
            {
                new LicenseWindow().ShowDialog();
            }, 140, accent: true);
            var close = WpfUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = WpfUi.ButtonRow(activate, close);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 12, 0, 0);
            WpfUi.Place(root, buttons, 2);

            Content = root;
        }

        private void OpenContact(string url)
        {
            if (!Workspace1C.Services.OneCLauncher.OpenUrl(url))
                _vm.ReportLinkOpenFailed();
        }
    }
}
#endif
