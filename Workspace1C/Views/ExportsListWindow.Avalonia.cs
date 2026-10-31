#if LINUX
using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;
using Workspace1C.ViewModels;

namespace Workspace1C;

/// <summary>
/// Окно «Список выгрузок» (Avalonia/Linux): обзор созданных файлов резервных копий
/// и восстановление данных в выбранную ИБ (в т.ч. через /RestoreIB).
/// </summary>
public sealed class ExportsListWindow : ModalWindowBase
{
    private readonly ExportsListViewModel _vm;
    private readonly Infobase? _infobase;
    private readonly MainViewModel? _mainVm;
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly ListBox _list = new();
    private readonly Button _restoreButton = new();
    private readonly Button _refreshButton = new();

    /// <param name="infobase">ИБ, в которую выполняется восстановление (может быть null).</param>
    /// <param name="mainVm">MainViewModel для операции восстановления (может быть null).</param>
    public ExportsListWindow(Infobase? infobase = null, MainViewModel? mainVm = null)
    {
        _infobase = infobase;
        _mainVm = mainVm;
        _vm = new ExportsListViewModel(
            AppServices.GetRequiredService<IBackupScenarioStore>(),
            AppServices.GetRequiredService<IInfobaseRepository>(),
            AppServices.GetRequiredService<IAppLogger>());
        Title = T("Restore.Title");
        Width = 820;
        Height = 520;
        MinWidth = 680;
        MinHeight = 400;
        FontSize = 13;
        Content = BuildRoot();
        // Сканирование каталогов НЕ в конструкторе (аудит производительности): сетевые
        // пути блокировали открытие окна. Первое обновление — в Opened, фоновой задачей.
        Opened += async (_, _) => await RefreshItemsAsync();
    }

    /// <summary>Фоновое обновление списка с занятием кнопок на время сканирования.</summary>
    private async System.Threading.Tasks.Task RefreshItemsAsync()
    {
        _restoreButton.IsEnabled = false;
        _refreshButton.IsEnabled = false;
        try
        {
            await _vm.RefreshAsync();
        }
        finally
        {
            _refreshButton.IsEnabled = true;
            _restoreButton.IsEnabled = _infobase is not null && _mainVm is not null;
        }
    }

    /// <summary>Показывает окно модально (синхронно).</summary>
    public bool ShowSync(Window? owner = null) => ShowDialogSync(owner);

    private static string T(string key) => LocalizationManager.T(key);

    private Control BuildRoot()
    {
        var dock = new DockPanel { Margin = new Avalonia.Thickness(14) };

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        };
        // Кнопки списка выгрузок используют вторичный стиль темы (issue #46):
        // иначе они рисуются дефолтным стилем и не переключаются с темой.
        var refresh = _refreshButton;
        refresh.Content = T("Common.Refresh");
        refresh.Width = 100;
        refresh.Styled(ControlThemes.SecondaryButton);
        refresh.Click += async (_, _) => await RefreshItemsAsync();

        _restoreButton.Content = T("Restore.Title");
        _restoreButton.Width = 110;
        _restoreButton.Styled(ControlThemes.SecondaryButton);
        _restoreButton.IsEnabled = _infobase is not null && _mainVm is not null;
        _restoreButton.Click += async (_, _) => await RestoreSelectedAsync();

        var openFolder = new Button { Content = T("Restore.OpenFolder"), Width = 110 };
        openFolder.Styled(ControlThemes.SecondaryButton);
        openFolder.Click += (_, _) => OpenFolder();

        var close = new Button { Content = T("Common.Close"), Width = 90 };
        close.Styled(ControlThemes.SecondaryButton);
        close.Click += (_, _) => Close();

        bottom.Children.Add(refresh);
        bottom.Children.Add(_restoreButton);
        bottom.Children.Add(openFolder);
        bottom.Children.Add(close);

        _list.ItemsSource = _vm.Items;
        _list.ItemTemplate = new FuncDataTemplate<BackupExportItem>((item, _) => BuildItem(item));
        _list.SelectionChanged += (_, _) =>
            _restoreButton.IsEnabled = _list.SelectedItem is not null && _infobase is not null && _mainVm is not null;
        _list.Margin = new Avalonia.Thickness(0, 0, 0, 8);

        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(bottom);
        dock.Children.Add(_list);
        return dock;
    }

    private Control BuildItem(BackupExportItem item)
    {
        var panel = new StackPanel { Margin = new Avalonia.Thickness(4), Spacing = 2 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        header.Children.Add(new TextBlock { Text = item.FileName, FontWeight = FontWeight.SemiBold });
        // Вторичный текст — на кисти темы (аудит UI, п. 2.9) вместо Brushes.Gray.
        var sizeText = new TextBlock { Text = item.SizeText };
        ThemeBrushes.Bind(sizeText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
        header.Children.Add(sizeText);
        var writeText = new TextBlock { Text = item.LastWriteText };
        ThemeBrushes.Bind(writeText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
        header.Children.Add(writeText);
        panel.Children.Add(header);
        var directoryText = new TextBlock
        {
            Text = item.Directory,
            FontSize = 11,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
        };
        ThemeBrushes.Bind(directoryText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
        panel.Children.Add(directoryText);
        return panel;
    }

    private BackupExportItem? Selected => _list.SelectedItem as BackupExportItem;

    private async System.Threading.Tasks.Task RestoreSelectedAsync()
    {
        if (Selected is not { } item || _infobase is null || _mainVm is null)
            return;
        await _mainVm.RestoreAsync(_infobase, item);
    }

    private void OpenFolder()
    {
        var dir = Selected is { } sel ? sel.Directory : null;
        if (string.IsNullOrWhiteSpace(dir))
        {
            _dialogs.ShowInfo(T("Restore.SelectItem"), T("Restore.OpenFolder"));
            return;
        }
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{dir}\"",
                UseShellExecute = false
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(ex.Message, T("Restore.OpenFolder"));
        }
    }
}
#endif