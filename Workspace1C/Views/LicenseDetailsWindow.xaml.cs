#if WINDOWS
using System.Windows;
using Workspace1C.Localization;
using Workspace1C.Models;
using Workspace1C.Services;

namespace Workspace1C
{
    /// <summary>
    /// Окно «Лицензия <серийный номер>» (issue #83, v1.0.1.27): читабельная
    /// информация из файла *.lic — тип, версия, владелец, пользователи, срок действия,
    /// даты, рег. номер и путь к файлу — БЕЗ начального длинного ключа. Открывается
    /// кликом по колонке «Серийный номер» таблицы «Установленные лицензии» окна
    /// «Серверы 1С»; кнопка «Открыть папку» использует общий
    /// <see cref="LicenseFolderOpener"/>.
    /// </summary>
    public partial class LicenseDetailsWindow : Window
    {
        private readonly ProgramLicense _license;

        /// <summary>Создаёт окно деталей по модели лицензии.</summary>
        public LicenseDetailsWindow(ProgramLicense license)
        {
            InitializeComponent();
            _license = license;
            Title = string.Format(
                LocalizationManager.T("ServerMonitor.Licenses.DetailsTitle"), license.SerialNumber);
            DetailsText.Text = RingLicenseParser.BuildReadableDetails(license);
        }

        /// <summary>«Открыть папку»: папка с файлом лицензии; ошибки — тихо, сообщением.</summary>
        private void OnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!LicenseFolderOpener.TryOpenFolderWithFile(_license.FilePath))
            {
                MessageBox.Show(
                    this,
                    string.Format(
                        LocalizationManager.T("ServerMonitor.Licenses.FileNotFound"), _license.FilePath),
                    Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }
}
#endif
