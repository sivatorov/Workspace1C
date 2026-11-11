#if WINDOWS
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C;

/// <summary>
/// Обработчики пунктов меню функции «Проверка обновлений конфигураций 1С»
/// (функции №21/№22): «Связать с конфигурацией» и «Список типовых конфигураций».
/// Частичный класс <see cref="MainWindow"/> (Windows/WPF).
/// </summary>
public partial class MainWindow
{
    private void OnConfigUpdateLinkMenuClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedInfobase is not null)
            _viewModel.OpenConfigUpdateLink(_viewModel.SelectedInfobase);
    }

    private void OnConfigTypesEditMenuClick(object sender, RoutedEventArgs e)
        => _viewModel.OpenConfigTypesEdit();

    private void OnItsAccountsMenuClick(object sender, RoutedEventArgs e)
        => _viewModel.OpenItsAccounts();

    /// <summary>
    /// Ручная проверка обновлений приложения из подменю «Утилиты» верхней панели (issue #279).
    /// Та же проверка, что во вкладке «О программе»: сообщает явный результат (актуальная
    /// версия / ошибка / доступно обновление) через UpdateService.
    /// </summary>
    private async void OnCheckForUpdatesMenuClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var updateService = AppServices.GetRequiredService<UpdateService>();
            await updateService.CheckForUpdatesManualAsync();
        }
        catch
        {
            // Внутренние ошибки уже показаны в UpdateService; здесь только страхуемся.
        }
    }

    // ---- Подвал строки состояния: версия и ссылки обновления (issue #116) ----

    /// <summary>
    /// Инициализирует блок версии в подвале строки состояния (issue #116): текст
    /// версии, подписка на смену состояния «доступна новая версия» службы обновления
    /// и первичная расстановка видимости ссылок «Обновить»/«Проверить обновления».
    /// </summary>
    private void InitializeFooterUpdateState()
    {
        FooterVersionText.Text = "v" + VersionInfo.Display();

        try
        {
            var updateService = AppServices.TryGetService<UpdateService>();
            if (updateService is not null)
            {
                updateService.AvailableReleaseChanged += OnAvailableReleaseChanged;
                // Событие приходит из фоновой проверки (чужой поток) — обновляем UI
                // через Dispatcher; первичное состояние расставляем отложенно,
                // к этому моменту App успеет применить режим обновления.
                Dispatcher.BeginInvoke(
                    new Action(RefreshFooterUpdateState),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
        }
        catch
        {
            // Состояние ссылок не критично: остаётся только версия.
        }
    }

    /// <summary>Обработчик смены «доступна новая версия» в службе обновления (issue #116).</summary>
    private void OnAvailableReleaseChanged()
        => Dispatcher.Invoke(RefreshFooterUpdateState);

    /// <summary>
    /// Расставляет три состояния значка обновления в статус-баре (issue #116,
    /// п.3 v1.0.1.50): скрыт — в авто-режимах без доступного релиза; синие стрелки
    /// синхронизации — ручная проверка доступна (режимы «только уведомлять» и
    /// «проверять вручную»), релиза нет; значок «внимание» (восклицательный знак
    /// в треугольнике) в акцентном цвете — есть доступный релиз (issue #116
    /// v1.0.1.54: жирные красные «!!!» вводили в заблуждение — красный «!!»
    /// в логах означает серьёзные ошибки). Tooltip меняется вместе с состоянием;
    /// клик — единая ручная проверка.
    /// </summary>
    private void RefreshFooterUpdateState()
    {
        try
        {
            var updateService = AppServices.TryGetService<UpdateService>();
            if (updateService is null)
                return;

            var hasRelease = updateService.AvailableRelease is not null;
            var manualCheck = updateService.Mode is AppUpdateMode.NotifyOnly or AppUpdateMode.Manual;

            FooterUpdateIcon.Visibility = hasRelease || manualCheck
                ? Visibility.Visible
                : Visibility.Collapsed;
            FooterUpdateSyncIcon.Visibility = !hasRelease ? Visibility.Visible : Visibility.Collapsed;
            FooterUpdateWarnIcon.Visibility = hasRelease ? Visibility.Visible : Visibility.Collapsed;

            FooterUpdateIcon.ToolTip = hasRelease
                ? string.Format(
                    LocalizationManager.T("Main.Footer.UpdateAvailableTooltip"),
                    updateService.AvailableRelease!.TagName)
                : (object)LocalizationManager.T("Main.Footer.CheckUpdates");
        }
        catch
        {
            // Состояние значка не критично: остаётся только версия.
        }
    }

    /// <summary>Клик по версии в подвале — окно «Что нового» (issue #116).</summary>
    private void OnFooterVersionClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var settings = AppServices.GetRequiredService<IInfobaseRepository>().LoadSettings();
            var dialog = new WhatsNewWindow(settings) { Owner = this };
            dialog.ShowDialog();
        }
        catch
        {
            // Диалог не должен ронять приложение.
        }
    }

    /// <summary>Клик по значку обновления в подвале — та же ручная проверка, что и в
    /// «О программе»: она откроет диалог обновления для доступного релиза
    /// (issue #116, п.3 v1.0.1.50).</summary>
    private async void OnFooterUpdateIconClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var updateService = AppServices.GetRequiredService<UpdateService>();
            await updateService.CheckForUpdatesManualAsync();
        }
        catch
        {
            // Внутренние ошибки уже показаны в UpdateService; здесь только страхуемся.
        }
    }

    /// <summary>
    /// Открывает окно «Задания по расписанию» из подменю «Утилиты» (issue #286).
    /// Окно отображается отложенно, чтобы контекстное меню успело закрыться
    /// (та же проблема и решение, что в issue #288).
    /// </summary>
    private void OnScheduledTasksMenuClick(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(
            new System.Action(() =>
            {
                var win = new ScheduledTasksWindow();
                win.Owner = this;
                win.ShowDialog();
            }),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Открывает выпадающее меню «Утилиты» верхней панели по клику на её кнопке (issue #262):
    /// глобальные команды, не привязанные к конкретной базе. Начиная с 0.3.12.29 (issue #374)
    /// меню строится ДИНАМИЧЕСКИ по каталогу UtilitiesMenuCatalog + компоновке
    /// UtilitiesMenuLayout (стандартная или пользовательская из utilities_menu_layout.json),
    /// а не статическим XAML, как раньше.
    /// </summary>
    private void OnUtilitiesMenuButton_Click(object sender, RoutedEventArgs e)
    {
        // Тот же паттерн, что и у остальных меню верхней панели (OnEnterpriseMenuClick,
        // OnConfiguratorMenuClick, OnClearCacheMenuClick): раскрываем меню под кнопкой
        // (PlacementMode.Bottom) и передаём DataContext окна, чтобы Command/InputGestureText
        // пунктов резолвились, а меню позиционировалось как в Linux (issue #282).
        if (sender is not System.Windows.Controls.Button btn)
            return;

        // Меню строится заново при каждом открытии: компоновка могла измениться
        // (правка структуры в редакторе, смена настройки UtilitiesMenuEditingEnabled).
        // ВАЖНО: меню НЕ присваивается кнопке (btn.ContextMenu) — иначе оно затрёт
        // XAML-контекстное меню настроек (ПКМ по «Утилиты», issue #374): после первого
        // левого клика правая кнопка открывала бы меню «Утилиты» вместо настроек.
        // Меню открывается напрямую через IsOpen (как в Avalonia: utilitiesMenu.Open).
        var menu = BuildUtilitiesMenu();
        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.DataContext = DataContext;

        // Открываем меню ОТЛОЖЕННО (как OnLaunchSplitButton_Click, см. MainWindow.Events.cs):
        // при синхронном открытии прямо в обработчике клика WPF не успевает измерить и
        // отрисовать пункты меню, из-за чего на Windows показывались только иконки без
        // текста заголовков (issue #282). Отложенное открытие на приоритете Input даёт
        // меню корректно раскрыться — заголовки, иконки и команды видны.
        Dispatcher.BeginInvoke(
            new Action(() => menu.IsOpen = true),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// ПКМ по заголовку «Утилиты» (issue #374): сразу открывает редактор структуры меню,
    /// минуя окно настроек и контекстное меню с единственной командой. Событие помечается
    /// обработанным, чтобы не сработал левый клик (открытие меню «Утилиты»).
    /// </summary>
    private void OnUtilitiesButton_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var store = AppServices.TryGetService<IUtilitiesMenuLayoutStore>();
        var win = new UtilitiesMenuEditorWindow(store) { Owner = this };
        win.ShowDialog();
    }
}
#endif