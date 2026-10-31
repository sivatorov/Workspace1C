#if LINUX
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог настроек приложения (Avalonia/Linux), часть «Платформы»:
    /// построение строки дерева найденных версий платформы 1С.
    /// </summary>
    public partial class SettingsWindow
    {
        /// <summary>
        /// Строка дерева платформ по шаблону разметки (SettingsWindow.xaml:330):
        /// подложка со скруглением 4, цветной значок 14 по типу узла, имя кеглем
        /// 12 и путь кеглем 11 под ним.
        /// </summary>
        private static Control BuildPlatformRow(object? item)
        {
            if (item is not PlatformVersionGroup node)
                return new TextBlock { Text = item?.ToString() ?? string.Empty };

            // Значок и цвет кодируют тип узла: линия это жёлтая папка, группа
            // сборок открытая синяя папка, сборка x64 контурный зелёный куб,
            // x32 сплошной фиолетовый, без метки синее окно.
            var (iconKey, iconColor) = node.Kind switch
            {
                PlatformNodeKind.Line => ("IconFolder", "@WarningBrush"),
                PlatformNodeKind.BuildGroup => ("IconFolderOpen", "@InfoBrush"),
                PlatformNodeKind.LeafX64 => ("IconCubeOutline", "@SuccessBrush"),
                PlatformNodeKind.LeafX32 => ("IconCube", "@PurpleAccentBrush"),
                _ => ("IconApplication", "@InfoBrush")
            };
            var icon = IconHelper.MakeIcon(iconKey, 14, Themes.ThemeBrushes.FromSpec(iconColor));
            icon.Margin = new Thickness(0, 2, 8, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;

            // DisplayName — единый форматтер (issue #33): для тонких клиентов имя
            // содержит локализованный суффикс «(тонкий клиент)» и подсвечивается
            // оранжевым, как в диалоге выбора версии.
            var name = new TextBlock
            {
                Text = node.DisplayName,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (node.IsThinClient)
                name.Foreground = Themes.ThemeBrushes.Get("WarningBrush", "#D97706");
            else
                ThemeBrushes.Bind(name, TextBlock.ForegroundProperty, "TextPrimaryBrush");

            // Строка пути есть у всех узлов, а не только у сборок: в разметке
            // шаблон один на все виды узлов, и у групп пустой TextBlock занимает
            // высоту строки. Замер снимка Windows: группа 38 пикселей, лист 39.
            var path = new TextBlock
            {
                Text = node.Path ?? string.Empty,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ThemeBrushes.Bind(path, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            if (!string.IsNullOrEmpty(node.Path))
                ToolTip.SetTip(path, node.Path);

            var texts = new StackPanel { Children = { name, path } };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(texts, 1);
            grid.Children.Add(icon);
            grid.Children.Add(texts);

            var row = new Border
            {
                Child = grid,
                Margin = new Thickness(0, 2),
                Padding = new Thickness(6, 4),
                CornerRadius = new CornerRadius(4)
            };
            ThemeBrushes.Bind(row, Border.BackgroundProperty, "ItemHoverBrush");
            if (!string.IsNullOrEmpty(node.Path))
                ToolTip.SetTip(row, node.Path);
            return row;
        }
    }
}
#endif