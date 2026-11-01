using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Обновление из хранилищ» (0.3.9.88, Windows/WPF): список баз с
    /// заполненным хранилищем конфигурации, чекбоксы, последовательный прогон
    /// выбранных через конфигуратор в пакетном режиме и построчный лог.
    /// Вся логика — в чистой ViewModel <see cref="RepositoryBatchUpdateViewModel"/>.
    /// </summary>
    public partial class RepositoryBatchUpdateWindow : Window
    {
        private readonly RepositoryBatchUpdateViewModel _vm;

        public RepositoryBatchUpdateWindow(IEnumerable<Infobase> infobases)
        {
            InitializeComponent();
            _vm = new RepositoryBatchUpdateViewModel(infobases);
            DataContext = _vm;

            Title = LocalizationManager.T("RepoUpdate.Title");
            HintText.Text = LocalizationManager.T("RepoUpdate.Hint");
            LogHeader.Text = LocalizationManager.T("RepoUpdate.Log");

            ItemsList.ItemsSource = _vm.Items;
            LogList.ItemsSource = _vm.LogLines;
            _vm.PropertyChanged += OnVmPropertyChanged;
            ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += OnLogLinesChanged;

            // Если баз с хранилищем нет — окно сразу сообщает об этом.
            if (!_vm.HasItems)
                HintText.Text = LocalizationManager.T("RepoUpdate.NoBases");
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(RepositoryBatchUpdateViewModel.IsRunning))
            {
                UpdateButton.IsEnabled = !_vm.IsRunning;
                // Пока идёт прогон, нельзя менять состав выбранных баз.
                foreach (var item in _vm.Items)
                    item.IsBusy = _vm.IsRunning;
            }
        }

        private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && _vm.LogLines.Count > 0)
            {
                var last = _vm.LogLines[_vm.LogLines.Count - 1];
                LogList.ScrollIntoView(last);
            }
        }

        private void OnSelectAll_Click(object sender, RoutedEventArgs e) => _vm.SetAll(true);

        private void OnSelectNone_Click(object sender, RoutedEventArgs e) => _vm.SetAll(false);

        private async void OnUpdateSelected_Click(object sender, RoutedEventArgs e)
        {
            // v100120: async void — исключение не должно уходить в глобальный обработчик
            // без контекста операции (аудит async void, шаблон RelayCommand.Execute).
            try
            {
                await _vm.RunAsync();
            }
            catch (Exception ex)
            {
                try { AppServices.TryGetService<Services.IAppLogger>()?.Error("Ошибка пакетного обновления конфигураций из хранилища", ex); } catch { /* лог не роняет обработчик */ }
            }
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}