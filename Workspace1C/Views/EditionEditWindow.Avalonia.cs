#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Модальный диалог создания/редактирования редакции типовой конфигурации 1С
    /// (issue #321): имя редакции, сегменты «Ред»/«Подред» и переопределённая ссылка.
    /// Работает на копии данных; результат возвращается через <see cref="Result"/>.
    /// Заменяет инлайн-поля под таблицей редакций в <see cref="ConfigTypeEditWindow"/>.
    /// Avalonia/Linux-версия WPF-окна <see cref="EditionEditWindow"/>.
    /// </summary>
    public sealed class EditionEditWindow : ModalWindowBase
    {
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
        // Поля со стилем ModernTextBox (как в ConfigTypeEditWindow): без явного стиля поля
        // выглядят «серыми»/недоступными (issue #321).
        private readonly TextBox _nameBox = MakeTextBox();
        private readonly TextBox _redBox = MakeTextBox();
        private readonly TextBox _subRedBox = MakeTextBox();
        private readonly TextBox _urlOverrideBox = MakeTextBox();

        /// <summary>Готовая редакция при подтверждении, иначе <c>null</c>.</summary>
        public OneCConfigEdition? Result { get; private set; }

        /// <param name="model">Редактируемая редакция или <c>null</c> для новой.</param>
        public EditionEditWindow(OneCConfigEdition? model = null)
        {
            var isNew = model is null;

            Title = T(isNew ? "Updates.AddEdition" : "Updates.EditEdition");
            Width = 520;
            // Выше (issue #321): поле URL (UrlOverrideBox) было не видно при Height=360.
            Height = 520;
            MinWidth = 460;
            MinHeight = 470;
            FontSize = 13;
            CanResize = true;

            _nameBox.Text = model?.Name ?? string.Empty;
            _redBox.Text = model?.Red ?? string.Empty;
            _subRedBox.Text = model?.SubRed ?? string.Empty;
            _urlOverrideBox.Text = model?.UrlOverride ?? string.Empty;

            Content = BuildRoot();

            // Фокус в первом поле (наименование): отложенно после показа окна (паттерн #299).
            Opened += (_, _) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _nameBox.Focus();
                    if (isNew)
                        _nameBox.SelectAll();
                }, DispatcherPriority.Background);
            };
        }

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private Control BuildRoot()
        {
            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };

            var title = new TextBlock
            {
                Text = Title,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            panel.Children.Add(title);

            panel.Children.Add(MakeFieldRow(T("Updates.Name"), _nameBox));
            panel.Children.Add(MakeFieldRow(T("Updates.EditionRed"), _redBox));
            panel.Children.Add(MakeFieldRow(T("Updates.EditionSubRed"), _subRedBox));
            // Переопределённая ссылка (issue #369): полная ссылка либо только конечная
            // часть после releases.1c.ru/project/ (например «Enterprise13»).
            ToolTip.SetTip(_urlOverrideBox, T("Updates.UrlOverrideHint"));
            panel.Children.Add(MakeFieldRow(T("Updates.UrlOverride"), _urlOverrideBox));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var save = new Button { Content = T("Common.Save"), Width = 130, Height = 36, IsDefault = true };
            save.Styled(ControlThemes.DialogConfirmButton);
            save.Click += (_, _) => OnSaveClick();
            var cancel = BuildCancelActionButton(120);
            cancel.Click += (_, _) => OnCancelClick();
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            return panel;
        }

        private static Control MakeFieldRow(string label, Control editor)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(140, GridUnitType.Pixel)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.Margin = new Thickness(0, 4, 0, 4);

            var caption = new TextBlock
            {
                Text = label,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(caption, 0);
            grid.Children.Add(caption);

            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);
            return grid;
        }

        private void OnSaveClick()
        {
            var name = _nameBox.Text?.Trim() ?? string.Empty;
            var red = _redBox.Text?.Trim() ?? string.Empty;

            // Редакция должна иметь имя или хотя бы сегмент «Ред» — иначе строка бессмысленна
            // (пустая строка «терялась» при показе списка — issue #321).
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(red))
            {
                _dialogs.ShowWarning(T("Updates.EditionNameOrRedRequired"), T("Updates.EditEdition"));
                _nameBox.Focus();
                return;
            }

            Result = new OneCConfigEdition
            {
                Name = name,
                Red = red,
                SubRed = _subRedBox.Text?.Trim() ?? string.Empty,
                UrlOverride = _urlOverrideBox.Text?.Trim() ?? string.Empty,
            };
            DialogResult = true;
            Close();
        }

        private void OnCancelClick()
        {
            DialogResult = false;
            Close();
        }

        /// <summary>Поле ввода формы (стиль ModernTextBox — как в окне конфигурации, issue #321).</summary>
        private static TextBox MakeTextBox()
        {
            var tb = new TextBox
            {
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            tb.Styled(ControlThemes.ModernTextBox);
            return tb;
        }
    }
}
#endif