<#
  _build_v100123.ps1 - сборка артефактов релиза 1.0.1.23 (Windows + Linux) БЕЗ git/release.

  Схема повторяет прошлый цикл публикации (_build_v100122.ps1 / CI release.yml):
    * Windows: Workspace1C\build-windows-single-file.ps1
               -> Workspace1C\dist\win-x64\Workspace1C.exe
               (WPF, net10.0-windows, win-x64, self-contained single-file)
    * Linux:   Workspace1C\build-linux-single-file.ps1  (-p:ForceLinux=true)
               -> Workspace1C\dist\linux-x64\Workspace1C
               (Avalonia, net10.0, linux-x64, self-contained single-file)

  Результаты копируются в publish\out-1.0.1.23\ под именами релизных asset'ов
  (как в .github/workflows/release.yml и релизе v1.0.1.22):
    Workspace1C.exe              (win-x64)
    Workspace1C-linux-x64        (linux-x64)

  Логи сборки: _publish_100123.log, _build_linux_100123.log

  Примечание: первая сборка WPF после очистки obj может упасть (известная особенность
  .NET 10 RC SDK temp-project) - при падении просто повторить прогон скрипта.

  Для prerelease-проверки полного цикла автообновления (issue #89) использовать
  _build_v100123_test1.ps1: пониженная версия 1.0.1.22 передаётся ВСЕМИ ЧЕТЫРЬМЯ
  свойствами (Version/AssemblyVersion/FileVersion/InformationalVersion).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$Root    = $PSScriptRoot
$Version = '1.0.1.23'
$LogFile = Join-Path $Root '_publish_100123.log'
$LinuxLog = Join-Path $Root '_build_linux_100123.log'
$OutDir  = Join-Path $Root ("publish\out-" + $Version)

$WinScript = Join-Path $Root 'Workspace1C\build-windows-single-file.ps1'
$WinExe    = Join-Path $Root 'Workspace1C\dist\win-x64\Workspace1C.exe'
$LinScript = Join-Path $Root 'Workspace1C\build-linux-single-file.ps1'
$LinBin    = Join-Path $Root 'Workspace1C\dist\linux-x64\Workspace1C'

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
            # Issue #89: проверяем ОБЕ версии — FileVersion и ProductVersion
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
    Stamp "=== Publish artifacts v$Version (Windows + Linux), build only ==="
    Stamp "No git add/commit/push, no GitHub release, no Telegram."

    # --- 1. Windows build ------------------------------------------------------
    Stamp "[1/5] Windows: build-windows-single-file.ps1 (WPF, net10.0-windows, win-x64, self-contained single-file)"
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

    # --- 2. Linux build (cross-compile from Windows, ForceLinux=true) -----------
    Stamp "[2/5] Linux: build-linux-single-file.ps1 (Avalonia, net10.0, linux-x64, self-contained single-file, -p:ForceLinux=true)"
    if (-not (Test-Path $LinScript)) {
        $script:Fail = 1
        Stamp "ERROR: script not found: $LinScript"
    } else {
        & $LinScript -Configuration Release *>&1 | Tee-Object -FilePath $LinuxLog
        if ($LASTEXITCODE -ne 0) {
            $script:Fail = 1
            Stamp "ERROR: Linux build failed with exit code $LASTEXITCODE"
        } else {
            Stamp "Linux build OK (exit 0), log: $LinuxLog"
        }
    }

    # --- 3. Verify artifacts ----------------------------------------------------
    Stamp "[3/5] Verify artifacts"
    Stamp "-- Windows (dist) --"
    Show-Artifact -Path $WinExe -ExpectedVersion $Version
    Stamp "-- Linux (dist) --"
    Show-Artifact -Path $LinBin -ExpectedVersion ''

    # --- 4. Copy to publish\out-1.0.1.23 -----------------------------------------
    Stamp "[4/5] Copy artifacts to $OutDir"
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    if (Test-Path $WinExe) { Copy-Item -Force $WinExe (Join-Path $OutDir 'Workspace1C.exe') }
    if (Test-Path $LinBin) { Copy-Item -Force $LinBin (Join-Path $OutDir 'Workspace1C-linux-x64') }
    Stamp "[5/5] Artifacts in $OutDir"
    Get-ChildItem $OutDir | ForEach-Object {
        Stamp ("  " + $_.Name + "  " + $_.Length + " bytes (" + [math]::Round($_.Length / 1MB, 1) + " MB)  SHA-256: " + (Get-FileHash -Algorithm SHA256 $_.FullName).Hash)
    }

    if ($script:Fail -eq 0) {
        Stamp "=== DONE: publish\out-1.0.1.23\Workspace1C.exe + Workspace1C-linux-x64 ==="
    } else {
        Stamp "=== BUILD FAILED (see errors above) ==="
    }
} *>&1 | Tee-Object -FilePath $LogFile

# Windows PowerShell 5.1 пишет Tee-Object в UTF-16: перекодируем лог в UTF-8 (с BOM).
foreach ($logPath in @($LogFile, $LinuxLog)) {
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
