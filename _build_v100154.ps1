<#
  _build_v100154.ps1 - сборка артефакта релиза 1.0.1.54 (ТОЛЬКО Windows) БЕЗ git/release.

  Схема повторяет прошлый цикл публикации (_build_v100153.ps1 / CI release.yml):
    * Windows: Workspace1C\build-windows-single-file.ps1
               -> Workspace1C\dist\win-x64\Workspace1C.exe
               (WPF, net10.0-windows, win-x64, self-contained single-file)

  Результат копируется в publish\out-1.0.1.54\ под именем релизного asset'а
  (как в .github/workflows/release.yml и релизах v1.0.1.27..v1.0.1.53):
    Workspace1C.exe              (win-x64)

  Лог сборки: _publish_100154.log

  Примечание: первая сборка WPF после очистки obj может упасть (известная особенность
  .NET 10 RC SDK temp-project) - при падении просто повторить прогон скрипта.

  Состав артефакта соответствует релизам v1.0.1.27..v1.0.1.53:
    Workspace1C.exe (bare exe, без zip) - AppImage/deb не публикуются.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$Root    = $PSScriptRoot
$Version = '1.0.1.54'
$LogFile = Join-Path $Root '_publish_100154.log'
$OutDir  = Join-Path $Root ("publish\out-" + $Version)

$WinScript = Join-Path $Root 'Workspace1C\build-windows-single-file.ps1'
$WinExe    = Join-Path $Root 'Workspace1C\dist\win-x64\Workspace1C.exe'

$script:Fail = 0

function Stamp {
    param([string]$Msg)
    Write-Output ('{0}  {1}' -f (Get-Date -Format 'HH:mm:ss'), $Msg)
}

function Show-Artifact {
    param([string]$Path, [string]$ExpectedVersion)
    if (Test-Path $Path) {
        $fi = Get-Item $Path
        Stamp ("Artifact: " + $fi.FullName)
        Stamp ("  size: " + $fi.Length + " bytes (" + [math]::Round($fi.Length / 1MB, 1) + " MB)")
        if ($ExpectedVersion -ne '') {
            # Issue #89: проверяем ОБЕ версии - FileVersion и ProductVersion
            # (InformationalVersion), чтобы расхождение ловилось на этапе сборки.
            # Суффикс «+<sha>» в InformationalVersion .NET SDK добавляет автоматически
            # при сборке из git-репозитория (SourceRevisionId); приложение само
            # отбрасывает его (VersionInfo.StripHash), поэтому сравниваем базу до «+».
            $fileVer = $fi.VersionInfo.FileVersion
            $prodVer = $fi.VersionInfo.ProductVersion
            $prodVerBase = ($prodVer -split '\+')[0]
            Stamp ("  FileVersion: " + $fileVer + " (expected " + $ExpectedVersion + ")")
            if ($fileVer -ne $ExpectedVersion) {
                Stamp "  WARNING: FileVersion mismatch!"
                $script:Fail = 1
            }
            Stamp ("  ProductVersion: " + $prodVer + " (expected " + $ExpectedVersion + "*)")
            if ($prodVerBase -ne $ExpectedVersion) {
                Stamp "  WARNING: ProductVersion (InformationalVersion) mismatch!"
                $script:Fail = 1
            }
        }
        Stamp ("  SHA-256: " + (Get-FileHash -Algorithm SHA256 $Path).Hash)
    } else {
        $script:Fail = 1
        Stamp "ERROR: artifact not found: $Path"
    }
}

& {
    Stamp "=== Publish artifact v$Version (Windows only), build only ==="
    Stamp "No git add/commit/push, no GitHub release, no Telegram."

    # --- 1. Windows build ------------------------------------------------------
    Stamp "[1/3] Windows: build-windows-single-file.ps1 (WPF, net10.0-windows, win-x64, self-contained single-file)"
    if (-not (Test-Path $WinScript)) {
        $script:Fail = 1
        Stamp "ERROR: script not found: $WinScript"
    } else {
        & $WinScript -Configuration Release
        if ($LASTEXITCODE -ne 0) {
            $script:Fail = 1
            Stamp "ERROR: Windows build failed with exit code $LASTEXITCODE"
        } else {
            Stamp "Windows build OK (exit 0)"
        }
    }

    # --- 2. Verify artifact -----------------------------------------------------
    Stamp "[2/3] Verify artifact"
    Show-Artifact -Path $WinExe -ExpectedVersion $Version

    # --- 3. Copy to publish\out-1.0.1.54 -----------------------------------------
    Stamp "[3/3] Copy artifact to $OutDir"
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    if (Test-Path $WinExe) { Copy-Item -Force $WinExe (Join-Path $OutDir 'Workspace1C.exe') }
    Get-ChildItem $OutDir | ForEach-Object {
        Stamp ("  " + $_.Name + "  " + $_.Length + " bytes (" + [math]::Round($_.Length / 1MB, 1) + " MB)  SHA-256: " + (Get-FileHash -Algorithm SHA256 $_.FullName).Hash)
    }

    if ($script:Fail -eq 0) {
        Stamp "=== DONE: publish\out-1.0.1.54\Workspace1C.exe ==="
    } else {
        Stamp "=== BUILD FAILED (see errors above) ==="
    }
} *>&1 | Tee-Object -FilePath $LogFile

# Windows PowerShell 5.1 пишет Tee-Object в UTF-16: перекодируем лог в UTF-8 (с BOM).
foreach ($logPath in @($LogFile)) {
    try {
        if ($logPath -and (Test-Path $logPath)) {
            $bytes = [System.IO.File]::ReadAllBytes($logPath)
            $isUtf16 = ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE)
            if ($isUtf16) {
                $text = [System.Text.Encoding]::Unicode.GetString($bytes)
                [System.IO.File]::WriteAllText($logPath, $text, (New-Object System.Text.UTF8Encoding($true)))
            }
        }
    } catch { }
}

exit $script:Fail
