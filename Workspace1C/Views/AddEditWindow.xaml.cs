#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Workspace1C
{
    /// <summary>
    /// Диалог выбора типа добавляемого элемента (информационная база или группа),
    /// аналогичный стартовому окну «1С:Предприятие».
    /// </summary>
    public partial class AddEditWindow : Window
    {
        /// <summary>Выбранный тип элемента: "Infobase" или "Group".</summary>
        public string SelectedType { get; private set; } = "Infobase";

        public AddEditWindow()
        {
            InitializeComponent();

            // ESC сначала закрывает открытые всплывающие подсказки, а только потом окно (issue #270).
            // Раньше первый ESC в окне свойств базы закрывал всё окно целиком (кнопка «Отмена»
            // IsCancel), хотя подсказка могла быть открыта. Preview перехватывает клавишу до того,
            // как её обработает IsCancel-кнопка; если тултипы были закрыты — окно на этом ESC не
            // закроется, повторный ESC уже закрывает окно как обычно (инвариант «сначала подсказка,
            // потом окно» — тот же, что у главного окна и окна настроек, issue #261/#270).
            ToolTipCloser.Register();
            PreviewKeyDown += OnToolTipEscPreviewKeyDown;

            // Подсказки скрываются при потере фокуса окна (issue #275), как контекстное меню:
            // при клике в другое окно/приложение открытый тултип исчезает, а не «висит» поверх.
            Deactivated += (_, _) => ToolTipCloser.CloseAll();
        }

        /// <summary>
        /// Preview-обработчик ESC (issue #270): первый ESC закрывает открытые всплывающие
        /// подсказки и помечает событие обработанным — кнопка «Отмена» (IsCancel) окно на этом
        /// ESC не закроет; повторный ESC закрывает окно как обычно.
        /// </summary>
        private void OnToolTipEscPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape &&
                Keyboard.Modifiers == ModifierKeys.None &&
                ToolTipCloser.CloseAll(suppressReopen: true))
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Обновляет выбранный тип при переключении радиокнопок.
        /// </summary>
        private void OnOption_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radioButton)
            {
                SelectedType = radioButton.Tag as string ?? "Infobase";
            }
        }

        /// <summary>
        /// Закрывает диалог с положительным результатом.
        /// </summary>
        private void OnNext_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
#endif