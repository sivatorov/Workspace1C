#if WINDOWS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.IO;
using MaterialDesignThemes.Wpf;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

namespace Workspace1C
{
    public partial class SettingsWindow
    {
        /// <summary>
        /// Строит вкладку «Резервное копирование» профиля: каталог, флажок восстановления
        /// при запуске и кнопки «Сохранить профиль» / «Восстановить профиль».
        /// </summary>
        private void InitializeProfileBackupTab()
        {
            var tab = new TabItem();
            try { tab.Style = (Style)FindResource("SettingsTabItem"); } catch { /* стандартный вид */ }
            var tabIcon = new MaterialDesignThemes.Wpf.PackIcon
            {
                Kind = MaterialDesignThemes.Wpf.PackIconKind.BackupRestore,
                Width = 18,
                Height = 18,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            tabIcon.SetBinding(MaterialDesignThemes.Wpf.PackIcon.ForegroundProperty,
                new System.Windows.Data.Binding("Foreground")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.FindAncestor,
                        typeof(System.Windows.Controls.TabItem), 1)
                });
            var tabHeader = new StackPanel { Orientation = Orientation.Horizontal };
            tabHeader.Children.Add(tabIcon);
            var title = new TextBlock
            {
                Text = LocalizationManager.T("Settings.TabProfile"),
                VerticalAlignment = VerticalAlignment.Center
            };
            // Цвет текста заголовка привязан к Foreground вкладки так же, как в
            // статичной разметке вкладок (SettingsWindow.xaml:296): при выборе
            // пункта тема красит подпись контрастной кистью TreeSelectedTextBrush
            // (issue #26), а не наследуемым значением — WPF не передаёт его от
            // шаблона в TextBlock через attached TextElement.Foreground.
            title.SetBinding(TextBlock.ForegroundProperty,
                new System.Windows.Data.Binding("Foreground")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.FindAncestor,
                        typeof(System.Windows.Controls.TabItem), 1)
                });
            tabHeader.Children.Add(title);
            tab.Header = tabHeader;

            var panel = new StackPanel { Margin = new Thickness(4, 12, 4, 0) };

            panel.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Profile.Description"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = SecondaryBrush(),
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Profile.Includes"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = SecondaryBrush(),
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Каталог резервной копии.
            var dirGroup = new GroupBox
            {
                Header = LocalizationManager.T("Settings.Profile.Directory"),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var dirDock = new DockPanel();
            var browse = new Button
            {
                Content = LocalizationManager.T("Settings.Profile.Browse"),
                Style = TryFindResource("SecondaryButton") as Style,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(8, 0, 0, 0)
            };
            DockPanel.SetDock(browse, Dock.Right);
            browse.Click += (_, _) => OnBrowseProfileDir_Click();
            _profileDirBox = new TextBox
            {
                Text = _viewModel.ProfileBackupDirectory,
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            dirDock.Children.Add(browse);
            dirDock.Children.Add(_profileDirBox);
            dirGroup.Content = dirDock;
            panel.Children.Add(dirGroup);

            _profileRestoreCheck = new CheckBox
            {
                Content = LocalizationManager.T("Settings.Profile.RestoreOnStartup"),
                IsChecked = _viewModel.ProfileRestoreOnStartup,
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(_profileRestoreCheck);
            panel.Children.Add(new TextBlock
            {
                Text = LocalizationManager.T("Settings.Profile.RestoreOnStartupHint"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = SecondaryBrush(),
                Margin = new Thickness(24, 0, 0, 12)
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            // Кнопки резервного копирования профиля используют вторичный стиль темы
            // (issue #46): раньше рисовались дефолтным стилем и не переключались с темой.
            var backup = new Button
            {
                Content = LocalizationManager.T("Settings.Profile.BackupNow"),
                Style = TryFindResource("SecondaryButton") as Style,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            backup.Click += (_, _) => OnProfileBackup_Click();
            var restore = new Button
            {
                Content = LocalizationManager.T("Settings.Profile.RestoreNow"),
                Style = TryFindResource("SecondaryButton") as Style,
                Padding = new Thickness(12, 6, 12, 6)
            };
            restore.Click += (_, _) => OnProfileRestore_Click();
            buttons.Children.Add(backup);
            buttons.Children.Add(restore);
            panel.Children.Add(buttons);

            tab.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            // Вставляем перед последней вкладкой («О программе»), чтобы она шла выше.
            SettingsTabs.Items.Insert(Math.Max(0, SettingsTabs.Items.Count - 1), tab);
        }

        /// <summary>
        /// Кисть вторичного текста темы (v100114: запасной цвет — тематизированный
        /// нейтральный #64748B, а не фиксированный серый — текст читается в обеих темах).
        /// </summary>
        private Brush SecondaryBrush() =>
            Themes.WpfBrushes.Get("TextSecondaryBrush") ?? Themes.WpfBrushes.FromSpec("#64748B");

        /// <summary>Выбор каталога резервной копии профиля.</summary>
        private void OnBrowseProfileDir_Click()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = LocalizationManager.T("Settings.Profile.Directory"),
                SelectedPath = _profileDirBox?.Text ?? string.Empty
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPath)
                && _profileDirBox is not null)
                _profileDirBox.Text = dlg.SelectedPath;
        }

        /// <summary>«Сохранить профиль»: копирует настройки в выбранный каталог.</summary>
        private void OnProfileBackup_Click()
        {
            _viewModel.ApplyProfileBackupSettings(_profileDirBox.Text, _profileRestoreCheck.IsChecked == true);
            _viewModel.BackupProfile();
        }

        /// <summary>«Восстановить профиль»: копирует настройки из каталога и закрывает окно.</summary>
        private void OnProfileRestore_Click()
        {
            _viewModel.ApplyProfileBackupSettings(_profileDirBox.Text, _profileRestoreCheck.IsChecked == true);
            if (_viewModel.RestoreProfile())
            {
                DialogResult = true;
                Close();
            }
        }
    }
}
#endif