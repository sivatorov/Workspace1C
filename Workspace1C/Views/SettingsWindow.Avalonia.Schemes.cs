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
    /// Диалог настроек приложения (Avalonia/Linux), часть «Цветовое оформление»:
    /// редактирование цветовых схем и живой предпросмотр текущей палитры.
    /// </summary>
    public partial class SettingsWindow
    {
        /// <summary>
        /// Строка цвета схемы: подпись и образец. Щелчок открывает выбор цвета
        /// и сразу применяет результат, чтобы правку было видно на приложении.
        /// </summary>
        private Control ColorRow(ColorScheme scheme, bool dark, string key, string label, string value)
        {
            // Числа из разметки (SettingsWindow.xaml:912): образец 28 на 20
            // в колонке шириной 36.
            // Граница образца — DividerBrush темы (аудит UI) вместо фиксированного серого.
            var swatch = new Border
            {
                Width = 28,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = Themes.ThemeBrushes.Get("DividerBrush", "#4B5563"),
                Background = ParseBrush(value)
            };

            // Подпись значения объявляется ниже, а обновлять её надо отсюда,
            // поэтому обновление передаётся отложенно.
            Action<string>? hexText = null;

            void PickColor()
            {
                var picker = new ColorPickerWindow(value);
                if (!picker.ShowDialogSync(this))
                    return;

                value = picker.Result;
                scheme.Palette(dark)[key] = value;
                swatch.Background = ParseBrush(value);
                hexText?.Invoke(value);
                RepaintThemePreview(scheme, dark);
            }

            // Порядок колонок по варианту 2 (#155): образец, затем hex, и уже потом
            // подчёркнутая кликабельная подпись. Значение показывается потому, что
            // цвет часто переносят копированием, а не глазом.
            var hex = new TextBlock
            {
                Text = value,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            // Цвет HEX-текста контрастен ФАКТИЧЕСКОМУ фону строки редактора (issue #26):
            // строка лежит на фоне окна настроек (карточка активной темы приложения),
            // а не на фоне редактируемой палитры. Иначе при редактировании светлой
            // палитры в тёмной теме текст HEX становился тёмным и сливался с тёмной
            // формой (и наоборот).
            var appDark = Themes.ThemeManager.CurrentTheme == Themes.ThemeManager.DarkThemeName;
            var rowBackground = Themes.ThemeManager.CurrentScheme is { } appScheme
                ? appScheme.PaletteValue(appDark, "CardBackgroundColor")
                : (appDark ? "#1E293B" : "#FFFFFF"); // fallback при отсутствии схемы
            hex.Foreground = new SolidColorBrush(Color.Parse(
                Models.ColorScheme.ContrastTextHex(rowBackground)));

            hexText = updated => hex.Text = updated;

            // Название цвета — кликабельная подчёркнутая ссылка, открывает выбор цвета.
            // Это убирает отдельную кнопку «Выбрать» и заметно сужает список.
            var link = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                TextDecorations = TextDecorations.Underline,
                Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            // Подпись просто контрастная, а не акцентная (issue #26): акцентный цвет
            // на карточном фоне тёмной темы не читается.
            ThemeBrushes.Bind(link, TextBlock.ForegroundProperty, "TextPrimaryColorBrush");
            link.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left)
                    return;
                // Отпускание вне текста щелчком не считается (как в LinkBlock).
                var point = e.GetPosition(link);
                if (point.X < 0 || point.Y < 0
                    || point.X > link.Bounds.Width || point.Y > link.Bounds.Height)
                    return;
                PickColor();
            };
            ToolTip.SetTip(link, LocalizationManager.T("Settings.ChooseColorTooltip"));

            // Ширины колонок: образец 36, hex по содержимому, тянется ссылка-подпись.
            var grid = new Grid { Margin = new Thickness(0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(36)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.Children.Add(swatch);
            grid.Children.Add(hex);
            Grid.SetColumn(hex, 1);
            grid.Children.Add(link);
            Grid.SetColumn(link, 2);
            return grid;
        }

        /// <summary>
        /// Миниатюрный предпросмотр темы (аналог WPF PreviewShell в
        /// SettingsWindow.xaml). Хранит ссылки на все части, чтобы перекрашивать
        /// их при изменении цветов схемы.
        /// </summary>
        private sealed class ThemePreview
        {
            public Border Shell = null!;
            public Border TitleBar = null!;
            public TextBlock TitleText = null!;
            public Border Sidebar = null!;
            public Border NavSelected = null!;
            public TextBlock NavSelectedText = null!;
            public Border NavItem1 = null!;
            public TextBlock NavItem1Text = null!;
            public Border NavItem2 = null!;
            public TextBlock NavItem2Text = null!;
            public Border Main = null!;
            public TextBlock ContentTitle = null!;
            public TextBlock ContentSubtitle = null!;
            public Border Card = null!;
            public TextBlock CardTitle = null!;
            public TextBlock CardText = null!;
            public TextBox TextField = null!;
            public Border PrimaryButton = null!;
            public TextBlock PrimaryButtonText = null!;
            public Border SecondaryButton = null!;
            public TextBlock SecondaryButtonText = null!;
            public Border ListBox = null!;
            public Border ListSelected = null!;
            public TextBlock ListSelectedText = null!;
            public Border ListItem1 = null!;
            public TextBlock ListItem1Text = null!;
            public Border ListItem2 = null!;
            public TextBlock ListItem2Text = null!;
        }

        /// <summary>Единый живой предпросмотр текущей редактируемой палитры.</summary>
        private ThemePreview _preview = null!;

        /// <summary>
        /// Строит миниатюрное окно приложения для предпросмотра схемы. Разметка
        /// повторяет WPF PreviewShell (SettingsWindow.xaml): акцентная шапка,
        /// боковое меню, карточка, поле ввода, кнопки, список.
        /// </summary>
        private static ThemePreview BuildThemePreview()
        {
            var p = new ThemePreview();

            // Шапка (акцент): заголовок и зелёный индикатор.
            p.TitleText = new TextBlock
            {
                Text = "Workspace 1C",
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            var statusDot = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = Themes.ThemeBrushes.Get("SuccessBrush", "#16A34A"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            Grid.SetColumn(statusDot, 1);
            titleGrid.Children.Add(p.TitleText);
            titleGrid.Children.Add(statusDot);
            p.TitleBar = new Border { Height = 34, Padding = new Thickness(10, 0), Child = titleGrid };

            // Боковое меню.
            p.NavSelectedText = new TextBlock { Text = "Базы", FontSize = 11 };
            p.NavItem1Text = new TextBlock { Text = "Избранное", FontSize = 11 };
            p.NavItem2Text = new TextBlock { Text = "История", FontSize = 11 };
            p.NavSelected = NewNavItem(p.NavSelectedText);
            p.NavItem1 = NewNavItem(p.NavItem1Text);
            p.NavItem2 = NewNavItem(p.NavItem2Text);
            p.Sidebar = new Border
            {
                Padding = new Thickness(6),
                Child = new StackPanel { Children = { p.NavSelected, p.NavItem1, p.NavItem2 } }
            };

            // Контент.
            p.ContentTitle = new TextBlock { Text = "Документы", FontWeight = FontWeight.SemiBold, FontSize = 13 };
            p.ContentSubtitle = new TextBlock { Text = "Последние изменения", FontSize = 11, Margin = new Thickness(0, 2, 0, 8) };
            p.CardTitle = new TextBlock { Text = "Карточка базы", FontWeight = FontWeight.SemiBold, FontSize = 11 };
            p.CardText = new TextBlock { Text = "Краткое описание объекта", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) };
            p.Card = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = new StackPanel { Children = { p.CardTitle, p.CardText } }
            };
            p.TextField = new TextBox { Height = 26, FontSize = 11, Padding = new Thickness(6, 2), BorderThickness = new Thickness(1) };
            p.PrimaryButtonText = new TextBlock { Text = "Готово", FontSize = 11, FontWeight = FontWeight.SemiBold };
            p.SecondaryButtonText = new TextBlock { Text = "Отмена", FontSize = 11 };
            p.PrimaryButton = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 5), Margin = new Thickness(0, 0, 6, 0), Child = p.PrimaryButtonText };
            p.SecondaryButton = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 5), Child = p.SecondaryButtonText };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0),
                Children = { p.PrimaryButton, p.SecondaryButton }
            };
            p.ListSelectedText = new TextBlock { Text = "Бухгалтерия предприятия", FontSize = 11 };
            p.ListItem1Text = new TextBlock { Text = "Зарплата и кадры", FontSize = 11 };
            p.ListItem2Text = new TextBlock { Text = "Управление торговлей", FontSize = 11 };
            p.ListSelected = new Border { Padding = new Thickness(8, 5), Child = p.ListSelectedText };
            p.ListItem1 = new Border { Padding = new Thickness(8, 5), BorderThickness = new Thickness(0, 1, 0, 0), Child = p.ListItem1Text };
            p.ListItem2 = new Border { Padding = new Thickness(8, 5), BorderThickness = new Thickness(0, 1, 0, 0), Child = p.ListItem2Text };
            p.ListBox = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 10, 0, 0),
                Child = new StackPanel { Children = { p.ListSelected, p.ListItem1, p.ListItem2 } }
            };
            p.Main = new Border
            {
                Padding = new Thickness(10),
                Child = new StackPanel { Children = { p.ContentTitle, p.ContentSubtitle, p.Card, p.TextField, buttons, p.ListBox } }
            };

            // Каркас: боковое меню слева, контент справа.
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(64)));
            content.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            Grid.SetColumn(p.Main, 1);
            content.Children.Add(p.Sidebar);
            content.Children.Add(p.Main);

            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            body.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            Grid.SetRow(content, 1);
            body.Children.Add(p.TitleBar);
            body.Children.Add(content);

            p.Shell = new Border
            {
                Width = 210,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = body
            };
            return p;
        }

        /// <summary>Перекрашивает единый предпросмотр текущей редактируемой палитрой схемы.</summary>
        private void RepaintThemePreview(ColorScheme scheme, bool dark)
        {
            if (_preview is null)
                return;
            PaintThemePreview(_preview, scheme, dark);
        }

        /// <summary>Рисует один миниатюрный предпросмотр темы для заданной палитры.</summary>
        private static void PaintThemePreview(ThemePreview p, ColorScheme scheme, bool dark)
        {
            string V(string key) => scheme.PaletteValue(dark, key);

            // Окно: подложка-карточка с рамкой и акцентная шапка. Заголовок на шапке —
            // контрастный к акценту цвет (TextOnAccentBrush пересчитывается по WCAG
            // в ThemeManager, issue #26), иначе предпросмотр расходится с реальностью.
            PaintBorder(p.Shell, V("CardBackgroundColor"), V("BorderColor"));
            PaintSolid(p.TitleBar, V("AccentColor"));
            PaintTextBrush(p.TitleText, new SolidColorBrush(ContrastColor(Color.Parse(V("AccentColor")))));

            // Боковая панель: фон, контрастный текст, подсветка пунктов.
            var sidebar = Color.Parse(V("SidebarColor"));
            var sidebarText = new SolidColorBrush(ContrastColor(sidebar));
            PaintSolid(p.Sidebar, V("SidebarColor"));
            PaintSolid(p.NavSelected, V("SidebarSelectedColor"));
            PaintSolid(p.NavItem1, V("SidebarHoverColor"));
            PaintSolid(p.NavItem2, V("SidebarHoverColor"));
            PaintTextBrush(p.NavSelectedText, sidebarText);
            PaintTextBrush(p.NavItem1Text, sidebarText);
            PaintTextBrush(p.NavItem2Text, sidebarText);

            // Контент.
            PaintSolid(p.Main, V("ContentBackgroundColor"));
            PaintText(p.ContentTitle, V("TextPrimaryColor"));
            PaintText(p.ContentSubtitle, V("TextSecondaryColor"));

            // Карточка.
            PaintBorder(p.Card, V("CardBackgroundColor"), V("BorderColor"));
            PaintText(p.CardTitle, V("TextPrimaryColor"));
            PaintText(p.CardText, V("TextSecondaryColor"));

            // Поле ввода.
            PaintTextBox(p.TextField, V("CardBackgroundColor"), V("BorderColor"), V("TextPrimaryColor"));

            // Кнопки: акцентная и вторичная.
            PaintSolid(p.PrimaryButton, V("AccentColor"));
            PaintText(p.PrimaryButtonText, V("ButtonTextColor"));
            PaintSolid(p.SecondaryButton, V("SecondaryButtonBackgroundColor"));
            PaintText(p.SecondaryButtonText, V("ButtonTextColor"));

            // Список.
            PaintBorder(p.ListBox, V("CardBackgroundColor"), V("BorderColor"));
            PaintSolid(p.ListSelected, V("ItemSelectedColor"));
            PaintSolid(p.ListItem1, V("ItemHoverColor"));
            PaintSolid(p.ListItem2, V("ItemHoverColor"));
            PaintText(p.ListSelectedText, V("TextPrimaryColor"));
            PaintText(p.ListItem1Text, V("TextPrimaryColor"));
            PaintText(p.ListItem2Text, V("TextPrimaryColor"));
        }
    }
}
#endif