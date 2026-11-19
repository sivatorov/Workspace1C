#if LINUX
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «Перенести в группу» окна «Управление службами» (issue #11, 1.0.0.14) —
    /// Avalonia/Linux-версия WPF-окна <see cref="ServiceGroupPickerWindow"/>: выбор
    /// места для службы — «Без группы» (корневой список) или одна из групп.
    /// <see cref="SelectedGroupName"/>: null — отмена; «» (пусто) — «Без группы».
    /// </summary>
    public sealed class ServiceGroupPickerWindow : ModalWindowBase
    {
        private readonly string? _initialGroup;
        private string? _selectedName;

        /// <summary>Выбранное место: имя группы или «» (пусто) — «Без группы»; null — отмена.</summary>
        public string? SelectedGroupName { get; private set; }

        /// <param name="serviceName">Имя переносимой службы (для заголовка вопроса).</param>
        /// <param name="groupNames">Имена групп для выбора (порядок — как в списке).</param>
        /// <param name="currentGroup">Группа, в которой служба сейчас; null — вне групп.</param>
        public ServiceGroupPickerWindow(string serviceName, IReadOnlyList<string> groupNames, string? currentGroup)
        {
            _initialGroup = currentGroup;
            _selectedName = string.IsNullOrEmpty(currentGroup) ? string.Empty : currentGroup;
            Title = LocalizationManager.T("Services.GroupPicker.Title");
            // Аудит UI P3a: минимальная ширина вместо фиксированной — длинные
            // английские подписи не обрезаются при 125/150 %.
            MinWidth = 400;
            MinHeight = 220;
            MaxHeight = 560;
            CanResize = false;
            FontSize = 13;
            Content = BuildRoot(serviceName, groupNames);
        }

        /// <summary>Показывает окно модально (синхронно).</summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private Control BuildRoot(string serviceName, IReadOnlyList<string> groupNames)
        {
            var question = new TextBlock
            {
                Text = string.Format(
                    LocalizationManager.T("Services.GroupPicker.QuestionFormat"),
                    string.IsNullOrWhiteSpace(serviceName) ? "—" : serviceName),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var noGroup = new RadioButton
            {
                Content = LocalizationManager.T("Services.GroupPicker.NoGroup"),
                IsChecked = string.IsNullOrEmpty(_initialGroup)
            };
            noGroup.Styled(ControlThemes.CacheCleanCheckBox);
            noGroup.IsCheckedChanged += (_, _) =>
            {
                if (noGroup.IsChecked == true)
                    _selectedName = string.Empty;
            };

            var host = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var name in groupNames)
            {
                var radio = new RadioButton
                {
                    Content = name,
                    IsChecked = string.Equals(name, _initialGroup, StringComparison.OrdinalIgnoreCase)
                };
                radio.Styled(ControlThemes.CacheCleanCheckBox);
                radio.IsCheckedChanged += (_, _) =>
                {
                    if (radio.IsChecked == true)
                        _selectedName = name;
                };
                host.Children.Add(radio);
            }
            var scroll = new ScrollViewer
            {
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = host
            };

            // Аудит UI P1c: высота нижней панели диалога — 36, ширина минимальная
            // (120), чтобы английские подписи не обрезались.
            var ok = new Button { Content = LocalizationManager.T("Common.Ok"), MinWidth = 120, Height = 36, IsDefault = true };
            ok.Styled(ControlThemes.DialogConfirmButton);
            ok.Click += (_, _) => OnOk();
            var cancel = new Button { Content = LocalizationManager.T("Common.Cancel"), MinWidth = 120, Height = 36, IsCancel = true };
            cancel.Styled(ControlThemes.DialogCancelButton);
            cancel.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 16, 0, 0),
                Children = { ok, cancel }
            };

            var root = new StackPanel { Margin = new Thickness(16), Children = { question, noGroup, scroll, buttons } };
            return root;
        }

        private void OnOk()
        {
            SelectedGroupName = _selectedName ?? string.Empty;
            DialogResult = true;
            Close();
        }
    }
}
#endif