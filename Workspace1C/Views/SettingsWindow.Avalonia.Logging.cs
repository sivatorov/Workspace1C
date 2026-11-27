#if !WINDOWS
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C
{
    public partial class SettingsWindow
    {
        /// <summary>Общий выключатель «CM_ON» вкладки «Логирование» (issue #80).</summary>
        private CheckBox? _loggingMasterOnCheck;

        /// <summary>
        /// Переключатель «Диагностика резервного копирования СУБД» (issue #143, 1.0.1.125):
        /// включает CM_DEBUG_DBMS и хранится в settings.json.
        /// </summary>
        private CheckBox? _dbmsBackupDiagnosticsCheck;

        /// <summary>Соответствие «категория → флажок» вкладки «Логирование» (issue #80).</summary>
        private readonly Dictionary<string, CheckBox> _loggingCategoryChecks = new();

        /// <summary>
        /// Строит вкладку «Логирование» (issue #80 «Настройка логирования»): вводное
        /// пояснение, общий выключатель «CM_ON» и список категорий «как для колонок» —
        /// флажок с названием и пояснением (HelpLink). По умолчанию все категории
        /// выключены; ERROR/WARN пишутся всегда. Значения читаются из <c>trace.json</c>
        /// через <see cref="TraceFlags"/>, изменения сохраняются туда же при «Сохранить»
        /// и применяются без перезапуска.
        /// </summary>
        private Control BuildLoggingTab()
        {
            var panel = new StackPanel { Spacing = 10 };

            panel.Children.Add(Hint(LocalizationManager.T("Settings.Logging.Description"), bottom: 4));

            // Общий выключатель «CM_ON».
            // Issue #175 п.7 (1.0.1.129): в Windows это CheckBox — галочка слева от
            // подписи. Раньше здесь стоял SettingsSwitch (ToggleButton с дорожкой
            // справа), из-за чего галка оказывалась «сильно справа».
            _loggingMasterOnCheck = LoggingCheck(
                "Settings.Logging.MasterOn", TraceFlags.IsLoggingMasterOn(),
                helpTextKey: "Settings.Logging.MasterOnDesc");
            _loggingMasterOnCheck.Margin = new Thickness(0, 0, 0, 6);
            panel.Children.Add(_loggingMasterOnCheck);

            // Issue #175 (1.0.1.127): группы вкладки — такие же карточки с видимой
            // рамкой, как остальные группы настроек (раньше были только заголовки
            // без границ, и группы визуально «слипались» с фоном).
            // Issue #175 (1.0.1.128): внутренний отступ карточки увеличен до 10×8,
            // чтобы галочки не сливались с краем рамки.
            var directions = new StackPanel { Spacing = 8 };
            AddLoggingCategories(directions, LogCategory.DirectionCategories);
            panel.Children.Add(SettingsGroup(
                LocalizationManager.T("Settings.Logging.Directions"),
                directions, new Thickness(10, 8), 10));

            // Существовавшие до issue #80 диагностические флаги trace.json.
            var diagnostics = new StackPanel { Spacing = 8 };
            AddLoggingCategories(diagnostics, LogCategory.DiagnosticCategories);

            // issue #143 (1.0.1.125): диагностика резервного копирования СУБД
            // (CM_DEBUG_DBMS=1) — настройка приложения, а не trace.json.
            _dbmsBackupDiagnosticsCheck = LoggingCheck(
                "Settings.Logging.DbmsDiagnostics", _viewModel.DbmsBackupDiagnosticsEnabled,
                helpTextKey: "Settings.Logging.DbmsDiagnosticsDesc");
            _dbmsBackupDiagnosticsCheck.Margin = new Thickness(0, 8, 0, 0);
            diagnostics.Children.Add(_dbmsBackupDiagnosticsCheck);

            panel.Children.Add(SettingsGroup(
                LocalizationManager.T("Settings.Logging.Diagnostics"),
                diagnostics, new Thickness(10, 8), 0));

            return panel;
        }

        /// <summary>
        /// Флажок настройки вкладки «Логирование» в виде Windows (issue #175 п.7):
        /// галочка слева, подпись справа, при необходимости — знак «?» с пояснением.
        /// Использует общий стиль <c>CheckBox</c> (ModernMaterialCheckBox), где
        /// <c>PART_Box</c> стоит в первой колонке, а содержимое — во второй.
        /// </summary>
        private static CheckBox LoggingCheck(string textKey, bool value, string? helpTextKey = null)
        {
            var caption = new TextBlock
            {
                Text = LocalizationManager.T(textKey),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };

            Control content;
            if (helpTextKey is null)
            {
                content = caption;
            }
            else
            {
                // Grid (Auto,Auto), как в WPF-разметке: подпись, затем «?».
                // Горизонтальный StackPanel здесь не нужен, но и не мешает:
                // подпись короткая, перенос не требуется.
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto") };
                Grid.SetColumn(caption, 0);
                row.Children.Add(caption);
                var help = new HelpLink
                {
                    HelpText = LocalizationManager.T(helpTextKey),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                Grid.SetColumn(help, 1);
                row.Children.Add(help);
                content = row;
            }

            return new CheckBox
            {
                Content = content,
                IsChecked = value,
                // Единое выравнивание по левому краю: галочка и подпись начинаются
                // у левой границы карточки, как в Windows.
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>Добавляет строки-флажки переданных категорий в панель вкладки.</summary>
        private void AddLoggingCategories(StackPanel panel, IEnumerable<string> categories)
        {
            foreach (var category in categories)
            {
                if (string.IsNullOrWhiteSpace(category))
                    continue;
                var check = LoggingCategoryCheck(category);
                _loggingCategoryChecks[category] = check;
                panel.Children.Add(check);
            }
        }

        /// <summary>
        /// Строка категории вкладки «Логирование» в виде Windows: галочка слева,
        /// справа — название и пояснение под ним (вторичный цвет темы).
        /// </summary>
        private static CheckBox LoggingCategoryCheck(string category)
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
            Themes.ThemeBrushes.Bind(description, TextBlock.ForegroundProperty, "TextSecondaryBrush");

            var content = new StackPanel
            {
                Children = { title, description }
            };

            return new CheckBox
            {
                Content = content,
                Margin = new Thickness(0),
                IsChecked = TraceFlags.IsEnabled(category),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>
        /// Сохраняет настройки вкладки «Логирование» в <c>trace.json</c> (issue #80):
        /// общий выключатель «CM_ON» и галочки направлений/диагностики. Записываются
        /// только известные флаги; рукописные значения конфига сохраняются.
        /// Применяется сразу — <see cref="TraceFlags"/> перечитывает конфиг при
        /// изменении файла, перезапуск не требуется.
        /// </summary>
        private void SaveLoggingSettingsAvalonia()
        {
            var values = new Dictionary<string, bool>
            {
                [LogCategory.MasterOn] = _loggingMasterOnCheck?.IsChecked == true
            };
            foreach (var pair in _loggingCategoryChecks)
                values[pair.Key] = pair.Value.IsChecked == true;
            TraceFlags.SaveCategorySettings(values);

            // issue #143 (1.0.1.125): диагностика СУБД хранится в settings.json и
            // применяется сразу в сеттере MainViewModel.
            if (_dbmsBackupDiagnosticsCheck != null)
                _viewModel.DbmsBackupDiagnosticsEnabled = _dbmsBackupDiagnosticsCheck.IsChecked == true;
        }
    }
}
#endif
