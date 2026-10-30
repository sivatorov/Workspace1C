<#
  _build_v10014.ps1 - сборка Windows-артефакта релиза 1.0.1.4 (build only, без git/release).

  Схема повторяет прошлые циклы публикации (Windows-часть _build_v10013.ps1 / CI release.yml):
    * Windows: Workspace1C\build-windows-single-file.ps1
               -> Workspace1C\dist\win-x64\Workspace1C.exe
               (WPF, net10.0-windows, win-x64, self-contained single-file)
    * Копия в dist\release\Workspace1C.exe (имя релизного asset'а, как в .github/workflows/release.yml).

  Лог сборки: _publish_10014.log
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$Root       = $PSScriptRoot
$Version    = '1.0.1.4'
$LogFile    = Join-Path $Root '_publish_10014.log'
$ReleaseDir = Join-Path $Root 'dist\release'

$WinScript  = Join-Path $Root 'Workspace1C\build-windows-single-file.ps1'
$WinExe     = Join-Path $Root 'Workspace1C\dist\win-x64\Workspace1C.exe'
$ReleaseExe = Join-Path $ReleaseDir 'Workspace1C.exe'

$script:Fail = 0

function Stamp {
    param([string]$Msg)
    Write-Output ('{0}  {1}' -f (Get-Date -Format 'HH:mm:ss'), $Msg)
}

& {
    Stamp "=== Publish Windows artifact v$Version (build only) ==="
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

    # --- 2. Verify artifact ----------------------------------------------------
    Stamp "[2/3] Verify artifact"
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

    # --- 3. Copy to dist\release ------------------------------------------------
    if (Test-Path $WinExe) {
        Stamp "[3/3] Copy artifact to $ReleaseExe"
        New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null
        Copy-Item -Force $WinExe $ReleaseExe
        $ri = Get-Item $ReleaseExe
        Stamp ("  " + $ri.FullName + "  " + $ri.Length + " bytes (" + [math]::Round($ri.Length / 1MB, 1) + " MB)")
        Stamp ("  FileVersion: " + $ri.VersionInfo.FileVersion)
    } else {
        $script:Fail = 1
        Stamp "ERROR: cannot copy artifact to $ReleaseExe - binary missing"
    }

    if ($script:Fail -eq 0) {
        Stamp "=== DONE: dist\release\Workspace1C.exe (FileVersion 1.0.1.4) ==="
    } else {
        Stamp "=== BUILD FAILED (see errors above) ==="
    }
} *>&1 | Tee-Object -FilePath $LogFile

# Windows PowerShell 5.1 пишет Tee-Object в UTF-16: перекодируем лог в UTF-8 (с BOM).
try {
    if (Test-Path $LogFile) {
        $bytes   = [System.IO.File]::ReadAllBytes($LogFile)
        $isUtf16 = ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE)
        if ($isUtf16) {
            $text = [System.Text.Encoding]::Unicode.GetString($bytes)
            [System.IO.File]::WriteAllText($LogFile, $text, (New-Object System.Text.UTF8Encoding($true)))
        }
    }
} catch { }

exit $script:Fail
