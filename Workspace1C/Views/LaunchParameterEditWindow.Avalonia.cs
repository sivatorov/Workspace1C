#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог правки пользовательского параметра справочника окна «Запустить с выбором
    /// параметров» (issue #68): ключ командной строки и описание. Avalonia/Linux-версия
    /// WPF-окна <see cref="LaunchParameterEditWindow"/>. Работает на копии данных;
    /// результат возвращается через <see cref="Key"/>/<see cref="Comment"/>.
    /// </summary>
    public sealed class LaunchParameterEditWindow : ModalWindowBase
    {
        private readonly TextBox _keyBox;
        private readonly TextBox _commentBox;

        /// <param name="key">Текущий ключ параметра.</param>
        /// <param name="comment">Текущее описание параметра.</param>
        public LaunchParameterEditWindow(string? key, string? comment)
        {
            Title = LocalizationManager.T("LaunchParams.EditTitle");
            Width = 480;
            Height = 240;
            MinWidth = 440;
            MinHeight = 220;
            CanResize = false;
            FontSize = 13;

            _keyBox = new TextBox
            {
                Text = key ?? string.Empty,
                MinHeight = 32,
                Padding = new Thickness(6, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _keyBox.Styled(ControlThemes.ModernTextBox);
            _keyBox.TextChanged += (_, _) => UpdateSaveState();

            _commentBox = new TextBox
            {
                Text = comment ?? string.Empty,
                MinHeight = 32,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(6, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _commentBox.Styled(ControlThemes.ModernTextBox);

            Content = BuildRoot();

            Opened += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _keyBox.Focus();
                    _keyBox.SelectAll();
                }, DispatcherPriority.Background);
            };
        }

        /// <summary>Новое значение ключа (после подтверждения).</summary>
        public string? Key => _keyBox.Text;

        /// <summary>Новое значение описания (после подтверждения).</summary>
        public string? Comment => _commentBox.Text;

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private Control BuildRoot()
        {
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 4 };

            panel.Children.Add(MakeFieldRow(T("LaunchParams.Parameter"), _keyBox));
            panel.Children.Add(MakeFieldRow(T("LaunchParams.Description"), _commentBox));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 16, 0, 0),
                Children =
                {
                    BuildConfirmActionButton("Common.Save", "IconCheck", 130, OnSaveClick),
                    BuildCancelActionButton(120)
                }
            };
            panel.Children.Add(buttons);

            return panel;
        }

        private static Control MakeFieldRow(string label, Control editor)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 130 });
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var caption = new TextBlock
            {
                Text = label,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            // Issue #61: подпись поля формы — единая кисть FormLabelBrush (не вторичная
            // и не фиксированный тёмный цвет), читаема в обеих темах и схемах.
            Themes.ThemeBrushes.Bind(caption, TextBlock.ForegroundProperty, "FormLabelBrush");
            Grid.SetColumn(caption, 0);
            grid.Children.Add(caption);

            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);
            return grid;
        }

        private void UpdateSaveState()
        {
            // Кнопка подтверждения всегда доступна; пустой ключ отклоняется при сохранении.
        }

        private void OnSaveClick()
        {
            if (string.IsNullOrWhiteSpace(_keyBox.Text))
            {
                _keyBox.Focus();
                return;
            }

            DialogResult = true;
        }
    }
}
#endif