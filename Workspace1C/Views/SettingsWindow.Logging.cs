#if WINDOWS
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C
{
    public partial class SettingsWindow
    {
        /// <summary>Соответствие «категория → чекбокс» вкладки «Логирование» (issue #80).</summary>
        private readonly Dictionary<string, CheckBox> _loggingCategoryChecks = new();

        /// <summary>
        /// Инициализирует вкладку «Логирование» (issue #80 «Настройка логирования»):
        /// общий выключатель «CM_ON» и списки категорий (направления + диагностические
        /// флаги <c>trace.json</c>) со значениями из конфига. По умолчанию все категории
        /// выключены. Значения читаются из <c>trace.json</c> через <see cref="TraceFlags"/>
        /// и применяются без перезапуска (конфиг перечитывается при изменении файла).
        /// </summary>
        private void InitializeLoggingTab()
        {
            try
            {
                if (LoggingMasterOnCheck != null)
                    LoggingMasterOnCheck.IsChecked = TraceFlags.IsLoggingMasterOn();
            }
            catch { /* конфиг может быть недоступен — используем дефолт (выключен) */ }

            if (LoggingDirectionsPanel != null)
                PopulateLoggingPanel(LoggingDirectionsPanel, LogCategory.DirectionCategories);
            if (LoggingDiagnosticsPanel != null)
                PopulateLoggingPanel(LoggingDiagnosticsPanel, LogCategory.DiagnosticCategories);

            // issue #143 (1.0.1.125): диагностика резервного копирования СУБД.
            if (DbmsBackupDiagnosticsCheck != null)
                DbmsBackupDiagnosticsCheck.IsChecked = _viewModel.DbmsBackupDiagnosticsEnabled;
        }

        /// <summary>Заполняет панель строками-галочками всех переданных категорий.</summary>
        private void PopulateLoggingPanel(Panel panel, IEnumerable<string> categories)
        {
            foreach (var category in categories)
            {
                if (string.IsNullOrWhiteSpace(category))
                    continue;
                var check = BuildLoggingCategoryCheck(category);
                _loggingCategoryChecks[category] = check;
                panel.Children.Add(check);
            }
        }

        /// <summary>
        /// Строка списка «как для колонок»: галочка + название категории и пояснение
        /// под ним (вторичный цвет темы). Состояние — фактическое значение флага
        /// категории в <c>trace.json</c> (общий выключатель CM_ON учитывается отдельно).
        /// </summary>
        private CheckBox BuildLoggingCategoryCheck(string category)
        {
            var title = new TextBlock
            {
                Text = LocalizationManager.T(LogCategory.TitleKey(category)),
                VerticalAlignment = VerticalAlignment.Center
            };
            var description = new TextBlock
            {
                Text = LocalizationManager.T(LogCategory.DescriptionKey(category)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            };
            description.Foreground = FindThemeBrush("TextSecondaryBrush");
            var content = new StackPanel
            {
                Children = { title, description }
            };

            var check = new CheckBox
            {
                Content = content,
                Margin = new Thickness(0, 0, 0, 8),
                VerticalContentAlignment = VerticalAlignment.Center,
                IsChecked = false
            };
            try
            {
                check.IsChecked = TraceFlags.IsEnabled(category);
            }
            catch { /* конфиг недоступен — галочка остаётся снятой (дефолт) */ }
            return check;
        }

        /// <summary>Поиск кисти темы по имени ресурса; null — наследуемая кисть.</summary>
        private static Brush? FindThemeBrush(string key)
        {
            try
            {
                if (Application.Current?.TryFindResource(key) is Brush brush)
                    return brush;
            }
            catch { /* ресурс может отсутствовать на раннем этапе */ }
            return null;
        }

        /// <summary>
        /// Сохраняет настройки вкладки «Логирование» в <c>trace.json</c> (issue #80):
        /// галочки категорий (направления и диагностика) и общий выключатель «CM_ON».
        /// Записываются только известные флаги; рукописные значения конфига сохраняются.
        /// Применяется сразу после сохранения окна: <see cref="TraceFlags"/> перечитывает
        /// конфиг при изменении файла — перезапуск не требуется.
        /// </summary>
        private void SaveLoggingSettings()
        {
            var values = new Dictionary<string, bool>
            {
                [LogCategory.MasterOn] = LoggingMasterOnCheck?.IsChecked == true
            };
            foreach (var pair in _loggingCategoryChecks)
                values[pair.Key] = pair.Value.IsChecked == true;
            TraceFlags.SaveCategorySettings(values);

            // issue #143 (1.0.1.125): переключатель диагностики СУБД хранится в
            // settings.json (не в trace.json) и применяется сразу в сеттере.
            if (DbmsBackupDiagnosticsCheck != null)
                _viewModel.DbmsBackupDiagnosticsEnabled = DbmsBackupDiagnosticsCheck.IsChecked == true;
        }
    }
}
#endif
