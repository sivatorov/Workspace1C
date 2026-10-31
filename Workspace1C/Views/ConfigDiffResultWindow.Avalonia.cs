#if LINUX
using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно отчёта о сравнении конфигураций (0.3.9.99, функция №9, Avalonia/Linux):
    /// шапка «что с чем сравнивали», сводка, группировка объектов по типам метаданных
    /// со статусами (добавлен/изменён/удалён/без изменений) и экспорт в CSV/TXT.
    /// Вся логика — в чистой ViewModel <see cref="ConfigDiffResultViewModel"/>.
    /// </summary>
    public sealed class ConfigDiffResultWindow : ModalWindowBase
    {
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
        private readonly ConfigDiffResultViewModel _vm;

        /// <summary>
        /// Левая база для кнопки «Обозреватель метаданных…» (этап 5 цикла 0.3.9.132–0.3.9.136):
        /// режим «База ↔ .cf» — передаётся из <see cref="ConfigDiffSetupWindow"/>, для
        /// CfVsCf — null (кнопка не создаётся). Параметр опционален — вызовы не ломаются.
        /// </summary>
        private readonly Infobase? _baseForExplorer;

        public ConfigDiffResultWindow(ConfigurationDiffResult result, Infobase? baseForExplorer = null)
        {
            _baseForExplorer = baseForExplorer;

            Title = LocalizationManager.T("ConfigDiff.Title");
            Width = 900;
            Height = 640;
            MinWidth = 700;
            MinHeight = 420;
            CanResize = true;
            FontSize = 13;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;

            _vm = new ConfigDiffResultViewModel(result);

            var header = new TextBlock
            {
                Text = _vm.HeaderText,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ThemeBrushes.Bind(header, TextBlock.ForegroundProperty, "TextPrimaryBrush");

            var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var text in new[] { _vm.ElapsedText, "\u00b7", _vm.RootChangedText })
            {
                var tb = new TextBlock { Text = text, FontSize = 12 };
                ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, "TextSecondaryBrush");
                meta.Children.Add(tb);
            }

            var summary = new TextBlock
            {
                Text = _vm.SummaryText,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            ThemeBrushes.Bind(summary, TextBlock.ForegroundProperty, "TextPrimaryBrush");

            // Группировка по типам: заголовок типа и строки объектов.
            var list = new StackPanel { Spacing = 2 };
            foreach (var typeNode in _vm.Types)
            {
                var typeHeader = new TextBlock
                {
                    Text = typeNode.Header,
                    FontSize = 13,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 8, 0, 4)
                };
                ThemeBrushes.Bind(typeHeader, TextBlock.ForegroundProperty, "TextPrimaryBrush");
                list.Children.Add(typeHeader);
                foreach (var obj in typeNode.Objects)
                    list.Children.Add(BuildObjectRow(obj));
            }

            var scroll = new ScrollViewer
            {
                Content = list,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 12, 0, 0)
            };
            // Этап 5 (0.3.9.136): «Обозреватель метаданных…» доступен только при наличии
            // левой базы (режим «База ↔ .cf») — открывает обозреватель с предвыбором базы.
            if (_baseForExplorer is not null)
            {
                var openExplorer = new Button { Content = LocalizationManager.T("MetadataExplorer.OpenFromDiff"), Width = 200 };
                openExplorer.Click += (_, _) => OpenMetadataExplorer();
                buttons.Children.Add(openExplorer);
            }
            var exportCsv = new Button { Content = LocalizationManager.T("ConfigDiff.ExportCsv"), Width = 140 };
            exportCsv.Click += (_, _) => ExportCsv();
            var exportTxt = new Button { Content = LocalizationManager.T("ConfigDiff.ExportTxt"), Width = 140 };
            exportTxt.Click += (_, _) => ExportTxt();
            var close = new Button { Content = LocalizationManager.T("ConfigDiff.Close"), Width = 100, IsCancel = true };
            close.Click += (_, _) => Close();
            buttons.Children.Add(exportCsv);
            buttons.Children.Add(exportTxt);
            buttons.Children.Add(close);

            var grid = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto)
                }
            };
            Grid.SetRow(header, 0);
            grid.Children.Add(header);
            Grid.SetRow(meta, 1);
            grid.Children.Add(meta);
            Grid.SetRow(summary, 2);
            grid.Children.Add(summary);
            Grid.SetRow(scroll, 3);
            grid.Children.Add(scroll);
            Grid.SetRow(buttons, 4);
            grid.Children.Add(buttons);
            Content = grid;
        }

        /// <summary>Строка объекта: Имя | Статус | Файлов | Размер.</summary>
        private static Control BuildObjectRow(ConfigDiffObjectItemViewModel obj)
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
                    new ColumnDefinition(new GridLength(110)),
                    new ColumnDefinition(new GridLength(70)),
                    new ColumnDefinition(new GridLength(90))
                },
                Margin = new Thickness(2, 1)
            };

            var name = new TextBlock { Text = obj.Name, TextTrimming = TextTrimming.CharacterEllipsis };
            ThemeBrushes.Bind(name, TextBlock.ForegroundProperty, "TextPrimaryBrush");
            grid.Children.Add(name);

            var status = new TextBlock
            {
                Text = obj.StatusText,
                TextAlignment = TextAlignment.Right,
                Foreground = StatusBrush(obj.Kind)
            };
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);

            var files = new TextBlock { Text = obj.FileCount.ToString(), TextAlignment = TextAlignment.Right };
            ThemeBrushes.Bind(files, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(files, 2);
            grid.Children.Add(files);

            var size = new TextBlock { Text = obj.SizeText, TextAlignment = TextAlignment.Right };
            ThemeBrushes.Bind(size, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(size, 3);
            grid.Children.Add(size);

            return grid;
        }

        // Тёмные оттенки Tailwind-600 читаются и на светлой, и на тёмной схеме:
        // прежние (#22C55E/#F59E0B/#EF4444/#94A3B8) на светлом фоне сливались
        // с подложкой (issue #316). Значения синхронны с WPF StatusTextStyle.
        private static IBrush StatusBrush(DiffChangeKind kind) => kind switch
        {
            // Статусы — на семантических кистях темы (аудит UI, п. 2.9).
            DiffChangeKind.Added => ThemeBrush("SuccessBrush", "#16A34A"),
            DiffChangeKind.Changed => ThemeBrush("WarningBrush", "#D97706"),
            DiffChangeKind.Removed => ThemeBrush("ErrorBrush", "#DC2626"),
            _ => ThemeBrush("TextSecondaryBrush", "#64748B")
        };

        /// <summary>Читает кисть темы по ключу; при отсутствии — запасной HEX (аудит UI, п. 2.9).</summary>
        private static IBrush ThemeBrush(string key, string fallbackHex)
            => Avalonia.Application.Current?.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var value) == true
                && value is IBrush brush
                    ? brush
                    : new SolidColorBrush(Color.Parse(fallbackHex));

        /// <summary>
        /// «Обозреватель метаданных…» (этап 5 цикла 0.3.9.132–0.3.9.136): открывает
        /// <see cref="MetadataExplorerWindow"/> с предвыбранной левой базой (режим
        /// «База ↔ .cf») и сразу запускает выгрузку (<c>LoadCommand.Execute(null)</c>).
        /// </summary>
        private void OpenMetadataExplorer()
        {
            try
            {
                var bases = AppServices.GetRequiredService<IInfobaseRepository>().Load();
                var window = new MetadataExplorerWindow(bases, _baseForExplorer);
                // Сразу загрузка предвыбранной базы: результат — дерево обозревателя.
                window.LoadCommand.Execute(null);
                window.ShowDialogSync(this);
            }
            catch (Exception ex)
            {
                _dialogs.ShowError(ex.Message, LocalizationManager.T("MetadataExplorer.Title"));
            }
        }

        private void ExportCsv()
        {
            var path = _dialogs.SaveFileDialog(
                LocalizationManager.T("ConfigDiff.ExportCsv"),
                $"ConfigDiff_{DateTime.Now:yyyy-MM-dd}.csv",
                LocalizationManager.T("ConfigDiff.CsvFileFilter"));
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                CsvExporter.WriteFile(path, _vm.BuildCsvRows());
            }
            catch (Exception ex)
            {
                _dialogs.ShowError(
                    string.Format(LocalizationManager.T("ConfigDiff.ExportFailedFormat"), ex.Message),
                    LocalizationManager.T("ConfigDiff.Title"));
            }
        }

        private void ExportTxt()
        {
            var path = _dialogs.SaveFileDialog(
                LocalizationManager.T("ConfigDiff.ExportTxt"),
                $"ConfigDiff_{DateTime.Now:yyyy-MM-dd}.txt",
                LocalizationManager.T("ConfigDiff.TxtFileFilter"));
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                File.WriteAllText(path, _vm.BuildTextReport(), System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _dialogs.ShowError(
                    string.Format(LocalizationManager.T("ConfigDiff.ExportFailedFormat"), ex.Message),
                    LocalizationManager.T("ConfigDiff.Title"));
            }
        }
    }
}
#endif