#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Services.Licensing;
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
            Width = 620;
            Height = 600;
            MinWidth = 520;
            MinHeight = 480;
            FontSize = 13;
            CanResize = true;

            var root = AvaloniaUi.Rows(0, -1, 0);
            root.Margin = new Thickness(16);

            var panel = new StackPanel();

            var price = AvaloniaUi.Text(LocalizationManager.T("Pro.HowToBuy.PriceHeader"), 13, bold: true);
            price.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(price);

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
                FontSize = 18,
                FontFamily = new FontFamily("monospace"),
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

            var scroll = new ScrollViewer { Content = panel };
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
        }

        private void OpenContact(string url)
        {
            if (!Services.OneCLauncher.OpenUrl(url))
                _vm.ReportLinkOpenFailed();
        }
    }
}
#endif
