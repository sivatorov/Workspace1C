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
    public partial class MainWindow
    {

        private void UpdateThemeButton()
        {
            // Навигация 1.0.1.99: отдельная кнопка темы на панели заменена пунктом
            // «Ещё» → «Блокировка» → «Сменить тему»; обновлять глиф не нужно.
        }

        /// <summary>
        /// Обработчик смены языка интерфейса. Выполняется на UI-потоке (при необходимости через
        /// диспетчер), чтобы элементы, заданные в code-behind, обновились сразу без перезапуска.
        /// Индексаторные LocExtension-привязки XAML обновляются сами через
        /// <see cref="LocalizationManager.Source"/>, а остальные привязки обновляются
        /// принудительно через проход по визуальному дереву
        /// (<see cref="RefreshAllBindingsOnVisualTree"/>).
        /// </summary>
        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (Dispatcher.CheckAccess())
                RebuildAfterLanguageChange();
            else
                Dispatcher.BeginInvoke(new Action(RebuildAfterLanguageChange));
        }

        /// <summary>
        /// Пересобирает элементы интерфейса, заданные в code-behind, при смене языка:
        /// заголовок окна (перекрытый локальным значением с версией), подсказку кнопки смены темы,
        /// подсказку и меню трея. Работает для любого направления (ru <-> en и внешние языки).
        /// </summary>
        private void RebuildAfterLanguageChange()
        {
            // XAML-привязка Title="{loc:Loc App.Title}" была перекрыта в конструкторе
            // локальным значением с версией, поэтому заголовок собираем заново.
            UpdateWindowTitle();

            // Подсказка кнопки смены темы («Переключить на светлую/тёмную») зависит от языка.
            UpdateThemeButton();

            // Подсказка и меню трея тоже должны переключиться на новый язык.
            if (_trayIcon is not null)
            {
                _trayIcon.Text = LocalizationManager.T("App.Title");
                if (_trayIcon.ContextMenuStrip is Forms.ContextMenuStrip menu)
                    RebuildTrayMenu(menu);
            }

            // Контекстное меню строки базы (issue #56): строится кодом (MainWindow.ContextMenu.cs),
            // поэтому при смене языка пересобираем его, чтобы подписи/хоткеи обновились сразу.
            RebuildRowContextMenu();

            // Принудительно обновляем целевые значения всех привязок визуального/логического
            // дерева окна. Это чинит элементы, которые не реагируют на PropertyChanged("Item[]")
            // (например MultiBinding-подсказки кнопок запуска с Path="Source" + конвертер).
            RefreshAllBindingsOnVisualTree();
        }

        /// <summary>
        /// Пересобирает заголовок окна из текущих настроек. Вызывается после «ОК» окна
        /// настроек (изменение дополнения заголовка, issue #28).
        /// </summary>
        public void RefreshWindowTitle() => UpdateWindowTitle();

        /// <summary>
        /// Смена редакции (активация/удаление ключа ПРО): метка «★» в заголовке должна
        /// появляться/исчезать сразу, без перезапуска приложения. Событие
        /// <see cref="Services.Licensing.EditionService.Changed"/> поднимается в потоке
        /// активации (может быть фоновым), поэтому заголовок обновляется в UI-потоке.
        /// </summary>
        private void OnEditionServiceChanged(object? sender, EventArgs e)
        {
            void RefreshEdition()
            {
                UpdateWindowTitle();
                UpdateSectionButtonsVisibility();
            }

            if (Dispatcher.CheckAccess())
                RefreshEdition();
            else
                Dispatcher.BeginInvoke(new Action(RefreshEdition));
        }

        /// <summary>
        /// Собирает заголовок программы из базового локализованного имени приложения
        /// и суффикса версии «v<версия>». Метод устойчив к повторному вызову: если
        /// базовый заголовок уже содержит суффикс версии, он не добавляется повторно
        /// (защита от дублирования вида «v0.3.5.68 v0.3.5.68»).
        /// При действующем ключе ПРО перед названием ставится метка редакции «★»
        /// (см. <see cref="Services.Licensing.LicenseSummary.ProMark"/>).
        /// Обновляет и системный заголовок окна (Title), и видимую кастомную шапку
        /// (иконка + название, <see cref="AppTitleBlock"/>): система Windows использует
        /// WindowChrome с CaptionHeight="0", поэтому пользователь видит именно шапку,
        /// а не системную строку заголовка.
        /// </summary>
        private void UpdateWindowTitle()
        {
            var baseTitle = LocalizationManager.T("App.Title");

            // «Дополнение заголовка» (issue #28): настройки из ViewModel; в конструкторе
            // окна VM ещё может не существовать (первичный заголовок пересобирается
            // App.xaml.cs после создания окна), поэтому без VM дополнение не добавляем.
            var titleSuffixEnabled = false;
            var titleSuffixTemplate = string.Empty;
            if (_viewModel is not null)
            {
                titleSuffixEnabled = _viewModel.TitleSuffixEnabled;
                titleSuffixTemplate = _viewModel.TitleSuffixTemplate;
            }

            // Метка редакции ПРО — признак действующего лицензионного ключа. От VM не
            // зависит, поэтому выставляется и при первом вызове из конструктора
            // (_viewModel is null), и после активации/удаления ключа.
            var proMark = Services.Licensing.EditionService.IsPro
                ? Services.Licensing.LicenseSummary.ProMark
                : string.Empty;

            var fullTitle = TitleSuffixResolver.Compose(
                baseTitle, _infoVersion ?? string.Empty, titleSuffixEnabled, titleSuffixTemplate,
                proMark: proMark);

            // Системный заголовок окна (важен для панели задач и перечисления окон).
            Title = fullTitle;

            // Видимая шапка окна (иконка + название программы) — её и видит пользователь,
            // т.к. системная строка заголовка скрыта WindowChrome.
            if (AppTitleBlock is not null)
                AppTitleBlock.Text = fullTitle;
        }

        /// <summary>
        /// Обходит визуальное (и логическое, где необходимо) дерево окна и принудительно вызывает
        /// <c>UpdateTarget()</c> для всех найденных привязок через
        /// <see cref="BindingOperations.GetBindingExpressionBase(DependencyObject, DependencyProperty)"/>.
        /// Нужно для полноты обновления при смене языка: индексаторные привязки {loc:Loc}
        /// обновляются сами, а привязки с Path="Source" + конвертер (например
        /// MultiBinding-подсказки кнопок запуска) — только при вызове UpdateTarget().
        /// Все операции обёрнуты в try/catch, чтобы сбой в одной привязке не прерывал
        /// пересборку интерфейса.
        /// </summary>
        private void RefreshAllBindingsOnVisualTree()
        {
            var visited = new HashSet<DependencyObject>();
            try
            {
                // Корневой элемент содержимого окна.
                if (Content is DependencyObject root)
                    UpdateBindingTargetsRecursive(root, visited);
                // Само окно (заголовок, атрибуты окна и т.п.).
                UpdateBindingTargetsRecursive(this, visited);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[l10n] RefreshAllBindings failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Рекурсивно обходит визуальное дерево и дополняет его логическими детьми,
        /// отсутствующими в визуальном дереве (переход между визуальным и логическим
        /// деревом). Посещённые узлы не обрабатываются повторно.
        /// </summary>
        private static void UpdateBindingTargetsRecursive(DependencyObject d, HashSet<DependencyObject> visited)
        {
            if (d is null || !visited.Add(d))
                return;

            UpdateBindingTarget(d);

            int count;
            try { count = VisualTreeHelper.GetChildrenCount(d); }
            catch { return; }

            for (var i = 0; i < count; i++)
            {
                try
                {
                    var child = VisualTreeHelper.GetChild(d, i);
                    if (child is not null)
                        UpdateBindingTargetsRecursive(child, visited);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[l10n] visual child walk failed: " + ex.Message);
                }
            }

            // Переход между визуальным и логическим деревом.
            if (d is FrameworkElement fe)
            {
                try
                {
                    foreach (var logicalChild in LogicalTreeHelper.GetChildren(fe))
                    {
                        if (logicalChild is DependencyObject lo && !visited.Contains(lo))
                            UpdateBindingTargetsRecursive(lo, visited);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[l10n] logical child walk failed: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Обновляет целевые значения всех привязок на указанном элементе
        /// (включая MultiBinding). Обёрнуто в try/catch.
        /// </summary>
        private static void UpdateBindingTarget(DependencyObject d)
        {
            try
            {
                var enumerator = d.GetLocalValueEnumerator();
                while (enumerator.MoveNext())
                {
                    var dp = enumerator.Current.Property;
                    if (dp is null)
                        continue;

                    try
                    {
                        BindingOperations.GetBindingExpressionBase(d, dp)?.UpdateTarget();
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("[l10n] UpdateTarget(" + dp.Name + ") failed: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[l10n] GetLocalValueEnumerator failed: " + ex.Message);
            }
        }

    }
}
#endif
