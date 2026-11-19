#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Типовые конфигурации» (issue #321): список предопределённых (только для чтения)
    /// и пользовательских конфигураций 1С. Добавление/правка выполняются в отдельном модальном
    /// окне <see cref="ConfigTypeEditWindow"/> — «Правка» больше не переключает это окно «два в
    /// одном», а «Закрыть» закрывает только список. Изменения сохраняются сразу в файл
    /// <c>custom_config_types.json</c> через <see cref="ICustomConfigTypesStore"/> и переживают
    /// перезапуск. Предопределённые конфигурации из <see cref="BuiltInConfigTypes"/> нельзя
    /// изменять или удалять (общие статические экземпляры — их мутация «расползалась» по другим
    /// окнам: актуальные релизы, проверка обновлений, связь с конфигурацией).
    /// Avalonia/Linux-версия WPF-окна <see cref="ConfigTypesEditWindow"/>.
    /// </summary>
    public sealed class ConfigTypesEditWindow : ModalWindowBase
    {
        private readonly Services.ICustomConfigTypesStore _store = AppServices.GetRequiredService<Services.ICustomConfigTypesStore>();
        private readonly Services.IItsAccountsStore _itsAccounts = AppServices.GetRequiredService<Services.IItsAccountsStore>();
        private readonly Services.IAppLogger _logger = AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private readonly List<OneCConfigType> _customTypes = new();
        // Полный набор строк (из LoadAll) и видимый набор после поискового фильтра
        // (issue #321, часть 2): фильтрация по копии полного списка, а не MergeAll на каждый ввод.
        private readonly List<ConfigTypeItemViewModel> _allRows = new();
        private readonly List<ConfigTypeItemViewModel> _rows = new();
        private TextBox? _searchBox;

        // Сортировка списка (issue #12): по умолчанию — по наименованию по возрастанию;
        // заголовки колонок кликабельные, текущая колонка помечается ▲/▼.
        private readonly ConfigTypesSorter _sorter = new();
        private readonly List<(TextBlock Block, Avalonia.Controls.Shapes.Path Arrow, string Title, ConfigTypesSortColumn Column)> _sortHeaders = new();

        // Панель строк списка (пересобирается после каждого изменения).
        private readonly StackPanel _rowsPanel = new();
        private readonly DockPanel _listView = new() { LastChildFill = true };

        /// <summary>
        /// Открывает окно редактирования списка типовых конфигураций.
        /// </summary>
        public ConfigTypesEditWindow()
        {
            Title = LocalizationManager.T("Updates.ConfigTypesTitle");
            Width = 960;
            Height = 560;
            MinWidth = 760;
            MinHeight = 440;
            FontSize = 13;
            CanResize = true;

            LoadCustomTypes();
            RebuildRows();
            Content = BuildRoot();
        }

        /// <summary>
        /// Показывает окно модально (синхронно). Открытая публичная обёртка над
        /// <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>, чтобы диалог можно было
        /// вызывать из ViewModel (не наследника окна).
        /// </summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private void LoadCustomTypes()
        {
            try
            {
                _customTypes.Clear();
                foreach (var ct in _store.Load())
                    _customTypes.Add(ct);
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка загрузки списка пользовательских типовых конфигураций", ex);
            }
        }

        /// <summary>Формирует строки таблицы единым правилом
        /// <see cref="Services.CustomConfigTypesStore.MergeAll"/>: пользовательская копия
        /// предопределённой заменяет встроенную с тем же кодом — дублей строк после правки
        /// встроенной не возникает (issue #321); обычные пользовательские записи добавляются
        /// следом.</summary>
        private void RebuildRows()
        {
            _allRows.Clear();
            foreach (var ct in Services.CustomConfigTypesStore.MergeAll(BuiltInConfigTypes.All, _customTypes))
                _allRows.Add(new ConfigTypeItemViewModel(ct, OnEditRow, OnDeleteRow, _itsAccounts));
            ApplyFilter();
        }

        /// <summary>Применяет поисковый фильтр к полному списку строк (пустой запрос — весь список).</summary>
        private void ApplyFilter()
        {
            var query = _searchBox?.Text; // до BuildRoot поле поиска ещё не создано
            _rows.Clear();
            _rowsPanel.Children.Clear();
            foreach (var row in _sorter.Apply(_allRows.Where(r => ViewModels.ConfigTypesFilter.Matches(r, query))))
                AddRow(row);
        }

        /// <summary>Щелчок по заголовку колонки (issue #12): смена колонки/направления и пересборка.</summary>
        private void OnSortHeaderClick(ConfigTypesSortColumn column)
        {
            _sorter.Toggle(column);
            UpdateSortHeaders();
            ApplyFilter();
        }

        /// <summary>Подписи заголовков с индикатором направления у текущей колонки (issue #12).</summary>
        private void UpdateSortHeaders()
        {
            foreach (var (block, arrow, title, column) in _sortHeaders)
            {
                block.Text = title;
                var indicator = _sorter.IndicatorFor(column);
                arrow.Data = indicator.Length == 0 ? null : IconHelper.Geometry(indicator);
            }
        }

        private void AddRow(ConfigTypeItemViewModel row)
        {
            _rows.Add(row);
            var index = _rowsPanel.Children.Count;
            _rowsPanel.Children.Add(BuildRow(row, index));
        }

        /// <summary>Строит визуальную строку таблицы конфигураций.</summary>
        private Grid BuildRow(ConfigTypeItemViewModel row, int index)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2), Focusable = true };

            // Подсветка только нечётных строк (1-я, 3-я, 5-я…): индекс строки начинается с 0,
            // поэтому нечётной позиции соответствует чётный индекс. Hover подсвечивает текущую строку.
            var oddRow = index % 2 == 0;
            // «Зебра» строк берёт тематическую кисть ZebraRowBrush (issue #26):
            // в тёмной теме это приглушённая #334155, а не светлая/белая полоса.
            var bandBrush = oddRow ? (TryBrush("ZebraRowBrush") ?? TryBrush("ItemHoverBrush") ?? Brushes.Transparent) : Brushes.Transparent;
            var hoverBrush = TryBrush("ItemSelectedBrush") ?? Brushes.Transparent;
            grid.Background = bandBrush;
            grid.PointerEntered += (_, _) => grid.Background = hoverBrush;
            grid.PointerExited += (_, _) => grid.Background = bandBrush;

            // DEL на сфокусированной пользовательской строке удаляет её (issue #321).
            if (!row.IsBuiltIn)
                grid.KeyDown += (_, e) =>
                {
                    if (e.Key == Avalonia.Input.Key.Delete)
                    {
                        e.Handled = true;
                        OnDeleteRow(row);
                    }
                };

            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var name = new TextBlock
            {
                Text = row.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (row.IsBuiltIn)
                Themes.ThemeBrushes.Bind(name, TextBlock.ForegroundProperty, "TextSecondaryBrush");

            // Пользовательская копия предопределённой (правка встроенной строки, issue #321)
            // помечается значком ✎★ рядом с именем.
            var namePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 0, 8, 0)
            };
            namePanel.Children.Add(name);
            // Пользовательская копия предопределённой (правка встроенной строки, issue #321):
            // векторные значки «правка» + «звезда» + серая подпись «заменена пользовательской».
            if (row.IsOverride)
            {
                // P1b: вместо текстового «✎★» — иконки активного набора.
                var mark = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    Spacing = 2,
                    Children =
                    {
                        IconHelper.MakeIcon("IconEdit", UiMetrics.IconInline, "AccentBrush"),
                        IconHelper.MakeIcon("IconStar", UiMetrics.IconInline, "AccentBrush")
                    }
                };
                ToolTip.SetTip(mark, T("Updates.OverrideHint"));
                namePanel.Children.Add(mark);

                var note = new TextBlock
                {
                    Text = " · " + T("Updates.ReplacedByUser"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                Themes.ThemeBrushes.Bind(note, TextBlock.ForegroundProperty, "TextSecondaryBrush");
                namePanel.Children.Add(note);

                ToolTip.SetTip(namePanel, T("Updates.OverrideRowHint"));
            }
            Grid.SetColumn(namePanel, 0);
            grid.Children.Add(namePanel);

            var urlCode = new TextBlock
            {
                Text = row.ConfigName,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            // Колонка «Имя конфигурации» (issue #321): имя в метаданных 1С — для
            // сопоставления данных о конфигурации, в адрес обновлений не попадает.
            ToolTip.SetTip(urlCode, T("Updates.ConfigNameHint"));
            Grid.SetColumn(urlCode, 1);
            grid.Children.Add(urlCode);

            var nick = new TextBlock
            {
                Text = row.Nick,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(nick, 2);
            grid.Children.Add(nick);

            var editions = new TextBlock
            {
                Text = row.EditionsSummary,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Grid.SetColumn(editions, 3);
            grid.Children.Add(editions);

            // Учётная запись ИТС для конфигурации (issue #333): имя выбранной записи
            // справочника либо «Основная», если запись не указана.
            var account = new TextBlock
            {
                Text = row.AccountDisplay,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            // Пояснение колонки учётной записи ИТС (issue #321, часть 2).
            ToolTip.SetTip(account, T("ItsAccounts.AccountColumnHint"));
            Grid.SetColumn(account, 4);
            grid.Children.Add(account);

            // Кнопка «Изменить» доступна для всех строк (issue #321): правка предопределённой
            // создаёт пользовательскую копию-переопределение. «Удалить» — только у пользовательских.
            var edit = new Button
            {
                Content = T("Updates.Edit"),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 4),
                Margin = new Thickness(4, 0, 4, 0)
            };
            edit.Styled(ControlThemes.SelectAllButton);
            edit.Click += (_, _) => OnEditRow(row);
            Grid.SetColumn(edit, 5);
            grid.Children.Add(edit);

            if (!row.IsBuiltIn)
            {
                var delete = new Button
                {
                    Content = T("Updates.Delete"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(10, 4),
                    Margin = new Thickness(4, 0, 4, 0)
                };
                delete.Styled(ControlThemes.SelectAllButton);
                delete.Click += (_, _) => OnDeleteRow(row);
                Grid.SetColumn(delete, 6);
                grid.Children.Add(delete);
            }

            return grid;
        }

        /// <summary>
        /// Открывает отдельное окно правки конфигурации. Правка предопределённой строки создаёт
        /// пользовательскую копию-переопределение с тем же кодом (статические экземпляры
        /// <see cref="BuiltInConfigTypes"/> не мутируются — issue #321); обычная пользовательская
        /// строка правится на месте.
        /// </summary>
        private void OnEditRow(ConfigTypeItemViewModel row)
        {
            var edit = new ConfigTypeEditWindow(CloneType(row.Model));
            if (!edit.ShowSync(this) || edit.Result is not { } updated)
                return; // Отмена — модель не изменялась (правка велась на копии).

            if (row.IsBuiltIn)
            {
                // Правка встроенной строки: сохраняем как пользовательскую копию (тот же код),
                // которая в общем списке LoadAll() заменяет предопределённую.
                updated.IsBuiltIn = false;
                updated.OverridesBuiltIn = true;
                _customTypes.RemoveAll(c => SameCode(c.Code, updated.Code) && c.OverridesBuiltIn);
                _customTypes.Add(updated);
            }
            else
            {
                ApplyTo(row.Model, updated);
            }

            Save();
            RebuildRows();
        }

        /// <summary>Открывает отдельное окно создания новой пользовательской конфигурации.</summary>
        private void OnAddConfigClick()
        {
            var edit = new ConfigTypeEditWindow();
            if (!edit.ShowSync(this) || edit.Result is not { } created)
                return;

            created.IsBuiltIn = false;
            created.IsTracked = true;
            created.OverridesBuiltIn = false;

            // Уникальность по составному ключу «наименование + редакции» (issue #321): несколько
            // записей одной конфигурации допустимы (ЗУП 3.0 и 3.1), точные дубли — нет.
            if (HasDuplicate(created))
            {
                _dialogs.ShowWarning(T("Updates.ConfigExists"), T("Updates.ConfigTypesTitle"));
                return;
            }

            _customTypes.Add(created);
            Save();
            RebuildRows();
        }

        /// <summary>
        /// «Восстановить типовые» (issue #321): удаляет пользовательские копии предопределённых
        /// конфигураций (<see cref="OneCConfigType.OverridesBuiltIn"/>) — предопределённый набор
        /// возвращается к <see cref="BuiltInConfigTypes.All"/>, пользовательские записи не трогаются.
        /// </summary>
        private void OnRestoreDefaultsClick()
        {
            if (_customTypes.Count == 0 || !_customTypes.Any(c => c.OverridesBuiltIn))
            {
                _dialogs.ShowInfo(T("Updates.NothingToRestore"), T("Updates.ConfigTypesTitle"));
                return;
            }

            if (!_dialogs.Confirm(T("Updates.RestoreDefaultsConfirm"), T("Updates.ConfigTypesTitle")))
                return;
            try
            {
                _store.RestoreDefaults();
                LoadCustomTypes();
                RebuildRows();
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка восстановления предопределённого набора типовых конфигураций", ex);
            }
        }

        private void OnDeleteRow(ConfigTypeItemViewModel row)
        {
            if (row.IsBuiltIn)
                return; // Предопределённые конфигурации нельзя удалить.

            if (!_dialogs.Confirm(T("Updates.ConfirmDelete"), T("Updates.ConfigTypesTitle")))
                return;
            try
            {
                _customTypes.Remove(row.Model);
                Save();
                RebuildRows();
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка удаления конфигурации «{row.Name}»", ex);
            }
        }

        /// <summary>Сразу сохраняет пользовательские конфигурации в файл custom_config_types.json.</summary>
        private void Save()
        {
            try
            {
                _store.Save(_customTypes);
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка сохранения списка типовых конфигураций", ex);
            }
        }

        /// <summary>Переносит отредактированную копию в модель строки (изменение одной строки не
        /// влияет на другие конфигурации — правка велась на отдельном экземпляре).</summary>
        private static void ApplyTo(OneCConfigType target, OneCConfigType source)
        {
            target.Code = source.Code;
            target.Name = source.Name;
            target.ConfigName = source.ConfigName ?? string.Empty;
            target.UrlCode = source.UrlCode;
            target.Nick = source.Nick;
            target.AccountId = source.AccountId ?? string.Empty;
            target.Editions.Clear();
            target.Editions.AddRange(source.Editions);
        }

        /// <summary>Глубокая копия конфигурации (для правки без мутации исходного экземпляра).</summary>
        private static OneCConfigType CloneType(OneCConfigType source) => new()
        {
            Code = source.Code,
            Name = source.Name,
            ConfigName = source.ConfigName ?? string.Empty,
            UrlCode = source.UrlCode,
            Nick = source.Nick,
            AccountId = source.AccountId ?? string.Empty,
            IsBuiltIn = source.IsBuiltIn,
            IsTracked = source.IsTracked,
            OverridesBuiltIn = source.OverridesBuiltIn,
            Editions = source.Editions.Select(e => new OneCConfigEdition
            {
                Name = e.Name,
                Red = e.Red,
                SubRed = e.SubRed,
                UrlOverride = e.UrlOverride,
                IsDefault = e.IsDefault,
            }).ToList(),
        };

        /// <summary>Сравнивает коды конфигураций без учёта регистра.</summary>
        private static bool SameCode(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
            string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Проверка дубля по составному ключу «наименование + редакции» (issue #321): дублем
        /// считается запись с тем же наименованием и тем же набором редакций (в любом порядке).
        /// Разные редакции одной конфигурации (ЗУП 3.0 и 3.1) дублями не считаются.
        /// </summary>
        private bool HasDuplicate(OneCConfigType candidate)
        {
            foreach (var existing in BuiltInConfigTypes.All.Concat(_customTypes))
            {
                if (string.Equals(existing.Name.Trim(), candidate.Name.Trim(), StringComparison.OrdinalIgnoreCase)
                    && SameEditions(existing.Editions, candidate.Editions))
                    return true;
            }
            return false;
        }

        private static bool SameEditions(IReadOnlyCollection<OneCConfigEdition> a, IReadOnlyCollection<OneCConfigEdition> b)
        {
            if (a.Count != b.Count)
                return false;
            var namesA = a.Select(e => e.Name.Trim()).Where(n => n.Length > 0)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            var namesB = b.Select(e => e.Name.Trim()).Where(n => n.Length > 0)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            return namesA.SequenceEqual(namesB, StringComparer.OrdinalIgnoreCase);
        }

        private Control BuildRoot()
        {
            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var title = new TextBlock
            {
                Text = T("Updates.ConfigTypesTitle"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Пояснения (issue #321): что это за окно, что нельзя менять предопределённые,
            // где хранятся пользовательские конфигурации.
            var explainPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            var explain = new TextBlock
            {
                Text = T("Updates.ConfigTypesExplanation"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Themes.ThemeBrushes.Bind(explain, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            explainPanel.Children.Add(explain);

            var storageHint = new TextBlock
            {
                Text = T("Updates.CustomStorageHint"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            Themes.ThemeBrushes.Bind(storageHint, TextBlock.ForegroundProperty, "TextSecondaryColorBrush");
            explainPanel.Children.Add(storageHint);
            Grid.SetRow(explainPanel, 1);
            grid.Children.Add(explainPanel);

            // Карточка списка конфигураций.
            var listBorder = new Border
            {
                Margin = new Thickness(0, 12, 0, 0),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
            Themes.ThemeBrushes.Bind(listBorder, Border.BackgroundProperty, "CardBackgroundColorBrush");
            Themes.ThemeBrushes.Bind(listBorder, Border.BorderBrushProperty, "BorderColorBrush");

            _rowsPanel.Margin = new Thickness(4, 2);
            var header = BuildHeaderGrid();

            var scroll = new ScrollViewer
            {
                Content = _rowsPanel,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(4)
            };

            _listView.Children.Clear();
            var addButton = new Button { Content = T("Updates.AddConfig"), Height = 32 };
            addButton.Styled(ControlThemes.ModernButton);
            addButton.Click += (_, _) => OnAddConfigClick();

            // «Восстановить типовые» (issue #321): убирает пользовательские копии встроенных
            // конфигураций, обычные пользовательские записи не трогает.
            var restoreButton = new Button { Content = T("Updates.RestoreDefaults"), Height = 32 };
            restoreButton.Styled(ControlThemes.SelectAllButton);
            restoreButton.Click += (_, _) => OnRestoreDefaultsClick();

            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(8, 8, 8, 4)
            };
            // Поиск/отбор списка (issue #321, часть 2): фильтр по мере ввода,
            // очистка кнопкой или Esc в поле поиска.
            _searchBox = new TextBox
            {
                Watermark = T("Updates.SearchPlaceholder"),
                MinHeight = 32,
                MinWidth = 260
            };
            _searchBox.Styled(ControlThemes.ModernTextBox);
            _searchBox.TextChanged += (_, _) => ApplyFilter();
            _searchBox.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Escape && !string.IsNullOrEmpty(_searchBox.Text))
                {
                    e.Handled = true;
                    _searchBox.Text = string.Empty;
                }
            };
            var clearSearch = new Button { Content = T("Updates.ClearSearch"), Height = 32 };
            clearSearch.Styled(ControlThemes.SelectAllButton);
            clearSearch.Click += (_, _) =>
            {
                _searchBox!.Text = string.Empty;
                _searchBox.Focus();
            };
            toolbar.Children.Add(_searchBox);
            toolbar.Children.Add(clearSearch);
            toolbar.Children.Add(addButton);
            toolbar.Children.Add(restoreButton);

            var toolbarBorder = new Border
            {
                Padding = new Thickness(8, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = Brushes.Transparent,
                Child = toolbar
            };
            Themes.ThemeBrushes.Bind(toolbarBorder, Border.BorderBrushProperty, "BorderColorBrush");
            DockPanel.SetDock(toolbarBorder, Dock.Top);
            _listView.Children.Add(toolbarBorder);

            DockPanel.SetDock(header, Dock.Top);
            _listView.Children.Add(header);
            _listView.Children.Add(scroll);
            listBorder.Child = _listView;

            Grid.SetRow(listBorder, 2);
            grid.Children.Add(listBorder);

            // Нижняя панель: «Добавить» + «Восстановить типовые» / «Закрыть».
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var leftButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var addBottom = new Button { Content = T("Updates.AddConfig"), Height = 34 };
            addBottom.Styled(ControlThemes.ModernButton);
            addBottom.Click += (_, _) => OnAddConfigClick();
            leftButtons.Children.Add(addBottom);

            var restoreBottom = new Button { Content = T("Updates.RestoreDefaults"), Height = 34 };
            restoreBottom.Styled(ControlThemes.SelectAllButton);
            restoreBottom.Click += (_, _) => OnRestoreDefaultsClick();
            leftButtons.Children.Add(restoreBottom);

            Grid.SetColumn(leftButtons, 0);
            bottom.Children.Add(leftButtons);

            var close = BuildCancelActionButton(140);
            close.Click += (_, _) => Close();
            Grid.SetColumn(close, 1);
            bottom.Children.Add(close);

            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            return grid;
        }

        /// <summary>Заголовок таблицы конфигураций.</summary>
        private Grid BuildHeaderGrid()
        {
            var grid = new Grid { Margin = new Thickness(8, 0, 8, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));

            _sortHeaders.Clear();
            grid.Children.Add(MakeSortHeader(T("Updates.Name"), 0, ConfigTypesSortColumn.Name));
            grid.Children.Add(MakeSortHeader(T("Updates.ConfigName"), 1, ConfigTypesSortColumn.ConfigName));
            grid.Children.Add(MakeSortHeader(T("Updates.Nick"), 2, ConfigTypesSortColumn.Nick));
            grid.Children.Add(MakeSortHeader(T("Updates.Editions"), 3, ConfigTypesSortColumn.Editions));
            grid.Children.Add(MakeSortHeader(T("ItsAccounts.AccountLabel"), 4, ConfigTypesSortColumn.Account));
            UpdateSortHeaders();
            return grid;
        }

        private static IBrush? TryBrush(string key)
        {
            if (Application.Current is not { } app || !app.TryFindResource(key, out var found))
                return null;
            return found as IBrush;
        }

        /// <summary>
        /// Кликабельный заголовок колонки (issue #12): прозрачная кнопка с подписью; щелчок
        /// сортирует по колонке, повторный — меняет направление.
        /// </summary>
        private Control MakeSortHeader(string title, int gridColumn, ConfigTypesSortColumn column)
        {
            var block = MakeHeaderText(title, 0);
            block.Margin = new Thickness(0);
            // P1b: индикатор направления — векторная стрелка, а не текстовые «▲/▼».
            var arrow = new Avalonia.Controls.Shapes.Path
            {
                Width = UiMetrics.IconInline,
                Height = UiMetrics.IconInline,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Themes.ThemeBrushes.Bind(arrow, Avalonia.Controls.Shapes.Shape.FillProperty, "TextSecondaryBrush");
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { block, arrow }
            };
            var button = new Button
            {
                Content = headerPanel,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            ToolTip.SetTip(button, T("Updates.SortByColumn"));
            button.Click += (_, _) => OnSortHeaderClick(column);
            Grid.SetColumn(button, gridColumn);
            _sortHeaders.Add((block, arrow, title, column));
            return button;
        }

        private static TextBlock MakeHeaderText(string text, int column)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            };
            Themes.ThemeBrushes.Bind(block, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            Grid.SetColumn(block, column);
            return block;
        }
    }
}
#endif