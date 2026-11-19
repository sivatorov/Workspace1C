#if LINUX
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Workspace1C.Controls;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C;

/// <summary>
/// Окно «Заметки базы» (п.8, 1.0.0.90), Avalonia/Linux-версия WPF-окна
/// <see cref="BaseNotesWindow"/>: markdown-карточка (для кого база, контакты, особенности
/// обновления) с предпросмотром через <see cref="MarkdownRenderer.ToStackPanel"/> и список
/// секретов внешних сервисов. Значения секретов хранятся только в зашифрованном виде
/// (<see cref="NoteCrypto"/>: AES-256 ключом профиля; на Windows — DPAPI, значение другой
/// платформы показывается как «зашифровано, ключ недоступен»). Показ — по требованию
/// (кнопка «глаз»), секреты не логируются.
/// </summary>
public sealed class BaseNotesWindow : ModalWindowBase
{
    private readonly IDialogService _dialogs = AppServices.GetRequiredService<IDialogService>();
    private readonly string _infobaseKey;
    private readonly string _infobaseName;
    private readonly NoteStore _store;
    private readonly NoteCrypto _crypto;
    private readonly List<SecretRow> _secretRows = new();
    private readonly TextBox _markdownBox = new TextBox
    {
        AcceptsReturn = true,
        AcceptsTab = true,
        Height = 190,
        FontFamily = new FontFamily(ThemeManager.DefaultMonospaceFontFamily),
        VerticalContentAlignment = VerticalAlignment.Top
    }.Styled(ControlThemes.ModernTextBox);

    private readonly Border _previewHost = new()
    {
        IsVisible = false,
        Padding = new Thickness(8),
        BorderThickness = new Thickness(1)
    };

    private readonly StackPanel _secretsPanel = new() { Spacing = 6 };

    /// <summary>Строка секрета: имя + замаскированное значение с показом по требованию.</summary>
    private sealed class SecretRow
    {
        public TextBox NameBox { get; } = new TextBox { Height = 32 }.Styled(ControlThemes.ModernTextBox);
        public TextBox ValueBox { get; } = new TextBox
        {
            Height = 32,
            PasswordChar = '●'
        }.Styled(ControlThemes.ModernPasswordBox);
        public Button RevealButton { get; } = new()
        {
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        public TextBlock StatusText { get; } = new()
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        };
        public Button RemoveButton { get; } = new() { Width = 28, Height = 30, Padding = new Thickness(0) };
        public Panel Container { get; set; } = null!;
        public string? StoredCipher { get; set; }
        public string? Plain { get; set; }
        public bool IsRevealed { get; set; }

        public string NameValue => NameBox.Text?.Trim() ?? string.Empty;
        public string ValueFromUi => ValueBox.Text ?? string.Empty;
    }

    /// <param name="infobase">База, к которой относится заметка (ключ — ID/имя, см. <see cref="InfobaseNote.KeyFor"/>).</param>
    public BaseNotesWindow(Infobase infobase)
    {
        ArgumentNullException.ThrowIfNull(infobase);
        _infobaseKey = InfobaseNote.KeyFor(infobase);
        _infobaseName = infobase.Name ?? string.Empty;
        _store = NoteStoreAccess.GetOrCreate();
        _crypto = NoteCrypto.CreateDefault();

        Title = string.Format(LocalizationManager.T("Notes.Title"), _infobaseName);
        Width = 780;
        Height = 600;
        MinWidth = 640;
        MinHeight = 480;
        FontSize = 13;
        CanResize = true;

        ThemeBrushes.Bind(_previewHost, Border.BorderBrushProperty, "BorderBrushColor");

        var note = _store.Get(_infobaseKey);
        _markdownBox.Text = note?.Markdown ?? string.Empty;
        if (note is not null)
        {
            foreach (var pair in note.Secrets)
                AddSecretRow(pair.Key, pair.Value);
        }

        Content = BuildRoot(infobase);

        // Фокус в поле заметки (паттерн #299): отложенный вызов после показа окна.
        Opened += (_, _) =>
        {
            Dispatcher.UIThread.Post(() => _markdownBox.Focus(), DispatcherPriority.Background);
        };
    }

    private static string T(string key) => LocalizationManager.T(key);

    private Control BuildRoot(Infobase infobase)
    {
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 8 };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var title = new TextBlock { Text = Title, FontSize = 15, FontWeight = FontWeight.SemiBold };
        ThemeBrushes.Bind(title, TextBlock.ForegroundProperty, "TextPrimaryBrush");
        header.Children.Add(title);
        var subtitle = new TextBlock { Text = infobase.ServerDatabaseDisplay, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        ThemeBrushes.Bind(subtitle, TextBlock.ForegroundProperty, "FormLabelBrush");
        header.Children.Add(subtitle);
        panel.Children.Add(header);

        // --- Markdown-карточка: редактор + предпросмотр ---
        var previewToggle = new CheckBox
        {
            Content = T("Notes.Preview"),
            VerticalAlignment = VerticalAlignment.Center,
            IsChecked = false
        };
        previewToggle.IsCheckedChanged += (_, _) => TogglePreview(previewToggle.IsChecked == true);

        var labelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        var markdownLabel = new TextBlock { Text = T("Notes.MarkdownLabel"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        ThemeBrushes.Bind(markdownLabel, TextBlock.ForegroundProperty, "FormLabelBrush");
        labelRow.Children.Add(markdownLabel);
        labelRow.Children.Add(previewToggle);
        panel.Children.Add(labelRow);

        panel.Children.Add(_markdownBox);
        panel.Children.Add(_previewHost);

        // --- Секреты ---
        var secretsHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 14, 0, 0) };
        var secretsLabel = new TextBlock { Text = T("Notes.SecretsHeader"), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        ThemeBrushes.Bind(secretsLabel, TextBlock.ForegroundProperty, "FormLabelBrush");
        secretsHeader.Children.Add(secretsLabel);
        // Аудит UI P1c: компактная кнопка действия — 32 px (шкала 36/32/40),
        // а не 30; ширина по содержимому.
        var addButton = new Button { Content = T("Notes.AddSecret"), Height = 32 };
        addButton.Styled(ControlThemes.DialogConfirmButton);
        addButton.Click += (_, _) => AddSecretRow(string.Empty, null);
        secretsHeader.Children.Add(addButton);
        panel.Children.Add(secretsHeader);

        panel.Children.Add(_secretsPanel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(0, 12, 0, 0)
        };
        // Аудит UI P1c: минимальная ширина вместо фиксированной — английская
        // подпись не обрезается.
        var save = new Button { Content = T("Common.Save"), MinWidth = 120, Height = 36, IsDefault = true };
        save.Styled(ControlThemes.DialogConfirmButton);
        save.Click += (_, _) => OnSaveClick();
        var cancel = BuildCancelActionButton(120);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        return panel;
    }

    private void TogglePreview(bool visible)
    {
        if (visible)
        {
            _previewHost.Child = MarkdownRenderer.ToStackPanel(_markdownBox.Text ?? string.Empty);
            _markdownBox.IsVisible = false;
            _previewHost.IsVisible = true;
        }
        else
        {
            _markdownBox.IsVisible = true;
            _previewHost.IsVisible = false;
        }
    }

    private void AddSecretRow(string name, string? storedCipher)
    {
        var row = new SecretRow();
        row.NameBox.Text = name;
        row.RevealButton.Content = IconHelper.MakeIcon("IconEye", 16);
        row.RevealButton.Styled(ControlThemes.IconButton);
        ToolTip.SetTip(row.RevealButton, T("Connection.ShowPasswordTooltip"));
        row.RemoveButton.Content = IconHelper.MakeIcon("IconClose", UiMetrics.IconInline);
        row.RemoveButton.Styled(ControlThemes.IconButton);
        ToolTip.SetTip(row.RemoveButton, T("Notes.DeleteSecret"));
        ThemeBrushes.Bind(row.StatusText, TextBlock.ForegroundProperty, "TextSecondaryBrush");

        if (!string.IsNullOrEmpty(storedCipher))
        {
            row.StoredCipher = storedCipher;
            var decrypted = _crypto.Decrypt(storedCipher);
            if (decrypted.Status == NoteCryptoStatus.Ok)
            {
                row.Plain = decrypted.Value;
                row.ValueBox.Text = decrypted.Value ?? string.Empty;
            }
            else
            {
                // «Зашифровано, ключ недоступен»: показ невозможен, секрет на диске цел.
                row.ValueBox.IsEnabled = false;
                row.ValueBox.Text = string.Empty;
                row.StatusText.Text = T(NoteCrypto.StatusTextKey(decrypted.Status));
            }
        }

        row.RevealButton.Click += (_, _) => ToggleReveal(row);
        row.RemoveButton.Click += (_, _) =>
        {
            if (row.Container is not null)
                _secretsPanel.Children.Remove(row.Container);
            _secretRows.Remove(row);
        };

        var nameCell = new StackPanel { Spacing = 3 };
        nameCell.Children.Add(row.NameBox);
        nameCell.Children.Add(row.StatusText);

        var valueCell = new Grid();
        valueCell.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        valueCell.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(row.ValueBox, 0);
        valueCell.Children.Add(row.ValueBox);
        Grid.SetColumn(row.RevealButton, 1);
        row.RevealButton.Margin = new Thickness(6, 0, 0, 0);
        valueCell.Children.Add(row.RevealButton);

        var removeCell = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        removeCell.Children.Add(row.RemoveButton);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.6, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(nameCell, 0);
        grid.Children.Add(nameCell);
        Grid.SetColumn(valueCell, 1);
        grid.Children.Add(valueCell);
        Grid.SetColumn(removeCell, 2);
        grid.Children.Add(removeCell);

        row.Container = grid;
        _secretsPanel.Children.Add(grid);
        _secretRows.Add(row);
    }

    private void ToggleReveal(SecretRow row)
    {
        if (row is null)
            return;

        if (row.IsRevealed)
        {
            row.IsRevealed = false;
            row.ValueBox.PasswordChar = '●';
            row.ValueBox.RevealPassword = false;
            row.RevealButton.Content = IconHelper.MakeIcon("IconEye", 16);
            return;
        }

        // Показ по требованию: расшифровываем значение здесь и сейчас.
        if (!string.IsNullOrEmpty(row.StoredCipher))
        {
            var decrypted = _crypto.Decrypt(row.StoredCipher);
            if (decrypted.Status != NoteCryptoStatus.Ok)
            {
                row.StatusText.Text = T(NoteCrypto.StatusTextKey(decrypted.Status));
                return;
            }
            row.Plain = decrypted.Value;
            row.ValueBox.Text = decrypted.Value ?? string.Empty;
        }

        row.IsRevealed = true;
        // Issue #49: RevealPassword снимает маску в шаблоне TextBox Avalonia 11.
        row.ValueBox.PasswordChar = '\0';
        row.ValueBox.RevealPassword = true;
        row.RevealButton.Content = IconHelper.MakeIcon("IconEyeOff", 16);
        row.ValueBox.Focus();
    }

    private void OnSaveClick()
    {
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in _secretRows)
        {
            if (row is null)
                continue;
            var name = row.NameValue;
            if (name.Length == 0)
                continue;

            var currentValue = row.ValueFromUi;
            var unchanged = row.StoredCipher is not null && !row.IsRevealed && row.Plain is not null
                && string.Equals(row.Plain, currentValue, StringComparison.Ordinal);
            if (unchanged)
            {
                secrets[name] = row.StoredCipher!;
                continue;
            }

            var encrypted = _crypto.Encrypt(currentValue);
            if (encrypted.Status != NoteCryptoStatus.Ok)
            {
                _dialogs.ShowWarning(
                    string.Format(T("Notes.SecretEncryptError"), name, T(NoteCrypto.StatusTextKey(encrypted.Status))),
                    string.Format(T("Notes.Title"), _infobaseName));
                return;
            }
            secrets[name] = encrypted.Value ?? string.Empty;
        }

        _store.Save(new InfobaseNote
        {
            InfobaseId = _infobaseKey,
            Markdown = _markdownBox.Text ?? string.Empty,
            Secrets = secrets,
            UpdatedAt = DateTime.UtcNow
        });
        DialogResult = true;
        Close();
    }
}
#endif
