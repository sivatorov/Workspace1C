<#
  _build_v100111.ps1 - сборка артефактов релиза 1.0.1.11 (Windows + Linux) БЕЗ git/release.

  Схема повторяет прошлые циклы публикации (_build_v100110.ps1 / CI release.yml):
    * Windows: Workspace1C\build-windows-single-file.ps1
               -> Workspace1C\dist\win-x64\Workspace1C.exe
               (WPF, net10.0-windows, win-x64, self-contained single-file)
    * Linux:   Workspace1C\build-linux-single-file.ps1  (cross-compile, ForceLinux=true)
               -> Workspace1C\dist\linux-x64\Workspace1C
               (Avalonia, net10.0, linux-x64, self-contained single-file)

  Результат копируется в publish\out-1.0.1.11\ под именами релизных asset'ов
  (как в .github/workflows/release.yml и релизе v1.0.1.10):
  Workspace1C.exe, Workspace1C-linux-x64.

  Лог сборки: _publish_100111.log
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$Root    = $PSScriptRoot
$Version = '1.0.1.11'
$LogFile = Join-Path $Root '_publish_100111.log'
$OutDir  = Join-Path $Root ("publish\out-" + $Version)

$WinScript = Join-Path $Root 'Workspace1C\build-windows-single-file.ps1'
$LinScript = Join-Path $Root 'Workspace1C\build-linux-single-file.ps1'
$WinExe    = Join-Path $Root 'Workspace1C\dist\win-x64\Workspace1C.exe'
$LinBin    = Join-Path $Root 'Workspace1C\dist\linux-x64\Workspace1C'

$script:Fail = 0

function Stamp {
    param([string]$Msg)
    Write-Output ('{0}  {1}' -f (Get-Date -Format 'HH:mm:ss'), $Msg)
}

& {
    Stamp "=== Publish artifacts v$Version (Windows + Linux), build only ==="
    Stamp "No git add/commit/push, no GitHub release, no Telegram."

    # --- 1. Windows build ------------------------------------------------------
    Stamp "[1/4] Windows: build-windows-single-file.ps1 (WPF, net10.0-windows, win-x64, self-contained single-file)"
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

    # --- 2. Linux build --------------------------------------------------------
    Stamp "[2/4] Linux: build-linux-single-file.ps1 (Avalonia, net10.0, linux-x64, cross-compile from Windows)"
    if (-not (Test-Path $LinScript)) {
        $script:Fail = 1
        Stamp "ERROR: script not found: $LinScript"
    } else {
        & $LinScript -Configuration Release
        if ($LASTEXITCODE -ne 0) {
            $script:Fail = 1
            Stamp "ERROR: Linux build failed with exit code $LASTEXITCODE"
        } else {
            Stamp "Linux build OK (exit 0)"
        }
    }

    # --- 3. Verify artifacts ----------------------------------------------------
    Stamp "[3/4] Verify artifacts"
    if (Test-Path $WinExe) {
        $fi  = Get-Item $WinExe
        $ver = $fi.VersionInfo.FileVersion
        Stamp ("Windows artifact: " + $fi.FullName)
        Stamp ("  size: " + $fi.Length + " bytes (" + [math]::Round($fi.Length / 1MB, 1) + " MB)")
        Stamp ("  FileVersion: " + $ver + " (expected " + $Version + ")")
        if ($ver -ne $Version) {
            Stamp "  WARNING: FileVersion mismatch!"
        }
    } else {
        $script:Fail = 1
        Stamp "ERROR: Windows artifact not found: $WinExe"
    }

    if (Test-Path $LinBin) {
        $fi = Get-Item $LinBin
        $ver = $fi.VersionInfo.FileVersion
        Stamp ("Linux artifact: " + $fi.FullName)
        Stamp ("  size: " + $fi.Length + " bytes (" + [math]::Round($fi.Length / 1MB, 1) + " MB)")
        Stamp ("  FileVersion: " + $ver + " (ELF: версия задаётся csproj InformationalVersion=" + $Version + ")")
    } else {
        $script:Fail = 1
        Stamp "ERROR: Linux artifact not found: $LinBin"
    }

    # --- 4. Copy to publish\out-1.0.1.11 ------------------------------------------
    if ((Test-Path $WinExe) -and (Test-Path $LinBin)) {
        Stamp "[4/4] Copy artifacts to $OutDir"
        New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
        Copy-Item -Force $WinExe (Join-Path $OutDir 'Workspace1C.exe')
        Copy-Item -Force $LinBin (Join-Path $OutDir 'Workspace1C-linux-x64')
        Get-ChildItem $OutDir | ForEach-Object {
            Stamp ("  " + $_.FullName + "  " + $_.Length + " bytes (" + [math]::Round($_.Length / 1MB, 1) + " MB)")
        }
    } else {
        $script:Fail = 1
        Stamp "ERROR: cannot copy artifacts to $OutDir - one of the binaries is missing"
    }

    if ($script:Fail -eq 0) {
        Stamp "=== DONE: publish\out-1.0.1.11\Workspace1C.exe and publish\out-1.0.1.11\Workspace1C-linux-x64 ==="
    } else {
        Stamp "=== BUILD FAILED (see errors above) ==="
    }
} *>&1 | Tee-Object -FilePath $LogFile

# Windows PowerShell 5.1 пишет Tee-Object в UTF-16: перекодируем лог в UTF-8 (с BOM).
try {
    if (Test-Path $LogFile) {
        $bytes = [System.IO.File]::ReadAllBytes($LogFile)
        $isUtf16 = ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE)
        if ($isUtf16) {
            $text = [System.Text.Encoding]::Unicode.GetString($bytes)
            [System.IO.File]::WriteAllText($LogFile, $text, (New-Object System.Text.UTF8Encoding($true)))
        }
    }
} catch { }

exit $script:Fail
