#if LINUX
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Локализация главного окна (Avalonia/Linux): пересборка содержимого при смене
    /// языка и составление заголовка окна.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Пересобирает главное окно при смене языка интерфейса, чтобы названия колонок,
        /// кнопки правой панели и подсказки (создаваемые через <c>LocalizationManager.T(...)</c>)
        /// обновились на новый язык сразу, а не после перезапуска. Компактный режим
        /// (<see cref="UiMetrics.Compact"/>) при этом сохраняется; выделение и прокрутка
        /// списка восстанавливаются после пересборки содержимого.
        /// </summary>
        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                RebuildAfterLanguageChange();
            else
                Avalonia.Threading.Dispatcher.UIThread.Post(RebuildAfterLanguageChange);
        }

        /// <summary>
        /// Пересобирает заголовок окна из текущих настроек. Вызывается после «ОК» окна
        /// настроек (изменение дополнения заголовка, issue #28).
        /// </summary>
        public void RefreshWindowTitle() => Title = ComposeWindowTitle();

        private void RebuildAfterLanguageChange()
        {
            var selected = (object?)_vm?.SelectedInfobase ?? _vm?.SelectedGroupNode;
            var offset = TreeScroll?.Offset;

            Content = BuildRoot();
            Title = ComposeWindowTitle();

            // Обновляем меню и подсказку трея, чтобы подписи кнопок и ToolTip
            // тоже переключились на новый язык без перезапуска.
            if (_trayIcon is not null)
            {
                _trayIcon.Menu = BuildTrayMenu();
                _trayIcon.ToolTipText = LocalizationManager.T("App.Title");
            }

            // Выделение и прокрутка восстанавливаются после того, как новое дерево
            // построено и разложено (иначе строки ещё не существуют). Строка выбирается
            // по конкретному контейнеру, а не через SelectedItem: закреплённая база есть
            // в дереве дважды, и разрешение по данным нашло бы первую копию в узле
            // «Закреплённые» (issue #301).
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (selected is not null && _tree is not null && !ReferenceEquals(_tree.SelectedItem, selected))
                {
                    _tree.SelectionChanged -= OnTreeSelectionChanged;
                    try
                    {
                        if (FindFindInListRow(selected) is { } row)
                        {
                            _tree.SelectRow(row);
                            row.BringIntoView();
                        }
                    }
                    finally { _tree.SelectionChanged += OnTreeSelectionChanged; }
                }
                if (offset is { } off && TreeScroll is { } scroll)
                    scroll.Offset = off;
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Заголовок программы: локализованное имя, суффикс версии и (если включено
        /// в настройках, issue #28) дополнение из шаблона. Суффикс не задваивается
        /// при повторном вызове, как в версии для Windows (MainWindow.Language.cs:107-122).
        /// При действующем ключе ПРО перед названием ставится метка редакции «★»
        /// (<see cref="Services.Licensing.LicenseSummary.ProMark"/>).
        /// Пользователь видит эту строку в шапке окна, потому что системной строки
        /// заголовка на Linux у нас нет.
        /// </summary>
        private string ComposeWindowTitle()
        {
            var baseTitle = LocalizationManager.T("App.Title");
            var version = VersionInfo.Display();

            // Метка редакции ПРО — признак действующего лицензионного ключа.
            var proMark = Services.Licensing.EditionService.IsPro
                ? Services.Licensing.LicenseSummary.ProMark
                : string.Empty;

            return TitleSuffixResolver.Compose(
                baseTitle,
                version,
                _vm?.TitleSuffixEnabled ?? false,
                _vm?.TitleSuffixTemplate ?? string.Empty,
                proMark: proMark,
                // %app_user% — имя активной учётной записи (issue #164).
                appUser: _vm?.TitleSuffixAppUser);
        }
    }
}
#endif