#if LINUX
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Диалог ввода произвольного названия (например, названия темы оформления).
    /// Avalonia/Linux-версия WPF-окна <see cref="NameInputWindow"/>.
    /// Раскладка лежит в NameInputWindow.axaml, здесь только поведение.
    /// </summary>
    public partial class NameInputWindow : ModalWindowBase
    {
        private readonly TextBox _nameBox;

        /// <summary>
        /// Создаёт диалог ввода названия.
        /// </summary>
        /// <param name="title">Заголовок окна.</param>
        /// <param name="label">Подпись над полем ввода.</param>
        /// <param name="okText">Текст на кнопке подтверждения.</param>
        /// <param name="initialText">Начальное значение поля ввода.</param>
        public NameInputWindow(string title, string label, string okText, string initialText = "")
        {
            AvaloniaXamlLoader.Load(this);

            Title = title;
            this.FindControl<TextBlock>("Prompt")!.Text = label;

            _nameBox = this.FindControl<TextBox>("NameBox")!;
            _nameBox.Text = initialText;
            _nameBox.KeyDown += OnNameBox_KeyDown;

            this.FindControl<ContentControl>("ButtonsHost")!.Content =
                BuildButtons(okText, 130, OnOk_Click);

            Opened += (_, _) =>
            {
                // Ширина фиксирована (440 DIP), чтобы длинная подпись не растягивала
                // окно на весь экран; на маленьких мониторах клампим по рабочей области.
                ApplyWorkAreaLimits();
                // Фокус ставим отложенно (после полного показа и активации окна):
                // синхронная установка в Opened слетает до отрисовки модального диалога.
                Dispatcher.UIThread.Post(() =>
                {
                    _nameBox.Focus();
                    _nameBox.SelectAll();
                });
            };
        }

        /// <summary>
        /// Клампит ширину/высоту диалога по рабочей области монитора (общая политика
        /// диалогов). Подпись переносится по словам, поэтому длинный текст не
        /// растягивает окно по горизонтали.
        /// </summary>
        private void ApplyWorkAreaLimits()
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null)
                return;
            var dipWidth = screen.WorkingArea.Width / screen.Scaling;
            var dipHeight = screen.WorkingArea.Height / screen.Scaling;
            MinWidth = DialogWorkAreaPolicy.ClampMinHeight(440, dipWidth);
            MinHeight = DialogWorkAreaPolicy.ClampMinHeight(200, dipHeight);
            Width = Math.Min(Width, MinWidth);
            MaxHeight = DialogWorkAreaPolicy.MaxHeight(dipHeight);
        }

        /// <summary>
        /// Введённое название (null, если пользователь отменил ввод).
        /// </summary>
        public string? Result { get; private set; }

        private void OnOk_Click()
        {
            Result = _nameBox.Text?.Trim();
        }

        private void OnNameBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OnOk_Click();
                DialogResult = true;
                Close();
                e.Handled = true;
            }
        }
    }
}
#endif
