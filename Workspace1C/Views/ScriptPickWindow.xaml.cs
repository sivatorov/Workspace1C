#if WINDOWS
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Выбор скрипта» (Windows/WPF, issue #308): список сценариев запуска скриптов
/// с живой подсказкой полной командной строки (подстановки выполнены для выбранной
/// базы). Запуск — двойным кликом или кнопкой «Выполнить».
/// </summary>
public partial class ScriptPickWindow : Window
{
    private readonly IScriptScenarioStore _store;
    private readonly Infobase _infobase;
    private readonly MainViewModel _vm;

    public ScriptPickWindow(Infobase infobase, MainViewModel vm)
    {
        InitializeComponent();
        _store = AppServices.GetRequiredService<IScriptScenarioStore>();
        _infobase = infobase;
        _vm = vm;

        Title = T("Script.PickTitle");
        HintText.Text = string.Format(T("Script.PickHint"), infobase.Name);
        PreviewLabel.Text = T("Script.CommandLinePreview");
        RunButton.Content = T("Script.RunShort");
        EditButton.Content = T("Common.Edit");

        LoadScenarios();

        // issue #308: активным при открытии делаем список, чтобы сценарий можно было
        // сразу выбирать курсором (стрелками). Первый пункт выбираем заранее — при
        // фокусе список уже «стоит» на нём, а кнопка «Выполнить» активна.
        if (ScenariosList.Items.Count > 0)
            ScenariosList.SelectedIndex = 0;
        // Focus до показа окна не срабатывает — ставим фокус после загрузки.
        Loaded += (_, _) => FocusScenarioList();
    }

    /// <summary>Передаёт фокус списку сценариев (issue #308).</summary>
    private void FocusScenarioList()
    {
        ScenariosList.Focus();
        Keyboard.Focus(ScenariosList);

        // issue #308 (финал): на Loaded окно может быть ещё неактивным, и WPF отдаёт
        // фокус первому focusable-элементу окна — ListBox фактически не получает
        // клавиатурный фокус, и стрелки не двигают выбор до клика мышью. Повторяем
        // перенос фокуса после полной активации/первой отрисовки окна.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded || !IsVisible)
                return;
            ScenariosList.Focus();
            Keyboard.Focus(ScenariosList);
            if (ScenariosList.SelectedIndex >= 0 &&
                ScenariosList.ItemContainerGenerator.ContainerFromIndex(ScenariosList.SelectedIndex)
                    is System.Windows.Controls.ListBoxItem container)
            {
                container.Focus();
                Keyboard.Focus(container);
            }
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static string T(string key) => LocalizationManager.T(key);

    private void LoadScenarios()
    {
        ScenariosList.ItemsSource = _store.LoadAll()
            .Select(s => new ScriptScenarioItemViewModel(s))
            .ToList();
    }

    private ScriptScenarioItemViewModel? Selected => ScenariosList.SelectedItem as ScriptScenarioItemViewModel;

    private void ScenariosList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var has = Selected is not null;
        RunButton.IsEnabled = has;
        EditButton.IsEnabled = has;
        UpdatePreview();
    }

    private void ScenariosList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is not null)
            Run_Click(sender, e);
    }

    /// <summary>Enter на выделенной строке = кнопка «Выполнить» (issue #308).</summary>
    private void ScenariosList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Selected is not null)
        {
            e.Handled = true;
            Run_Click(sender, e);
        }
    }

    private void UpdatePreview()
    {
        PreviewBox.Text = Selected is { } item
            ? ScriptScenarioEditViewModel.BuildExampleCommandLine(item.Scenario, _infobase)
            : "—";
    }

    /// <summary>
    /// «Изменить» (issue #308, п.8): открывает редактор выбранного сценария; после
    /// сохранения изменения записываются в хранилище (тот же Id) и список обновляется,
    /// выбранный элемент восстанавливается — кнопки и превью соответствуют ему.
    /// </summary>
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item)
            return;
        // Issue #67 (п.4): «База для примера» в редакторе — база, из которой запущен
        // сценарий (InternalBase), а не всегда первая в списке.
        var edit = new ScriptScenarioEditWindow(item.Scenario, _infobase);
        edit.Owner = this;
        if (edit.ShowDialog() == true && edit.Result is { } updated)
        {
            _store.Save(updated);
            LoadScenarios();
            var updatedItem = ScenariosList.Items.Cast<ScriptScenarioItemViewModel>()
                .FirstOrDefault(i => i.Id == updated.Id);
            if (updatedItem is not null)
                ScenariosList.SelectedItem = updatedItem;
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        // v100120: async void — исключение не должно уходить в глобальный обработчик
        // без контекста операции (аудит async void, шаблон RelayCommand.Execute).
        try
        {
            if (Selected is not { } item)
                return;
            await _vm.RunScriptAsync(_infobase, item.Scenario);
            Close();
        }
        catch (Exception ex)
        {
            try { AppServices.TryGetService<Services.IAppLogger>()?.Error("Ошибка запуска сценария скрипта", ex); } catch { /* лог не роняет обработчик */ }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
#endif