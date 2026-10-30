param([string]$ImagePath = "G:\h\Workspace1C\_issue63_last_screenshot.png")
$ErrorActionPreference = "Stop"
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
    $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType = WindowsRuntime]
    $null = [Windows.Storage.StorageFile, Windows.Foundation, ContentType = WindowsRuntime]

    $source = @"
using System;
using System.Threading.Tasks;
using Windows.Media.Ocr;
using Windows.Graphics.Imaging;
using Windows.Storage;
public static class OcrHelper
{
    public static string Recognize(string path)
    {
        var file = StorageFile.GetFileFromPathAsync(path).AsTask().GetAwaiter().GetResult();
        var stream = file.OpenAsync(FileAccessMode.Read).AsTask().GetAwaiter().GetResult();
        var decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
        var bitmap = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null) return "ENGINE_NULL";
        var result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
        return result.Text;
    }
}
"@
    Add-Type -TypeDefinition $source -ReferencedAssemblies @(
        "System.Runtime.WindowsRuntime",
        "Windows.Foundation",
        "Windows.Storage",
        "Windows.Graphics",
        "Windows.Media.Ocr"
    )
    $text = [OcrHelper]::Recognize($ImagePath)
    Write-Output $text
} catch {
    Write-Output ("ERROR: " + $_.Exception.ToString())
    exit 1
}