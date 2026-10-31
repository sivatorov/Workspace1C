using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Workspace1C.Localization;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «Что нового» после обновления (v100112, F3, Windows/WPF): компактное окно
    /// с краткой сводкой изменений релиза (захардкоженный текст через
    /// <see cref="WhatsNewService.GetReleaseNotes"/>), галкой «Показывать при запуске»
    /// (<see cref="Models.AppSettings.ShowWhatsNewOnStart"/>) и кнопкой «Открыть CHANGELOG
    /// на GitHub». Тонкая обёртка — логика показа/маркировки в <see cref="WhatsNewService"/>.
    /// </summary>
    public partial class WhatsNewWindow : Window
    {
        private readonly Models.AppSettings _settings;

        /// <param name="settings">Настройки профиля: читаются галкой, сохраняет вызывающий.</param>
        public WhatsNewWindow(Models.AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            var version = WhatsNewService.CurrentVersion;
            TitleText.Text = string.Format(
                LocalizationManager.T("WhatsNew.TitleFormat"), version);
            Title = string.Format(
                LocalizationManager.T("WhatsNew.TitleFormat"), version);
            ShowOnStartCheck.IsChecked = settings.ShowWhatsNewOnStart;

            var notes = WhatsNewService.GetReleaseNotes(version);
            foreach (var note in notes ?? Array.Empty<string>())
            {
                NotesPanel.Children.Add(new TextBlock
                {
                    Text = "• " + note,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });
            }
        }

        /// <summary>Применяет галку «Показывать при запуске» к настройкам (сохраняет вызывающий).</summary>
        public void ApplyShowOnStart() =>
            _settings.ShowWhatsNewOnStart = ShowOnStartCheck.IsChecked == true;

        private void OnOpenChangelog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = WhatsNewService.ChangelogUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Открытие браузера — вспомогательное действие: сбой не роняет диалог.
            }
        }

        private void OnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
