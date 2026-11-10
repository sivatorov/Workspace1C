#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог выбора типа добавляемого элемента (информационная база или группа),
    /// аналогичный стартовому окну «1С:Предприятие». Avalonia/Linux-версия WPF-окна
    /// <see cref="AddEditWindow"/>.
    /// </summary>
    public class AddEditWindow : ModalWindowBase
    {
        /// <summary>Выбранный тип элемента: "Infobase", "CreateEmpty", "CreateFromTemplate" или "Group".</summary>
        public string SelectedType { get; private set; } = "Infobase";

        public AddEditWindow()
        {
            Title = LocalizationManager.T("AddEdit.Title");
            // Кегль окна из разметки: подписи без явного размера берут его по наследству.
            FontSize = 13;
            Width = 480;
            SizeToContent = SizeToContent.Height;
            CanResize = false;

            Content = BuildRoot();
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Заголовок со справкой рядом (AddEditWindow.xaml:63-68).
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("AddEdit.Question"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            });
            header.Children.Add(new Controls.HelpLink
            {
                HelpText = LocalizationManager.T("AddEdit.HelpText"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var options = new StackPanel();

            options.Children.Add(BuildOption("IconList", LocalizationManager.T("AddEdit.ExistingBase"),
                LocalizationManager.T("AddEdit.ExistingBaseDescription"), "Infobase", true));
            options.Children.Add(BuildOption("IconSave", LocalizationManager.T("AddEdit.CreateEmpty"),
                LocalizationManager.T("AddEdit.CreateEmptyDescription"), "CreateEmpty"));
            options.Children.Add(BuildOption("IconPackage", LocalizationManager.T("AddEdit.CreateFromTemplate"),
                LocalizationManager.T("AddEdit.CreateFromTemplateDescription"), "CreateFromTemplate"));
            options.Children.Add(BuildOption("IconFolder", LocalizationManager.T("AddEdit.Group"),
                LocalizationManager.T("AddEdit.GroupDescription"), "Group"));

            Grid.SetRow(options, 1);
            grid.Children.Add(options);

            // Порядок и оформление по разметке (AddEditWindow.xaml:151):
            // зелёное «Далее» слева, красная «Отмена» справа, зазор 10.
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(0, 12, 0, 0),
                Children =
                {
                    BuildConfirmActionButton("AddEdit.Next", "IconArrowRight", 130),
                    BuildCancelActionButton(130)
                }
            };

            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            return grid;
        }

        private RadioButton BuildOption(string iconKey, string title, string description, string tag, bool isChecked = false)
        {
            var radio = new RadioButton
            {
                Tag = tag,
                GroupName = "AddType",
                IsChecked = isChecked
            };
            // Карточка без маркера, с акцентной рамкой у выбранного варианта
            // (AddEditWindow.xaml:26). Отступы и поля задаёт сама тема.
            radio.Styled(Themes.ControlThemes.AddOptionCard);

            var content = new Grid { Margin = new Thickness(0) };
            content.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(36)));
            content.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            // Issue #107 п.1 (v1.0.1.49): значок и описание следуют Foreground карточки —
            // на акцентной заливке выбранного варианта акцентный значок был бы невидим.
            var iconBlock = IconHelper.MakeIcon(iconKey, 24, out var iconPath);
            iconPath.Bind(Avalonia.Controls.Shapes.Path.FillProperty,
                new Binding(nameof(RadioButton.Foreground)) { Source = radio });
            Grid.SetColumn(iconBlock, 0);
            content.Children.Add(iconBlock);

            var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textPanel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14 });
            var descriptionBlock = new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.85
            };
            descriptionBlock.Bind(TextBlock.ForegroundProperty,
                new Binding(nameof(RadioButton.Foreground)) { Source = radio });
            textPanel.Children.Add(descriptionBlock);
            Grid.SetColumn(textPanel, 1);
            content.Children.Add(textPanel);

            radio.Content = content;
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true && radio.Tag is string key)
                    SelectedType = key;
            };

            return radio;
        }
    }
}
#endif