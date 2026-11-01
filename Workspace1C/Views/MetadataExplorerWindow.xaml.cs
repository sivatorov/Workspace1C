#if WINDOWS
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Обозреватель метаданных…» (цикл 0.3.9.132–0.3.9.136, этап 2, Windows/WPF):
    /// просмотр дерева метаданных конфигурации 1С БЕЗ интерактивного конфигуратора —
    /// источник (база или файл .cf) выгружается в XML через /DumpConfigToFiles
    /// (<see cref="MetadataExplorerService"/>), дерево «Конфигурация → подсистемы → типы →
    /// объекты» строится лениво (<see cref="MetadataTreeNodeViewModel"/>, дети подгружаются
    /// при раскрытии узла), панель справа показывает детали выбранного объекта.
    /// Вся логика — в чистой ViewModel <see cref="MetadataExplorerViewModel"/>;
    /// окно открывает окно прогресса на время выгрузки и удаляет временный каталог
    /// выгрузки в Closed (Dispose).
    /// </summary>
    public partial class MetadataExplorerWindow : Window
    {
        private readonly MetadataExplorerViewModel _vm;

        /// <param name="bases">Все информационные базы списка (для ComboBox).</param>
        /// <param name="selectedBase">Предвыбранная база (выбранная в главном окне).</param>
        public MetadataExplorerWindow(IReadOnlyList<Infobase> bases, Infobase? selectedBase)
        {
            InitializeComponent();

            var service = AppServices.GetRequiredService<IMetadataExplorerService>();
            var dialogs = AppServices.GetRequiredService<IDialogService>();

            _vm = new MetadataExplorerViewModel(
                bases,
                selectedBase,
                service,
                dialogs,
                action => Application.Current?.Dispatcher.BeginInvoke(action));

            DataContext = _vm;
            Title = LocalizationManager.T("MetadataExplorer.Title");

            // Подсказка (этап 5): напоминание о времени выгрузки большой конфигурации
            // и об ограничении «разбираются только метаданные» (модули не читаются).
            HintText.Text = LocalizationManager.T("MetadataExplorer.Hint")
                + Environment.NewLine
                + LocalizationManager.T("MetadataExplorer.Hint.LargeConfig");

            // Раскрытие узла → ленивая подгрузка детей (TreeViewItem.Expanded всплывает
            // от любого вложенного контейнера — подписка один раз на дерево).
            MetadataTree.AddHandler(
                TreeViewItem.ExpandedEvent,
                new RoutedEventHandler(OnTreeItemExpanded));

            MetadataTree.SelectedItemChanged += OnTreeSelectionChanged;

            Closed += (_, _) => _vm.Dispose();
        }

        /// <summary>
        /// Команда «Загрузить» ViewModel — для интеграции из отчёта сравнения (этап 5):
        /// кнопка «Обозреватель метаданных…» в <see cref="ConfigDiffResultWindow"/>
        /// вызывает <c>LoadCommand.Execute(null)</c> сразу после открытия окна.
        /// </summary>
        public System.Windows.Input.ICommand LoadCommand => _vm.LoadCommand;

        /// <summary>«Загрузить»: окно прогресса (индитерминант, этапы из StageChanged) +
        /// выполнение выгрузки; результат — дерево, ошибки — в статус-строку VM.</summary>
        private async void OnLoad_Click(object sender, RoutedEventArgs e)
        {
            // v100120: async void — исключение не должно уходить в глобальный обработчик
            // без контекста операции (аудит async void, шаблон RelayCommand.Execute).
            var progress = new MetadataExplorerProgressWindow { Owner = this };
            progress.SetStage(LocalizationManager.T("MetadataExplorer.Status.Dumping"));
            Action<string> onStage = progress.SetStage;
            _vm.StageChanged += onStage;
            progress.Show();
            try
            {
                await _vm.LoadAsync();
            }
            catch (Exception ex)
            {
                try { AppServices.TryGetService<Services.IAppLogger>()?.Error("Ошибка загрузки метаданных", ex); } catch { /* лог не роняет обработчик */ }
            }
            finally
            {
                _vm.StageChanged -= onStage;
                progress.Close();
            }
        }

        private void OnTreeItemExpanded(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is TreeViewItem item &&
                item.DataContext is MetadataTreeNodeViewModel node)
            {
                node.EnsureLoaded();
                // issue #365: после ленивой загрузки детей пересчитываем видимость
                // контейнеров — подсистема, у которой фильтр скрыл всех потомков, прячется.
                _vm.RefreshNodeVisibility(node);
            }
        }

        private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            _vm.SelectedNode = MetadataTree.SelectedItem as MetadataTreeNodeViewModel;
        }

        /// <summary>«Очистить»: сброс текста поиска (возврат в режим дерева).</summary>
        private void OnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            _vm.SearchText = string.Empty;
            MetadataTree.Focus();
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
#endif