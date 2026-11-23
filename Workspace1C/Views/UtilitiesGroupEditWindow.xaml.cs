#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог создания/правки группы меню «Утилиты» (issue #374): имя группы и иконка
    /// из каталога (с превью). Результат — <see cref="UtilitiesGroupEditParams"/> через
    /// <see cref="Result"/> (null — отмена).
    /// </summary>
    public partial class UtilitiesGroupEditWindow : Window
    {
        /// <param name="icons">Доступные иконки каталога (из ViewModel редактора).</param>
        /// <param name="initialTitle">Текущее имя группы (для правки) или пусто.</param>
        /// <param name="initialIconKey">Текущий ключ иконки (для правки) или пусто.</param>
        /// <param name="initialIconColor">Текущий цвет иконки (#RRGGBB) для синтетического
        /// варианта, если ключа нет в каталоге (issue #146).</param>
        /// <param name="initialDisplayMode">Текущий режим отображения группы (issue #146).</param>
        /// <param name="showDisplayMode">Показывать ли выбор «Иконка» / «С надписью».
        /// Только для групп верхнего уровня; у вложенных всегда подпись.</param>
        public UtilitiesGroupEditWindow(
            IReadOnlyList<UtilitiesIconOption> icons,
            string? initialTitle = null,
            string? initialIconKey = null,
            string? initialIconColor = null,
            UtilitiesMenuGroupDisplay initialDisplayMode = UtilitiesMenuGroupDisplay.IconWithLabel,
            bool showDisplayMode = false)
        {
            InitializeComponent();
            Owner = Application.Current?.MainWindow;

            // issue #146: режим отображения доступен только группам верхнего уровня.
            // У вложенной группы выбор скрыт, результат принудительно «С надписью».
            DisplayModeLabel.Visibility = showDisplayMode ? Visibility.Visible : Visibility.Collapsed;
            DisplayCombo.Visibility = showDisplayMode ? Visibility.Visible : Visibility.Collapsed;
            DisplayCombo.ItemsSource = UtilitiesMenuEditorViewModel.GroupDisplayOptions;
            var mode = showDisplayMode
                ? initialDisplayMode
                : UtilitiesMenuGroupDisplay.IconWithLabel;
            DisplayCombo.SelectedItem = UtilitiesMenuEditorViewModel.GroupDisplayOptions
                .FirstOrDefault(o => o.Mode == mode)
                ?? UtilitiesMenuEditorViewModel.GroupDisplayOptions[0];

            // issue #146: эффективной иконки группы может не быть в каталоге (новая
            // верхнеуровневая папка) — добавляем её синтетическим вариантом, иначе
            // поле показывало бы первый пункт каталога, а не иконку из дерева.
            var options = new List<UtilitiesIconOption>(icons);
            if (!string.IsNullOrWhiteSpace(initialIconKey) &&
                !options.Any(o => string.Equals(o.Key, initialIconKey, StringComparison.Ordinal)))
            {
                options.Add(new UtilitiesIconOption(
                    initialIconKey!, string.IsNullOrWhiteSpace(initialIconColor) ? "#6B7280" : initialIconColor!));
            }
            IconCombo.ItemsSource = options;
            NameBox.Text = initialTitle ?? string.Empty;
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);

            // Выбираем именно текущую иконку (для правки) или первую из каталога.
            if (!string.IsNullOrWhiteSpace(initialIconKey))
            {
                var match = options.FirstOrDefault(o => string.Equals(o.Key, initialIconKey, StringComparison.Ordinal));
                if (match is not null)
                    IconCombo.SelectedItem = match;
            }
            if (IconCombo.SelectedItem is null && options.Count > 0)
                IconCombo.SelectedItem = options[0];

            Loaded += (_, _) =>
            {
                // Активное поле «Имя» с выделением текста (issue #374). Фокус откладываем:
                // в Loaded окно ещё не активировано, и Focus() не встаёт (текст выделяется,
                // но поле не активно для ввода). После активации окна фокус гарантирован.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Activate();
                    NameBox.Focus();
                    NameBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
                // Диалог чуть выше центра владельца (issue #374).
                Top = Math.Max(SystemParameters.WorkArea.Top, Top - 120);
            };
        }

        /// <summary>Результат диалога: имя и ключ иконки (null — отмена).</summary>
        public UtilitiesGroupEditParams? Result { get; private set; }

        private void OnNameBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
        }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            var icon = IconCombo.SelectedItem as UtilitiesIconOption;
            // issue #146: для вложенной группы поле скрыто — всегда «С надписью».
            var mode = DisplayCombo.Visibility == Visibility.Visible
                ? (DisplayCombo.SelectedItem as UtilitiesGroupDisplayOption)?.Mode
                    ?? UtilitiesMenuGroupDisplay.IconWithLabel
                : UtilitiesMenuGroupDisplay.IconWithLabel;
            Result = new UtilitiesGroupEditParams(
                NameBox.Text?.Trim() ?? string.Empty,
                icon is null ? null : icon.Key,
                mode);
            DialogResult = true;
        }
    }
}
#endif