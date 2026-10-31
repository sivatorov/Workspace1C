using System.Windows;
using Workspace1C.Localization;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «Регламентные задания сервера» (v100112, F1, Windows/WPF): список заданий
    /// кластера через rac «job list» с фильтром по информационной базе и действиями
    /// «Запустить сейчас»/«Перезапустить»/«Приостановить»/«Возобновить» (rac «job
    /// pause/resume/disable/enable»), обновление списка. Тонкая обёртка: вся логика —
    /// в чистой ViewModel <see cref="ServerJobsViewModel"/>; окно задаёт только открытие
    /// окна «Детали задания» (<see cref="JobDetailsWindow"/>).
    /// </summary>
    public partial class ServerJobsWindow : Window
    {
        private readonly ServerJobsViewModel _vm;

        public ServerJobsWindow(ServerJobsViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            Title = LocalizationManager.T("ServerMonitor.Jobs.Title");

            // Окно «Детали задания» открывает платформенное окно (VM не знает о фреймворке).
            _vm.ShowDetails = job =>
            {
                var details = new JobDetailsWindow(job.DetailsText) { Owner = this };
                details.ShowDialog();
            };
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Первичная загрузка списка после показа окна — rac вызывается асинхронно,
            // UI-поток не блокируется.
            await _vm.LoadJobsAsync();
        }
    }
}
