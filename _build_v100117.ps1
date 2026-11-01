<#
  _build_v100117.ps1 - сборка артефакта релиза 1.0.1.17 (только Windows) БЕЗ git/release.

  Схема повторяет прошлый цикл публикации (_build_v100116.ps1 / CI release.yml):
    * Windows: Workspace1C\build-windows-single-file.ps1
               -> Workspace1C\dist\win-x64\Workspace1C.exe
               (WPF, net10.0-windows, win-x64, self-contained single-file)

  Результат копируется в publish\out-1.0.1.17\ под именем релизного asset'а
  (как в .github/workflows/release.yml и релизе v1.0.1.10): Workspace1C.exe.

  Лог сборки: _publish_100117.log

  Примечание: первая сборка WPF после очистки obj может упасть (известная особенность
  .NET 10 RC SDK temp-project) - при падении просто повторить прогон скрипта.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$Root    = $PSScriptRoot
$Version = '1.0.1.17'
$LogFile = Join-Path $Root '_publish_100117.log'
$OutDir  = Join-Path $Root ("publish\out-" + $Version)

$WinScript = Join-Path $Root 'Workspace1C\build-windows-single-file.ps1'
$WinExe    = Join-Path $Root 'Workspace1C\dist\win-x64\Workspace1C.exe'

$script:Fail = 0

function Stamp {
    param([string]$Msg)
    Write-Output ('{0}  {1}' -f (Get-Date -Format 'HH:mm:ss'), $Msg)
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
            $script:Fail = 1
        }
    } else {
        $script:Fail = 1
        Stamp "ERROR: Windows artifact not found: $WinExe"
    }

    # --- 3. Copy to publish\out-1.0.1.17 -----------------------------------------
    if (Test-Path $WinExe) {
        Stamp "[3/3] Copy artifact to $OutDir"
        New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
        Copy-Item -Force $WinExe (Join-Path $OutDir 'Workspace1C.exe')
        Get-ChildItem $OutDir | ForEach-Object {
            Stamp ("  " + $_.FullName + "  " + $_.Length + " bytes (" + [math]::Round($_.Length / 1MB, 1) + " MB)")
        }
    } else {
        $script:Fail = 1
        Stamp "ERROR: cannot copy artifact to $OutDir - binary is missing"
    }

    if ($script:Fail -eq 0) {
        Stamp "=== DONE: publish\out-1.0.1.17\Workspace1C.exe ==="
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
