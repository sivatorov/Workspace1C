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
    /// Диалог настроек приложения (Avalonia/Linux), часть «Клавиши»:
    /// валидация сочетаний, строки переназначения и слоты избранного.
    /// </summary>
    public partial class SettingsWindow
    {
        /// <summary>
        /// Проверяет назначения перед сохранением: понятное ли сочетание,
        /// не отбирает ли оно обычный ввод и не назначено ли двум действиям.
        /// При отказе окно остаётся открытым, чтобы было что исправлять.
        /// </summary>
        private bool ValidateHotkeys((string Action, Controls.HotkeyBox Box)[] assignments)
        {
            var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (action, box) in assignments)
            {
                var value = box.Value?.Trim() ?? string.Empty;
                if (value.Length == 0)
                    continue;

                if (!Controls.HotkeyBox.TryParse(value, out var gesture) || gesture is null)
                {
                    _viewModel.ShowWarning(string.Format(LocalizationManager.T("Settings.Hotkeys.Unsupported"), value));
                    return false;
                }

                if (Controls.HotkeyBox.IsUnsafeForTextInput(gesture))
                {
                    _viewModel.ShowWarning(string.Format(LocalizationManager.T("Settings.Hotkeys.Unsafe"), value));
                    return false;
                }

                if (used.TryGetValue(value, out var other))
                {
                    _viewModel.ShowWarning(string.Format(
                        LocalizationManager.T("Settings.Hotkeys.DuplicateMsg"),
                        string.Format(LocalizationManager.T("Settings.Hotkeys.AssignedTo"), value, other + ", " + action)));
                    return false;
                }

                used[value] = action;
            }

            // Комбинации цифр с модификаторами заняты закладками баз (issue #32):
            // они регистрируются системно и не видны в списке полей — предупреждаем,
            // иначе клавиша «назначится», но не сработает.
            var reserved = BookmarkSlotHelper.FindReservedHotkeyConflicts(
                assignments.Select(a => (a.Action, a.Box.Value?.Trim() ?? string.Empty)));
            if (reserved.Count > 0)
            {
                var msg = string.Join("\n", reserved.Select(r =>
                    string.Format(LocalizationManager.T("Settings.Hotkeys.AssignedTo"), r.Key, r.Name)));
                _viewModel.ShowWarning(string.Format(
                    LocalizationManager.T("Settings.Hotkeys.ReservedConflictMsg"), msg));
                return false;
            }

            return true;
        }

        /// <summary>Строка переназначения: подпись действия и поле ввода сочетания.</summary>
        private static Controls.HotkeyBox HotkeyRow(Panel host, string action, string value, string? tooltip = null)
        {
            // Расстановка строки как в WPF (issue #86, комментарий 7OH): фиксированная
            // ширина колонки подписей (260 — достаточно для самой длинной подписи
            // «Показать главное окно (глобально):»), поле HotkeyBox на остатке ширины —
            // поля всех строк начинаются в одной позиции, «гребёнка» исчезает.
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(260)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

            var label = new TextBlock { Text = action, VerticalAlignment = VerticalAlignment.Center };
            // Подсказка строки (аналог ToolTip в разметке WPF).
            if (!string.IsNullOrEmpty(tooltip))
                ToolTip.SetTip(label, tooltip);
            grid.Children.Add(label);

            // Поле сочетания в разметке идёт тем же стилем, что и обычное поле
            // ввода (SettingsWindow.xaml:977 и далее).
            var box = new Controls.HotkeyBox { Value = value ?? string.Empty, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 34 };
            box.Styled(ControlThemes.ModernTextBox);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);

            host.Children.Add(grid);
            return box;
        }

        private static Grid BuildHotkeyRow(string action, string key)
        {
            var grid = new Grid { Margin = new Thickness(0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(140)));

            var actionBlock = new TextBlock { Text = action, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(actionBlock, 0);
            grid.Children.Add(actionBlock);

            var keyBorder = new Border
            {
                Child = new TextBlock { Text = key, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Padding = new Thickness(10, 4),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(keyBorder, 1);
            grid.Children.Add(keyBorder);
            return grid;
        }

        /// <summary>Строка списка слотов избранного: ключ базы, её имя и номер слота.</summary>
        private sealed class FavoriteSlotItem : INotifyPropertyChanged
        {
            private int _number;

            public FavoriteSlotItem(string key, string name)
            {
                Key = key;
                Name = name;
            }

            public string Key { get; }

            public string Name { get; }

            public int Number
            {
                get => _number;
                set
                {
                    if (_number == value)
                        return;
                    _number = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Number)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Caption)));
                }
            }

            /// <summary>Подпись слота в списке: «Alt+1» и так далее.</summary>
            public string Caption => $"Alt+{_number}";

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
#endif