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
        /// Инициализирует выпадающий список «Разрядность по умолчанию» во вкладке «Платформы».
        /// </summary>
        private void InitializeDefaultArchitecture()
        {
            DefaultArchComboBox.Items.Clear();
            DefaultArchComboBox.Items.Add(LocalizationManager.T("Settings.Arch64Recommended"));
            DefaultArchComboBox.Items.Add(LocalizationManager.T("Settings.Arch32"));
            DefaultArchComboBox.Items.Add(LocalizationManager.T("Settings.ArchBasePriority"));
            DefaultArchComboBox.SelectedIndex =
                string.Equals(_viewModel.DefaultArchitecture, "X86", StringComparison.OrdinalIgnoreCase) ? 1
                : string.Equals(_viewModel.DefaultArchitecture, "Priority", StringComparison.OrdinalIgnoreCase) ? 2
                : 0;
        }

        /// <summary>Локализованное название колонки по её ключу.</summary>
        private static string ColumnOrderLabel(string key) => LocalizationManager.T(key switch
        {
            "Version" => "Column.Version",
            "Configuration" => "Column.Configuration",
            "ConfigurationVersion" => "Column.ConfigurationVersion",
            "LaunchMode" => "Column.LaunchMode",
            "ServerBase" => "Column.ServerBase",
            "LastLaunch" => "Column.LastLaunch",
            "Size" => "Column.Size",
            "Modified" => "Column.Modified",
            "LastBackup" => "Column.LastBackup",
            "Actions" => "Column.Actions",
            _ => "Column.Name"
        });

        /// <summary>Иконка колонки по её ключу — та же, что в заголовке списка баз.</summary>
        private static MaterialDesignThemes.Wpf.PackIconKind ColumnOrderIcon(string key) => key switch
        {
            "Version" => MaterialDesignThemes.Wpf.PackIconKind.Information,
            "Configuration" => MaterialDesignThemes.Wpf.PackIconKind.CubeOutline,
            "ConfigurationVersion" => MaterialDesignThemes.Wpf.PackIconKind.Counter,
            "LaunchMode" => MaterialDesignThemes.Wpf.PackIconKind.Play,
            "ServerBase" => MaterialDesignThemes.Wpf.PackIconKind.Server,
            "LastLaunch" => MaterialDesignThemes.Wpf.PackIconKind.ClockOutline,
            "Size" => MaterialDesignThemes.Wpf.PackIconKind.Database,
            "Modified" => MaterialDesignThemes.Wpf.PackIconKind.CalendarClock,
            "LastBackup" => MaterialDesignThemes.Wpf.PackIconKind.BackupRestore,
            "Actions" => MaterialDesignThemes.Wpf.PackIconKind.Cog,
            _ => MaterialDesignThemes.Wpf.PackIconKind.FormatTitle
        };

        /// <summary>Видимость колонки по её ключу из текущих настроек.</summary>
        private bool ColumnVisible(string key) => key switch
        {
            "Version" => _viewModel.ShowVersionColumn,
            "Configuration" => _viewModel.ShowConfigurationColumn,
            "ConfigurationVersion" => _viewModel.ShowConfigurationVersionColumn,
            "LaunchMode" => _viewModel.ShowLaunchModeColumn,
            "ServerBase" => _viewModel.ShowServerColumn,
            "LastLaunch" => _viewModel.ShowLastLaunchColumn,
            "Size" => _viewModel.ShowSizeColumn,
            "Modified" => _viewModel.ShowModifiedColumn,
            "LastBackup" => _viewModel.ShowLastBackupColumn,
            "Actions" => _viewModel.ShowActionsColumn,
            _ => true
        };

        /// <summary>Заполняет список порядка колонок текущим порядком из настроек.</summary>
        private void InitializeColumnOrder()
        {
            _columnOrderItems.Clear();
            foreach (var key in _viewModel.ColumnOrderKeys)
                _columnOrderItems.Add(new ColumnOrderItem
                {
                    Key = key,
                    Display = ColumnOrderLabel(key),
                    Visible = ColumnVisible(key),
                    IconKind = ColumnOrderIcon(key)
                });
            if (ColumnOrderList != null)
                ColumnOrderList.ItemsSource = _columnOrderItems;
            UpdateColumnOrderButtons();
        }

        /// <summary>Обновляет доступность кнопок «Вверх»/«Вниз» по выбранной строке.</summary>
        private void UpdateColumnOrderButtons()
        {
            if (ColumnOrderList == null)
                return;
            var idx = ColumnOrderList.SelectedIndex;
            if (ColumnOrderUpButton != null)
                ColumnOrderUpButton.IsEnabled = idx > 0;
            if (ColumnOrderDownButton != null)
                ColumnOrderDownButton.IsEnabled = idx >= 0 && idx < _columnOrderItems.Count - 1;
        }

        private void OnColumnOrderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateColumnOrderButtons();

        private void OnColumnOrderUp_Click(object sender, RoutedEventArgs e)
        {
            var idx = ColumnOrderList?.SelectedIndex ?? -1;
            if (idx <= 0 || ColumnOrderList == null)
                return;
            var item = _columnOrderItems[idx];
            _columnOrderItems.Move(idx, idx - 1);
            ColumnOrderList.SelectedIndex = idx - 1;
            UpdateColumnOrderButtons();
        }

        private void OnColumnOrderDown_Click(object sender, RoutedEventArgs e)
        {
            var idx = ColumnOrderList?.SelectedIndex ?? -1;
            if (idx < 0 || idx >= _columnOrderItems.Count - 1 || ColumnOrderList == null)
                return;
            var item = _columnOrderItems[idx];
            _columnOrderItems.Move(idx, idx + 1);
            ColumnOrderList.SelectedIndex = idx + 1;
            UpdateColumnOrderButtons();
        }

        /// <summary>
        /// Переключает окно настроек сразу на подвкладку «Колонки» (issue #173).
        /// Используется из контекстного меню заголовка колонки списка баз.
        /// «Отображение» — вторая вкладка главного контрола; «Колонки» — вторая
        /// подвкладка раздела (после «Значки и кнопки»).
        /// </summary>
        public void SelectColumnsTab()
        {
            if (SettingsTabs != null)
                SettingsTabs.SelectedIndex = 1;
            if (DisplaySubTabs != null)
                DisplaySubTabs.SelectedIndex = 1;
        }

        /// <summary>
        /// Переключает окно настроек сразу на вкладку «Меню „Утилиты“» (issue #374).
        /// Используется из контекстного меню кнопки «Утилиты» главного окна.
        /// Вкладка «Меню „Утилиты“» — шестая в главном контроле (индекс 5).
        /// </summary>
        public void SelectUtilitiesMenuTab()
        {
            if (SettingsTabs != null)
                SettingsTabs.SelectedIndex = 5;
        }

        /// <summary>
        /// Инициализирует вкладку «Отображение»: заполняет флажки текущими
        /// настройками отображения списка баз.
        /// </summary>
        private void InitializeDisplaySettings()
        {
            _showFavoritesButton = _viewModel.ShowFavoritesButton;
            _showPinnedButton = _viewModel.ShowPinnedButton;
            _showTags = _viewModel.ShowTags;

            // Видимость колонок (кроме закреплённой «Название») задаётся в том же
            // списке, что и порядок: флажки заполняются в InitializeColumnOrder.
            ShowNameColumnCheck.IsChecked = true;

            // «Действия только при наведении» (issue #128).
            if (ActionsHoverOnlyCheck != null)
                ActionsHoverOnlyCheck.IsChecked = _viewModel.ActionsHoverOnly;

            InitializeColumnOrder();

            ShowFavoritesButtonCheck.IsChecked = _showFavoritesButton;
            ShowPinnedButtonCheck.IsChecked = _showPinnedButton;
            ShowTagsCheck.IsChecked = _showTags;
            if (ShowTagFilterPanelCheck != null)
                ShowTagFilterPanelCheck.IsChecked = _viewModel.ShowTagFilterPanel;
            if (AllowMultipleInstancesCheck != null)
                AllowMultipleInstancesCheck.IsChecked = _viewModel.AllowMultipleInstances;
            // Режим обновления (issue #116): порядок вариантов ComboBox совпадает
            // с порядком значений enum AppUpdateMode.
            if (UpdateModeComboBox != null)
                UpdateModeComboBox.SelectedIndex = (int)_viewModel.UpdateMode;
            // Периодичность проверки обновлений (issue #116 v1.0.1.54): интервал
            // хранится в минутах и раскладывается на «значение + единица»
            // (минуты/часы/дни) для поля «Интервал», как в планировщике заданий.
            if (UpdateIntervalValueBox != null)
            {
                AppSettings.DecomposeUpdateInterval(
                    _viewModel.UpdateCheckIntervalMinutes, out var intervalValue, out var intervalUnit);
                UpdateIntervalValueBox.Text = intervalValue.ToString();
                if (UpdateIntervalUnitCombo != null)
                    UpdateIntervalUnitCombo.SelectedIndex = (int)intervalUnit;
            }
            if (ShowTrayIconCheck != null)
                ShowTrayIconCheck.IsChecked = _viewModel.ShowTrayIcon;
            if (CloseToTrayCheck != null)
                CloseToTrayCheck.IsChecked = _viewModel.CloseToTray;
            if (EscapeToTrayCheck != null)
                EscapeToTrayCheck.IsChecked = _viewModel.EscapeToTray;
            // Тема приложения следует за режимом ОС (issue #108).
            if (ThemeFollowsOsCheck != null)
                ThemeFollowsOsCheck.IsChecked = _viewModel.ThemeFollowsOs;
            if (ShowSystemNotificationsCheck != null)
                ShowSystemNotificationsCheck.IsChecked = _viewModel.ShowSystemNotifications;
            if (CatchUpMissedTasksCheck != null)
                CatchUpMissedTasksCheck.IsChecked = _viewModel.CatchUpMissedTasks;
            if (AfterLaunchActionCombo != null)
            {
                AfterLaunchActionCombo.ItemsSource = new[]
                {
                    LocalizationManager.T("Settings.General.AfterLaunchAction.None"),
                    LocalizationManager.T("Settings.General.AfterLaunchAction.Minimize"),
                    LocalizationManager.T("Settings.General.AfterLaunchAction.MinimizeToTray"),
                    LocalizationManager.T("Settings.General.AfterLaunchAction.Close")
                };
                AfterLaunchActionCombo.SelectedIndex = (int)Models.AfterLaunchActionHelper.Parse(_viewModel.AfterLaunchAction);
            }
            if (RememberWindowLayoutCheck != null)
                RememberWindowLayoutCheck.IsChecked = _viewModel.RememberWindowLayout;
            // Начальная установка переключателя компактного режима не должна влечь
            // повторное масштабирование главного окна («прыжок отступов», issue #199):
            // событие Checked/Unchecked при открытии настроек подавляем.
            if (CompactModeCheck != null)
            {
                _suppressCompactEvent = true;
                try
                {
                    CompactModeCheck.IsChecked = _viewModel.CompactMode;
                }
                finally
                {
                    _suppressCompactEvent = false;
                }
            }

            GroupByGroupCheck.IsChecked = _viewModel.GroupByGroup;
            ShowFavoritesOnlyCheck.IsChecked = _viewModel.ShowFavoritesOnly;
            if (ShowEmptyGroupsCheck != null)
                ShowEmptyGroupsCheck.IsChecked = _viewModel.ShowEmptyGroups;
            if (AddTimestampToExportFileNameCheck != null)
                AddTimestampToExportFileNameCheck.IsChecked = _viewModel.AddTimestampToExportFileName;

            // «Дополнение заголовка программы» (issue #28): галочка, шаблон и превью.
            if (TitleSuffixEnabledCheck != null)
                TitleSuffixEnabledCheck.IsChecked = _viewModel.TitleSuffixEnabled;
            if (TitleSuffixTemplateBox != null)
                TitleSuffixTemplateBox.Text = _viewModel.TitleSuffixTemplate;
            UpdateTitleSuffixPreview();

            if (ShowRightPanelDetailsCheck != null)
                ShowRightPanelDetailsCheck.IsChecked = _viewModel.ShowRightPanelDetails;
            if (ShowSessionLaunchPanelCheck != null)
                ShowSessionLaunchPanelCheck.IsChecked = _viewModel.ShowSessionLaunchPanel;
            if (StatusShowConnectionPathCheck != null)
                StatusShowConnectionPathCheck.IsChecked = _viewModel.StatusShowConnectionPath;
            if (StatusShowArchitectureCheck != null)
                StatusShowArchitectureCheck.IsChecked = _viewModel.StatusShowArchitecture;
            if (StatusShowLaunchModeCheck != null)
                StatusShowLaunchModeCheck.IsChecked = _viewModel.StatusShowLaunchMode;
            if (StatusShowPortCheck != null)
                StatusShowPortCheck.IsChecked = _viewModel.StatusShowPort;
            if (StatusShowPlatformVersionCheck != null)
                StatusShowPlatformVersionCheck.IsChecked = _viewModel.StatusShowPlatformVersion;
            if (StatusShowClientTypeCheck != null)
                StatusShowClientTypeCheck.IsChecked = _viewModel.StatusShowClientType;
            if (StatusShowConnectionTypeCheck != null)
                StatusShowConnectionTypeCheck.IsChecked = _viewModel.StatusShowConnectionType;
            if (StatusShowUserCheck != null)
                StatusShowUserCheck.IsChecked = _viewModel.StatusShowUser;
            if (StatusShowIdCheck != null)
                StatusShowIdCheck.IsChecked = _viewModel.StatusShowId;

            InitHotkeyCombos();
        }

        /// <summary>
        /// Обновляет превью дополнения заголовка (issue #28): показывает результат
        /// будущей подстановки шаблона (мелким шрифтом под полем ввода).
        /// </summary>
        private void UpdateTitleSuffixPreview()
        {
            if (TitleSuffixPreviewText is null)
                return;
            var template = TitleSuffixTemplateBox?.Text ?? string.Empty;
            var resolved = Services.TitleSuffixResolver.Resolve(template);
            TitleSuffixPreviewText.Text = string.Format(
                LocalizationManager.T("Settings.Display.TitleSuffixPreviewFormat"),
                resolved.Length == 0
                    ? LocalizationManager.T("Settings.Display.TitleSuffixPreviewEmpty")
                    : resolved);
        }

        /// <summary>Изменение шаблона дополнения заголовка: обновляет превью (issue #28).</summary>
        private void OnTitleSuffixTemplate_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateTitleSuffixPreview();
        }

        /// <summary>
        /// Клик по токену-подсказке: вставляет токен в поле шаблона дополнения
        /// заголовка (issue #28) и обновляет превью.
        /// </summary>
        private void OnTitleSuffixToken_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string token } || TitleSuffixTemplateBox is null)
                return;

            var current = TitleSuffixTemplateBox.Text ?? string.Empty;
            TitleSuffixTemplateBox.Text = current.Length == 0 ? token : current + token;
            TitleSuffixTemplateBox.Focus();
            TitleSuffixTemplateBox.SelectAll();
            UpdateTitleSuffixPreview();
        }
    }
}
#endif