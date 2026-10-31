#if LINUX
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
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
    /// Окно настройки связи ИБ ↔ типовая конфигурация 1С: выбор типовой конфигурации и редакции,
    /// формирование адреса каталога релизов (автоматически по правилу 1С либо вручную) и кнопка
    /// «Определить версию», которая читает имя и версию конфигурации базы через
    /// <see cref="ConfigurationInfoService.ReadAndApply"/> и автоматически сопоставляет базу с
    /// типовой конфигурацией по имени (issue #322). При сохранении записывает
    /// <see cref="Infobase.UpdateConfigCode"/>, <see cref="Infobase.UpdateUrlOverride"/> и
    /// персональный сегмент (ник) адреса <see cref="Infobase.UpdateUrlSegment"/>.
    /// Avalonia/Linux-версия WPF-окна <see cref="ConfigUpdateLinkWindow"/>.
    /// </summary>
    public sealed class ConfigUpdateLinkWindow : ModalWindowBase
    {
        private readonly Services.IOneCUpdatesService _updates = AppServices.GetRequiredService<Services.IOneCUpdatesService>();
        private readonly Services.IInfobaseRepository _repository = AppServices.GetRequiredService<Services.IInfobaseRepository>();
        private readonly Services.ICustomConfigTypesStore _store = AppServices.GetRequiredService<Services.ICustomConfigTypesStore>();
        private readonly Services.IAppLogger _logger = AppServices.GetRequiredService<Services.IAppLogger>();
        private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();

        private readonly Infobase _infobase;
        private readonly List<OneCConfigType> _configs = new();

        /// <summary>Строки выпадающего списка «Конфигурация» (обёртки с пометкой происхождения,
        /// issue #322) — рабочий список остаётся в <see cref="_configs"/>.</summary>
        private readonly List<ConfigLinkItemViewModel> _configItems = new();

        private bool _initializing = true;
        private bool _definingVersion;

        private readonly TextBlock _baseNameText = new();
        private ComboBox _configCombo = new();
        private ComboBox _editionCombo = new();
        private RadioButton _autoUrlRadio = new();
        private RadioButton _manualUrlRadio = new();
        private TextBox _urlBox = new();
        private TextBox _segmentBox = new();
        private Button _defineVersionButton = new();
        private readonly TextBlock _currentConfigText = new();
        private readonly TextBlock _resultText = new();

        /// <param name="infobase">Информационная база, для которой настраивается связь с конфигурацией.</param>
        public ConfigUpdateLinkWindow(Infobase infobase)
        {
            _infobase = infobase ?? throw new ArgumentNullException(nameof(infobase));

            Title = LocalizationManager.T("Updates.EditConfig");
            Width = 700;
            Height = 640;
            MinWidth = 580;
            MinHeight = 560;
            FontSize = 13;
            CanResize = true;

            _baseNameText.Text = _infobase.Name;
            Themes.ThemeBrushes.Bind(_baseNameText, TextBlock.ForegroundProperty, "AccentBrush");

            // Сначала строим дерево UI — только после этого можно устанавливать значения
            // элементов (в Avalonia элементы создаются в BuildRoot и иначе терялись бы
            // значения, заданные до него: выбор конфигурации, режим URL, ручная ссылка).
            Content = BuildRoot();

            LoadConfigs();
            _configCombo.ItemsSource = _configItems;
            SelectInitialConfig();
            ApplyConfigSelection(rebuildUrl: false);

            // Режим URL: ручная ссылка, если она была задана, иначе автоформирование.
            var hasOverride = !string.IsNullOrWhiteSpace(_infobase.UpdateUrlOverride);
            if (hasOverride)
            {
                _manualUrlRadio.IsChecked = true;
                _urlBox.IsReadOnly = false;
                _urlBox.Text = _infobase.UpdateUrlOverride;
            }
            else
            {
                _autoUrlRadio.IsChecked = true;
                _urlBox.IsReadOnly = true;
            }

            _initializing = false;
            RebuildUrl();

            ShowCurrentConfigSummary();

            // Автоопределение при открытии (issue #322): если у базы уже определены свойства
            // конфигурации (вкладка «Платформа» / «Определить версию»), подставляем совпавшую
            // типовую конфигурацию сразу — связь строится из свойств, ручной выбор — явный override.
            if (!string.IsNullOrWhiteSpace(_infobase.ConfigurationName))
                TryAutoMatchConfig(_infobase.ConfigurationName, _infobase.ConfigurationVersion);
        }

        /// <summary>
        /// Показывает окно модально (синхронно). Открытая публичная обёртка над
        /// <see cref="ModalWindowBase.ShowDialogSync(Window?)"/>, чтобы диалог можно было
        /// вызывать из ViewModel (не наследника окна).
        /// </summary>
        public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

        private static string T(string key) => LocalizationManager.T(key);

        private void LoadConfigs()
        {
            try
            {
                // Общий список типовых = встроенные + пользовательские из файла
                // custom_config_types.json (единый загрузчик — issue #321).
                _configs.Clear();
                _configs.AddRange(_store.LoadAll());
            }
            catch (Exception ex)
            {
                _logger.Error("Ошибка загрузки списка типовых конфигураций для связи ИБ", ex);
                _configs.Clear();
                _configs.AddRange(BuiltInConfigTypes.All);
            }
    
            _configItems.Clear();
            _configItems.AddRange(_configs.Select(c => new ConfigLinkItemViewModel(c)));
        }

        private void SelectInitialConfig()
        {
            var code = _infobase.UpdateConfigCode;
            ConfigLinkItemViewModel? selected = null;
            if (!string.IsNullOrWhiteSpace(code))
                selected = _configItems.FirstOrDefault(i =>
                    string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));

            _configCombo.SelectedItem = selected ?? (_configItems.Count > 0 ? _configItems[0] : null);
        }

        /// <summary>Выбранная в списке типовая конфигурация (через обёртку строки списка).</summary>
        private OneCConfigType? SelectedConfig => (_configCombo.SelectedItem as ConfigLinkItemViewModel)?.Model;

        private void OnConfigSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_initializing)
                return;
            ApplyConfigSelection(rebuildUrl: true);
        }

        /// <summary>
        /// Применяет выбранную конфигурацию: заполняет список редакций и поле сегмента адреса.
        /// Вызывается и при смене конфигурации, и при инициализации окна (раньше при старте
        /// редакции не заполнялись, а выбор конфигурации терялся из-за пересоздания элементов
        /// в BuildRoot — «выбор версии не работал» при автоопределении, issue #322).
        /// </summary>
        private void ApplyConfigSelection(bool rebuildUrl)
        {
            var config = SelectedConfig;
            var editions = config?.Editions ?? new List<OneCConfigEdition>();

            var previous = _editionCombo.SelectedItem;
            _editionCombo.ItemsSource = editions;
            if (editions.Count > 0)
            {
                var previousValid = previous is OneCConfigEdition prev && editions.Contains(prev);
                _editionCombo.SelectedItem = previousValid ? previous : (config?.DefaultEdition ?? editions[0]);
            }
            else
            {
                _editionCombo.SelectedItem = null;
            }

            UpdateSegmentControls();

            if (rebuildUrl)
                RebuildUrl();
        }

        private void OnEditionSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_initializing)
                return;
            RebuildUrl();
        }

        private void OnUrlModeChanged(object? sender, RoutedEventArgs e)
        {
            if (_initializing)
                return;

            var manual = _manualUrlRadio.IsChecked == true;
            _urlBox.IsReadOnly = !manual;
            if (!manual)
                RebuildUrl();
        }

        /// <summary>
        /// Показывает в поле сегмента действующее значение для базы: персональный сегмент базы,
        /// либо (если не задан) ник выбранной типовой конфигурации как исходное значение.
        /// </summary>
        private void UpdateSegmentControls()
        {
            var config = SelectedConfig;
            var personal = _infobase.UpdateUrlSegment;
            if (!string.IsNullOrWhiteSpace(personal))
            {
                _segmentBox.Text = personal.Trim();
                return;
            }

            _segmentBox.Text = config?.Nick?.Trim() ?? string.Empty;
        }

        private void OnSegmentChanged(object? sender, TextChangedEventArgs e)
        {
            if (_initializing)
                return;
            RebuildUrl();
        }

        /// <summary>Открывает окно «Типовые конфигурации» (модально поверх текущего), где хранится
        /// ник конфигурации, по умолчанию подставляемый в адрес каталога релизов.</summary>
        private void OnOpenConfigTypesClick()
        {
            var win = new ConfigTypesEditWindow();
            win.ShowSync(this);

            // После правки пользовательских конфигураций перечитываем список и обновляем выбор.
            var code = SelectedConfig?.Code;
            LoadConfigs();
            _configCombo.ItemsSource = _configItems;
            var restored = _configItems.FirstOrDefault(i =>
                string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));
            _configCombo.SelectedItem = restored ?? (_configItems.Count > 0 ? _configItems[0] : null);
            ApplyConfigSelection(rebuildUrl: true);
        }

        /// <summary>Пересчитывает адрес каталога релизов в автоматическом режиме.</summary>
        private void RebuildUrl()
        {
            if (_initializing)
                return;
            if (_manualUrlRadio.IsChecked == true)
                return;

            var config = SelectedConfig;
            var edition = _editionCombo.SelectedItem as OneCConfigEdition;
            var segment = _segmentBox.Text?.Trim() ?? string.Empty;
            _urlBox.Text = _updates.BuildUpdateUrl(config, edition, null, segment);
        }

        private async void OnDefineVersionClick()
        {
            if (_definingVersion)
                return;

            _definingVersion = true;
            _defineVersionButton.IsEnabled = false;
            _resultText.Text = string.Empty;

            try
            {
                var info = await Task.Run(() => ConfigurationInfoService.ReadAndApply(
                    _infobase, overwriteExisting: true, mode: OneCLaunchMode.Configurator));
                ShowCurrentConfigSummary();
                if (info is { Name.Length: > 0 })
                    TryAutoMatchConfig(info.Value.Name, info.Value.Version);
                else
                    _resultText.Text = T("Updates.VersionDefined");
            }
            catch (Exception ex)
            {
                _logger.Error($"Ошибка определения версии конфигурации базы «{_infobase.Name}»", ex);
                _dialogs.ShowError(T("Updates.CheckFailed"), T("Updates.EditConfig"));
            }
            finally
            {
                _definingVersion = false;
                _defineVersionButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Сопоставляет имя конфигурации базы со списком типовых (встроенные + пользовательские) и
        /// при совпадении подставляет конфигурацию и (по версии) редакцию — «прописал в базе — и
        /// связь работает» (issue #322). Если имя не найдено — текущий выбор сохраняется, в результат
        /// выводится понятное сообщение.
        /// </summary>
        private void TryAutoMatchConfig(string configName, string version)
        {
            var result = FindConfigByInfobaseName(configName);
            if (result is null)
            {
                _resultText.Text = string.Format(T("Updates.ConfigNotMatched"), configName);
                return;
            }

            var match = result.Config;
            _configCombo.SelectedItem = _configItems.FirstOrDefault(i => ReferenceEquals(i.Model, match));
            ApplyConfigSelection(rebuildUrl: true);
            TrySelectEditionByVersion(match, version);

            // Поясняем, по какому полю найдена запись (issue #322): точное имя / внутреннее имя
            // конфигурации / сегмент адреса / вхождение имени — чтобы было видно, почему выбрана
            // именно эта запись.
            var reason = result.Kind switch
            {
                ConfigMatchKind.ExactUrlCode => T("Updates.ReasonUrlCode"),
                ConfigMatchKind.ExactConfigName => T("Updates.ReasonConfigName"),
                ConfigMatchKind.NameContainedInBaseName => T("Updates.ReasonNameContains"),
                ConfigMatchKind.BaseNameContainedInConfigName => T("Updates.ReasonBaseContains"),
                _ => T("Updates.ReasonExactName"),
            };
            _resultText.Text = string.Format(T("Updates.ConfigMatchedReason"), match.Name, reason);
        }

        private ConfigMatchResult? FindConfigByInfobaseName(string configName) =>
            ConfigTypeMatcher.FindMatch(_configs, configName);

        /// <summary>Выбирает редакцию по префиксу версии базы («3.0.142.32» → редакция «3.0») —
        /// единая логика в <see cref="ConfigTypeMatcher.FindEditionByVersion"/> (issue #346).</summary>
        private void TrySelectEditionByVersion(OneCConfigType config, string version)
        {
            var edition = ConfigTypeMatcher.FindEditionByVersion(config, version);
            if (edition is not null)
                _editionCombo.SelectedItem = edition;
        }

        private void ShowCurrentConfigSummary()
        {
            var name = string.IsNullOrWhiteSpace(_infobase.ConfigurationName)
                ? "—"
                : _infobase.ConfigurationName;
            var version = string.IsNullOrWhiteSpace(_infobase.ConfigurationVersion)
                ? "—"
                : _infobase.ConfigurationVersion;
            _currentConfigText.Text = $"{name} · {version}";
        }

        private void OnSaveClick()
        {
            var config = SelectedConfig;
            if (config is null)
            {
                _dialogs.ShowWarning(T("Updates.NoConfigSelected"), T("Updates.EditConfig"));
                return;
            }

            _infobase.UpdateConfigCode = config.Code;
            _infobase.UpdateUrlOverride = _manualUrlRadio.IsChecked == true
                ? (_urlBox.Text?.Trim() ?? string.Empty)
                : string.Empty;

            // Персональный сегмент (ник): если он совпадает с ником типовой конфигурации — не
            // закрепляем его за базой (пустое значение означает «наследовать ник конфигурации»).
            var segment = _segmentBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(config.Nick) &&
                string.Equals(segment, config.Nick.Trim(), StringComparison.OrdinalIgnoreCase))
                segment = string.Empty;
            _infobase.UpdateUrlSegment = segment;

            PersistLink();
            DialogResult = true;
            Close();
        }

        private void PersistLink()
        {
            try
            {
                var all = _repository.Load();
                var match = all.FirstOrDefault(x => ReferenceEquals(x, _infobase));
                if (match is null)
                    return;
                match.UpdateConfigCode = _infobase.UpdateConfigCode;
                match.UpdateUrlOverride = _infobase.UpdateUrlOverride;
                match.UpdateUrlSegment = _infobase.UpdateUrlSegment;
                _repository.Save(all);
            }
            catch (Exception ex)
            {
                _logger.Warn("Не удалось сохранить связь ИБ ↔ конфигурация в репозиторий: " + ex.Message);
            }
        }

        private void OnCancelClick()
        {
            DialogResult = false;
            Close();
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
                Text = T("Updates.EditConfig"),
                FontSize = 15,
                FontWeight = FontWeight.SemiBold
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            _baseNameText.FontSize = 13;
            _baseNameText.FontWeight = FontWeight.SemiBold;
            _baseNameText.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(_baseNameText, 1);
            grid.Children.Add(_baseNameText);

            var form = new StackPanel { Margin = new Thickness(0, 4, 0, 0), Spacing = 4 };

            // Типовая конфигурация: строка списка — имя + пометка происхождения (★ типовая /
            // пользовательская / ✎★ пользовательская копия — перекрывает типовую, issue #322).
            form.Children.Add(MakeFieldLabel(T("Updates.Name")));
            _configCombo = new ComboBox
            {
                ItemsSource = _configItems,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 36
            };
            _configCombo.ItemTemplate = new FuncDataTemplate<ConfigLinkItemViewModel>((item, _) =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                var name = new TextBlock { Text = item.Name, VerticalAlignment = VerticalAlignment.Center };
                var badge = new TextBlock
                {
                    Text = item.OriginBadge,
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Themes.ThemeBrushes.Bind(badge, TextBlock.ForegroundProperty, "TextSecondaryBrush");
                row.Children.Add(name);
                row.Children.Add(badge);
                ToolTip.SetTip(row, item.ToolTipText);
                return row;
            });
            _configCombo.SelectionChanged += OnConfigSelectionChanged;
            form.Children.Add(_configCombo);

            // Редакция.
            form.Children.Add(MakeFieldLabel(T("Updates.SelectEdition")));
            _editionCombo = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = 36
            };
            _editionCombo.SelectionChanged += OnEditionSelectionChanged;
            form.Children.Add(_editionCombo);

            // Режим URL: авто / вручную.
            form.Children.Add(MakeFieldLabel(T("Updates.Url")));
            var radioRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20 };
            _autoUrlRadio = new RadioButton { Content = T("Updates.UrlAuto"), GroupName = "UrlMode" };
            _autoUrlRadio.IsCheckedChanged += OnUrlModeChanged;
            _manualUrlRadio = new RadioButton { Content = T("Updates.UrlManual"), GroupName = "UrlMode" };
            _manualUrlRadio.IsCheckedChanged += OnUrlModeChanged;
            radioRow.Children.Add(_autoUrlRadio);
            radioRow.Children.Add(_manualUrlRadio);
            form.Children.Add(radioRow);

            _urlBox = new TextBox
            {
                IsReadOnly = true,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Center,
                MinHeight = 36
            };
            _urlBox.Styled(ControlThemes.ModernTextBox);
            form.Children.Add(_urlBox);

            // Сегмент адреса (ник каталога релизов).
            form.Children.Add(MakeFieldLabel(T("Updates.Segment")));
            _segmentBox = new TextBox
            {
                FontSize = 12,
                MinHeight = 36
            };
            _segmentBox.Styled(ControlThemes.ModernTextBox);
            _segmentBox.TextChanged += OnSegmentChanged;
            form.Children.Add(_segmentBox);

            var segmentHint = new TextBlock
            {
                Text = T("Updates.SegmentHint"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            Themes.ThemeBrushes.Bind(segmentHint, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            form.Children.Add(segmentHint);

            // «Список типовых конфигураций» + «Определить версию» — в одну строку (issue #322);
            // название конфигурации в поле выше занимает всю ширину окна. Надпись о текущих
            // свойствах базы — на отдельной строке под кнопками (не ужимает их и сама
            // переносится по всей ширине), ниже — подсказка о критериях поиска.
            var actionArea = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Spacing = 6 };

            var buttonsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            var configTypesButton = new Button
            {
                Content = T("Updates.ManageList"),
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            configTypesButton.Styled(ControlThemes.SecondaryButton);
            configTypesButton.Click += (_, _) => OnOpenConfigTypesClick();
            buttonsRow.Children.Add(configTypesButton);

            _defineVersionButton = new Button
            {
                Content = T("Updates.DefineVersion"),
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _defineVersionButton.Styled(ControlThemes.SecondaryButton);
            _defineVersionButton.Click += (_, _) => OnDefineVersionClick();
            ToolTip.SetTip(_defineVersionButton, T("Updates.DefineVersionHint"));
            buttonsRow.Children.Add(_defineVersionButton);

            actionArea.Children.Add(buttonsRow);

            _currentConfigText.FontSize = 12;
            _currentConfigText.TextWrapping = TextWrapping.Wrap;
            _currentConfigText.Margin = new Thickness(0, 2, 0, 0);
            _currentConfigText.HorizontalAlignment = HorizontalAlignment.Left;
            Themes.ThemeBrushes.Bind(_currentConfigText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            actionArea.Children.Add(_currentConfigText);

            var matchHint = new TextBlock
            {
                Text = T("Updates.MatchCriteriaHint"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
            Themes.ThemeBrushes.Bind(matchHint, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            actionArea.Children.Add(matchHint);

            form.Children.Add(actionArea);

            _resultText.FontSize = 12;
            _resultText.TextWrapping = TextWrapping.Wrap;
            _resultText.Margin = new Thickness(0, 8, 0, 0);
            Themes.ThemeBrushes.Bind(_resultText, TextBlock.ForegroundProperty, "SuccessBrush");
            form.Children.Add(_resultText);

            var scroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 2);
            grid.Children.Add(scroll);

            // Нижняя панель: Сохранить / Отмена.
            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bottom.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            bottom.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10
            };
            var save = new Button { Content = T("Updates.Save"), Width = 150, Height = 36, IsDefault = true };
            save.Styled(ControlThemes.DialogConfirmButton);
            save.Click += (_, _) => OnSaveClick();

            var cancel = BuildCancelActionButton(120);
            cancel.Click += (_, _) => OnCancelClick();

            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            Grid.SetColumn(buttons, 1);
            bottom.Children.Add(buttons);

            Grid.SetRow(bottom, 3);
            grid.Children.Add(bottom);

            return grid;
        }

        private static TextBlock MakeFieldLabel(string text)
        {
            var label = new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 14, 0, 4)
            };
            Themes.ThemeBrushes.Bind(label, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            return label;
        }
    }
}
#endif