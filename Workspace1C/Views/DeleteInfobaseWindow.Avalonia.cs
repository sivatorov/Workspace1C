#if LINUX
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;
using Workspace1C.Themes;

namespace Workspace1C
{
    /// <summary>
    /// Диалог удаления ИБ: сведения о базе и опция физического удаления каталога (файловые базы).
    /// Avalonia/Linux-версия WPF-окна <see cref="DeleteInfobaseWindow"/>.
    /// Раскладка лежит в DeleteInfobaseWindow.axaml, здесь только данные и поведение.
    /// </summary>
    public partial class DeleteInfobaseWindow : ModalWindowBase
    {
        private readonly Infobase _infobase;
        private readonly IDialogService _dialogs;
        private readonly CheckBox _physicalCheck;
        private readonly Control _physicalPanel;

        /// <summary>Пользователь подтвердил удаление.</summary>
        public bool Confirmed { get; private set; }

        /// <summary>Нужно физически удалить каталог файловой базы.</summary>
        public bool DeletePhysically { get; private set; }

        public DeleteInfobaseWindow(Infobase infobase)
        {
            AvaloniaXamlLoader.Load(this);

            _infobase = infobase;
            _dialogs = AppServices.GetRequiredService<IDialogService>();

            _physicalPanel = this.FindControl<Border>("PhysicalPanel")!;
            _physicalCheck = this.FindControl<CheckBox>("PhysicalCheck")!;

            FillDetails();
            FillPhysicalSection();

            this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
            this.FindControl<Button>("DeleteButton")!.Click += (_, _) => OnDelete_Click();
        }

        private void FillDetails()
        {
            void Set(string name, string value) => this.FindControl<TextBlock>(name)!.Text = value;

            Set("NameText", string.IsNullOrWhiteSpace(_infobase.Name) ? "—" : _infobase.Name);
            Set("TypeText", _infobase.ConnectionTypeDisplay);
            Set("PathText", string.IsNullOrWhiteSpace(_infobase.ServerDatabaseDisplay)
                ? (_infobase.ConnectionStringDisplay ?? "—")
                : _infobase.ServerDatabaseDisplay);
            Set("GroupText", string.IsNullOrWhiteSpace(_infobase.Group)
                ? LocalizationManager.T("Connection.NoGroup")
                : _infobase.Group);
            Set("PlatformText", string.IsNullOrWhiteSpace(_infobase.PlatformVersion)
                ? "—" : _infobase.PlatformVersion);
        }

        private void FillPhysicalSection()
        {
            var existsText = this.FindControl<TextBlock>("ExistsText")!;
            var hint = this.FindControl<TextBlock>("PhysicalHint")!;

            if (_infobase.Connection.Type != ConnectionType.File)
            {
                // Не файловая база: физически удалять нечего, панель прячется целиком.
                existsText.Text = LocalizationManager.T("DeleteInfobase.NonFileOnlyFromList");
                // Значение гасится, как в коде за разметкой
                // (DeleteInfobaseWindow.xaml.cs:65, там жёсткий серый): удалять
                // с диска нечего. Кисть берём из темы, иначе в тёмной схеме
                // постоянный серый выпадает из палитры.
                Themes.ThemeBrushes.Bind(existsText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
                _physicalPanel.IsVisible = false;
                return;
            }

            var dir = InfobaseMaintenanceService.GetFileBaseDirectory(_infobase);
            var exists = InfobaseMaintenanceService.FileBaseExists(_infobase);

            if (exists && !string.IsNullOrEmpty(dir))
            {
                existsText.Text = string.Format(LocalizationManager.T("DeleteInfobase.ExistsYes"), dir);
                Themes.ThemeBrushes.Bind(existsText, TextBlock.ForegroundProperty, "SuccessBrush");
                _physicalCheck.IsEnabled = true;
                // Подсказка из разметки общая, а здесь известен каталог,
                // поэтому текст уточняется на предметный.
                hint.Text = string.Format(LocalizationManager.T("DeleteInfobase.PhysicalHintDynamic"), dir);
                return;
            }

            existsText.Text = string.IsNullOrEmpty(dir)
                ? LocalizationManager.T("DeleteInfobase.DirNotSpecified")
                : string.Format(LocalizationManager.T("DeleteInfobase.DirNotFound"), dir);
            Themes.ThemeBrushes.Bind(existsText, TextBlock.ForegroundProperty, "TextSecondaryBrush");
            _physicalCheck.IsEnabled = false;
            _physicalCheck.IsChecked = false;
            hint.Text = LocalizationManager.T("DeleteInfobase.PhysicalUnavailable");
        }

        private void OnDelete_Click()
        {
            DeletePhysically = _physicalCheck.IsChecked == true
                               && _physicalPanel.IsVisible
                               && _physicalCheck.IsEnabled;

            if (DeletePhysically)
            {
                var dir = InfobaseMaintenanceService.GetFileBaseDirectory(_infobase) ?? "";
                var confirm = _dialogs.Confirm(
                    string.Format(LocalizationManager.T("DeleteInfobase.PhysicalConfirm"), dir),
                    LocalizationManager.T("DeleteInfobase.PhysicalDeleteTitle"));
                if (!confirm)
                    return;
            }

            Confirmed = true;
            DialogResult = true;
            Close();
        }
    }
}
#endif
