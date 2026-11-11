#if LINUX
using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог «Что нового» после обновления (v100112, F3, Linux/Avalonia): компактное окно
    /// с краткой сводкой изменений релиза (захардкоженный текст через
    /// <see cref="WhatsNewService.GetReleaseNotes"/>), галкой «Показывать при запуске»
    /// (<see cref="Models.AppSettings.ShowWhatsNewOnStart"/>) и кнопкой «Открыть CHANGELOG
    /// на GitHub». Собрано в коде из общих помощников <see cref="AvaloniaUi"/> — стили и
    /// темы без хардкод-цветов. Логика показа/маркировки — в <see cref="WhatsNewService"/>.
    /// </summary>
    public sealed class WhatsNewWindow : ModalWindowBase
    {
        private readonly Models.AppSettings _settings;
        private readonly CheckBox _showOnStartCheck;
        private bool _githubFallbackLoaded;

        /// <param name="settings">Настройки профиля: читаются галкой, сохраняет вызывающий.</param>
        public WhatsNewWindow(Models.AppSettings settings)
        {
            _settings = settings;
            Title = LocalizationManager.T("WhatsNew.Title");
            Width = 560;
            Height = 420;
            MinWidth = 440;
            MinHeight = 320;
            FontSize = 13;
            CanResize = true;

            var version = WhatsNewService.CurrentVersion;

            var root = AvaloniaUi.Rows(0, -1, 0);
            root.Margin = new Thickness(20);

            // Заголовок: «Что нового в Workspace 1C <версия>».
            var title = AvaloniaUi.Text(
                string.Format(LocalizationManager.T("WhatsNew.TitleFormat"), version),
                16, bold: true);
            title.Margin = new Thickness(0, 0, 0, 12);
            AvaloniaUi.Place(root, title, 0);

            // Сводка изменений релиза: пункты списком.
            var notesPanel = new StackPanel { Orientation = Orientation.Vertical };
            var notes = WhatsNewService.GetReleaseNotes(version);
            foreach (var note in notes ?? Array.Empty<string>())
            {
                var item = AvaloniaUi.Text("• " + note, 13);
                item.Margin = new Thickness(0, 0, 0, 8);
                notesPanel.Children.Add(item);
            }

            if (notes is null)
            {
                // Issue #116 п.1: для версии без готовой сводки — fallback на body
                // GitHub-релиза текущего тега (после показа окна, не в конструкторе).
                Loaded += async (_, _) => await LoadGithubNotesAsync(notesPanel);
            }
            var card = new Border
            {
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(4),
                // Issue #116 п.1 (v1.0.1.50): единственный скролл-контейнер карточки —
                // один ScrollViewer с явной вертикальной прокруткой (Auto), иначе
                // поведение колеса мыши зависело от дефолтов контейнеров.
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = notesPanel
                }
            };
            ThemeBrushes.Bind(card, Border.BackgroundProperty, "CardBackgroundColorBrush");
            ThemeBrushes.Bind(card, Border.BorderBrushProperty, "BorderBrush");
            AvaloniaUi.Place(root, card, 1);

            // Кнопки: галка «показывать при запуске», CHANGELOG на GitHub, закрыть.
            _showOnStartCheck = new CheckBox
            {
                Content = LocalizationManager.T("WhatsNew.ShowOnStart"),
                IsChecked = settings.ShowWhatsNewOnStart,
                VerticalAlignment = VerticalAlignment.Center
            };
            var changelog = AvaloniaUi.Button("WhatsNew.OpenChangelog", (_, _) => OpenChangelog(), 220);
            var close = AvaloniaUi.Button("Common.Close", (_, _) => Close(), 110);
            close.IsCancel = true;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(changelog);
            buttons.Children.Add(close);

            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            AvaloniaUi.Place(bottom, _showOnStartCheck, 0, 0);
            AvaloniaUi.Place(bottom, buttons, 0, 1);
            AvaloniaUi.Place(root, bottom, 2);

            Content = root;
        }

        /// <summary>Применяет галку «Показывать при запуске» к настройкам (сохраняет вызывающий).</summary>
        public void ApplyShowOnStart() =>
            _settings.ShowWhatsNewOnStart = _showOnStartCheck.IsChecked == true;

        private async Task LoadGithubNotesAsync(StackPanel panel)
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
                    var info = AvaloniaUi.Text(LocalizationManager.T("WhatsNew.NotesUnavailable"), 13);
                    info.Opacity = 0.7;
                    panel.Children.Add(info);
                    return;
                }
                panel.Children.Clear();
                panel.Children.Add(MarkdownRenderer.ToStackPanel(body));
            }
            catch
            {
                // Сбой загрузки сводки не роняет диалог: пробуем локальный CHANGELOG,
                // иначе показываем заглушку (окно никогда не остаётся пустым —
                // issue #116 v1.0.1.54).
                panel.Children.Clear();
                var changelog = WhatsNewService.TryGetChangelogSection(WhatsNewService.CurrentVersion);
                if (!string.IsNullOrWhiteSpace(changelog))
                {
                    panel.Children.Add(MarkdownRenderer.ToStackPanel(changelog));
                }
                else
                {
                    var info = AvaloniaUi.Text(LocalizationManager.T("WhatsNew.NotesUnavailable"), 13);
                    info.Opacity = 0.7;
                    panel.Children.Add(info);
                }
            }
        }

        private static void OpenChangelog()
        {
            try
            {
                // Linux: открытие ссылки — xdg-open (UseShellExecute не поддерживается).
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    UseShellExecute = false,
                    ArgumentList = { WhatsNewService.ChangelogUrl }
                });
            }
            catch
            {
                // Открытие браузера — вспомогательное действие: сбой не роняет диалог.
            }
        }
    }
}
#endif
