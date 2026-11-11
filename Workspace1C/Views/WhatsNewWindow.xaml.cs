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
        private bool _githubFallbackLoaded;

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
            if (notes is not null)
            {
                // Краткая сводка: один скролл-контейнер (ScrollViewer → StackPanel);
                // колёсико доходит до содержимого напрямую (issue #116 п.1).
                var panel = new StackPanel();
                foreach (var note in notes)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "• " + note,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                }
                NotesHost.Children.Add(new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = panel
                });
            }
            else
            {
                // Issue #116 п.1: для версии без готовой сводки — fallback на body
                // GitHub-релиза текущего тега (после показа окна, не в конструкторе).
                Loaded += async (_, _) => await LoadGithubNotesAsync();
            }
        }

        private async System.Threading.Tasks.Task LoadGithubNotesAsync()
        {
            if (_githubFallbackLoaded)
                return;
            _githubFallbackLoaded = true;
            try
            {
                // Fallback-порядок сводки (issue #116 v1.0.1.54): body GitHub-релиза
                // текущего тега → локальная секция CHANGELOG.md рядом с exe →
                // текст-заглушка. Окно никогда не остаётся с пустым содержимым.
                var body = await WhatsNewService.GetGithubReleaseNotesAsync(WhatsNewService.CurrentVersion);
                if (string.IsNullOrWhiteSpace(body))
                    body = WhatsNewService.TryGetChangelogSection(WhatsNewService.CurrentVersion);
                if (string.IsNullOrWhiteSpace(body))
                {
                    NotesHost.Children.Add(new TextBlock
                    {
                        Text = LocalizationManager.T("WhatsNew.NotesUnavailable"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0),
                        Opacity = 0.7
                    });
                    return;
                }
                // Markdown-текст GitHub-релиза: FlowDocumentScrollViewer сам является
                // скролл-контейнером — кладём его в хост НАПРЯМУЮ, без внешнего
                // ScrollViewer. Раньше вложенный FlowDocumentScrollViewer перехватывал
                // колесо мыши, и текст не скроллился (issue #116 п.1, v1.0.1.50).
                NotesHost.Children.Clear();
                NotesHost.Children.Add(new System.Windows.Controls.FlowDocumentScrollViewer
                {
                    Document = MarkdownRenderer.ToFlowDocument(body),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                });
            }
            catch
            {
                // Сбой загрузки сводки не роняет диалог; пробуем локальный CHANGELOG,
                // иначе показываем заглушку (окно никогда не остаётся пустым —
                // issue #116 v1.0.1.54).
                var changelog = WhatsNewService.TryGetChangelogSection(WhatsNewService.CurrentVersion);
                NotesHost.Children.Clear();
                if (!string.IsNullOrWhiteSpace(changelog))
                {
                    NotesHost.Children.Add(new System.Windows.Controls.FlowDocumentScrollViewer
                    {
                        Document = MarkdownRenderer.ToFlowDocument(changelog),
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                    });
                }
                else
                {
                    NotesHost.Children.Add(new TextBlock
                    {
                        Text = LocalizationManager.T("WhatsNew.NotesUnavailable"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 4, 0, 0),
                        Opacity = 0.7
                    });
                }
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
