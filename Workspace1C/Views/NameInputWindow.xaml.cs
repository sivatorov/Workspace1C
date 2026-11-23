#if WINDOWS
using System;
using System.Windows;
using System.Windows.Input;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Диалог ввода произвольного названия (например, названия темы оформления)
    /// в стиле Material Design: с подписью, полем ввода и кнопками «Отмена»/«ОК».
    /// </summary>
    public partial class NameInputWindow : Window
    {
        /// <summary>
        /// Создаёт диалог ввода названия.
        /// </summary>
        /// <param name="title">Заголовок окна.</param>
        /// <param name="label">Подпись над полем ввода.</param>
        /// <param name="okText">Текст на кнопке подтверждения.</param>
        /// <param name="initialText">Начальное значение поля ввода.</param>
        public NameInputWindow(string title, string label, string okText, string initialText = "")
        {
            InitializeComponent();
            Title = title;
            PromptLabel.Text = label;
            OkTextBlock.Text = okText;
            NameBox.Text = initialText;
            ApplyWorkAreaLimits();
            Loaded += (_, _) =>
            {
                // Фокус ставим отложенно, после полного показа окна: синхронная
                // установка в Loaded слетает до отрисовки модального диалога.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    NameBox.Focus();
                    NameBox.SelectAll();
                }));
            };
        }

        /// <summary>
        /// Ширина диалога фиксирована (440 DIP), чтобы длинная подпись не растягивала
        /// окно на весь экран; на маленьких мониторах ширина и высота дополнительно
        /// клампятся по рабочей области. Подпись переносится по словам.
        /// </summary>
        private void ApplyWorkAreaLimits()
        {
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(Width, DialogWorkAreaPolicy.MaxHeight(workArea.Width));
            MaxHeight = DialogWorkAreaPolicy.MaxHeight(workArea.Height);
            MinHeight = DialogWorkAreaPolicy.ClampMinHeight(MinHeight, workArea.Height);
        }

        /// <summary>
        /// Введённое название (null, если пользователь отменил ввод).
        /// </summary>
        public string? Result { get; private set; }

        private void OnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = NameBox.Text?.Trim();
            DialogResult = true;
        }

        private void OnNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnOk_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
#endif