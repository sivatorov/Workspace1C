#if LINUX
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Сменить платформу» (issue #54, переработка по комментарию 7OH, Avalonia/Linux):
    /// выбор версии платформы 1С из установленных и замена её у ВЫДЕЛЕННЫХ баз по «Сменить».
    /// Вся логика — в чистой <see cref="PlatformChangeViewModel"/> (применение через
    /// <c>ConnectionReplacementPlanner.ApplyPlatform</c>, undo через
    /// <see cref="MainViewModel.ApplyConnectionReplace"/>); окно — тонкая обёртка (аналог
    /// <see cref="ConnectionReplaceWindow"/>): ComboBox версий, сводка и кнопки
    /// «Сменить»/«Отмена». По «Сменить» — команда VM и закрытие с DialogResult=true.
    /// </summary>
    public sealed class PlatformChangeWindow : ModalWindowBase
    {
        private readonly PlatformChangeViewModel _vm;

        /// <param name="vm">Готовый ViewModel окна (кандидаты и колбэк формирует MainViewModel).</param>
        public PlatformChangeWindow(PlatformChangeViewModel vm)
        {
            _vm = vm ?? throw new ArgumentNullException(nameof(vm));

            Title = LocalizationManager.T("PlatformChange.Title");
            Width = 460;
            CanResize = true;
            // Минимальные размеры (issue #55): при уменьшении окна содержимое не сжимается
            // до обрезки — дальше нижней границы окно не уменьшается, а прокрутка
            // (ScrollViewer ниже) страхует от обрезки по вертикали.
            MinWidth = 380;
            MinHeight = 240;
            FontSize = 13;
            DataContext = _vm;

            // ---- Выбор версии платформы. ----
            var platformCombo = new ComboBox
            {
                // Фиксированная ширина 260 уезжала за правый край окна при уменьшении
                // ширины/крупных шрифтах (issue #55): поле растягивается в звёздной
                // колонке Grid ниже, а не прячется.
                MinWidth = 180,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            platformCombo.Styled(ControlThemes.ModernComboBox);
            platformCombo.ItemsSource = _vm.InstalledPlatformItems;
            // Тонкие клиенты (issue #54) помечаются локализованным суффиксом
            // «(тонкий клиент)» (DisplayName — единый форматтер, issue #33) и
            // подсвечиваются оранжевым, как в настройках.
            platformCombo.ItemTemplate = new FuncDataTemplate<PlatformVersionInfo>((item, _) =>
            {
                var text = new TextBlock
                {
                    Text = item?.DisplayName ?? string.Empty,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (item is { IsThinClient: true })
                    text.Foreground = Themes.ThemeBrushes.Get("WarningBrush", "#D97706");
                else
                    ThemeBrushes.Bind(text, TextBlock.ForegroundProperty, "TextPrimaryBrush");
                return text;
            });
            // В Avalonia нет SelectedValuePath (см. UtilitiesMenuEditorWindow): выбранную
            // версию пишем в VM вручную по событию, а начальный выбор (предзаполнение
            // первой версии в VM) восстанавливаем по SelectedPlatform.
            platformCombo.SelectedItem = _vm.InstalledPlatformItems
                .FirstOrDefault(i => i.Display == _vm.SelectedPlatform);
            platformCombo.SelectionChanged += (_, _) =>
            {
                if (platformCombo.SelectedItem is PlatformVersionInfo item)
                    _vm.SelectedPlatform = item.Display;
            };

            // ---- Сводка и результат. ----
            var summary = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 12, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            summary.Bind(TextBlock.TextProperty, new Binding("SummaryText"));
            var result = new TextBlock
            {
                FontSize = 12,
                Opacity = 0.85,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            result.Bind(TextBlock.TextProperty, new Binding("ResultText"));

            // ---- Кнопки: Сменить / Отмена. ----
            var applyButton = new Button
            {
                // Текст берёт контрастный цвет ModernButton (TextOnAccentBrush),
                // жёсткий белый убран (issue #26).
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock { Text = "✓", VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = LocalizationManager.T("PlatformChange.ApplyButton"), VerticalAlignment = VerticalAlignment.Center }
                    }
                },
                Padding = new Thickness(16, 6)
            };
            applyButton.Styled(ControlThemes.ModernButton);
            applyButton.Bind(Button.IsEnabledProperty, new Binding("CanApply"));
            applyButton.Click += (_, _) => OnApplyClick();

            var cancelButton = new Button
            {
                Content = LocalizationManager.T("Common.Cancel"),
                Padding = new Thickness(16, 6)
            };
            cancelButton.Styled(ControlThemes.SecondaryButton);
            cancelButton.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0),
                Children = { applyButton, cancelButton }
            };

            // Строка «подпись + поле»: сетка Auto+* вместо горизонтального StackPanel —
            // при уменьшении окна комбобокс сжимается вместе со звёздной колонкой,
            // а не обрезается правым краем (issue #55).
            var labelRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            Grid.SetColumn(platformCombo, 1);
            labelRow.Children.Add(FieldLabel("PlatformChange.PlatformLabel"));
            labelRow.Children.Add(platformCombo);

            var panel = new StackPanel
            {
                Margin = new Thickness(20, 16),
                Children =
                {
                    labelRow,
                    summary,
                    result,
                    buttons
                }
            };
            // Прокрутка по вертикали (issue #55): при малой высоте окна содержимое
            // скроллится, а не обрезается.
            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        private void OnApplyClick()
        {
            _vm.ApplyCommand.Execute(null);
            DialogResult = true;
            Close();
        }

        /// <summary>Подпись поля формы по ключу локализации (issue #61): единая кисть
        /// FormLabelBrush — читаема в обеих темах и пользовательских схемах.</summary>
        private static TextBlock FieldLabel(string textKey)
        {
            var block = new TextBlock
            {
                Text = LocalizationManager.T(textKey),
                VerticalAlignment = VerticalAlignment.Center
            };
            ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "FormLabelBrush");
            return block;
        }
    }
}
#endif