<#
  _build_linux_v100149.ps1 - сборка ТОЛЬКО Linux-артефакта релиза 1.0.1.49 БЕЗ git/release.

  Linux-часть по образцу _build_linux_v100148.ps1:
    * Linux: Workspace1C\build-linux-single-file.ps1 (-p:ForceLinux=true)
             -> Workspace1C\dist\linux-x64\Workspace1C
             (Avalonia, net10.0, linux-x64, self-contained single-file,
              кросс-компиляция с Windows, без WSL/docker)

  Результат копируется в publish\out-1.0.1.49\ под именем релизного asset'а
  (как в .github/workflows/release.yml и релизах v1.0.1.27..v1.0.1.48):
    Workspace1C-linux-x64        (linux-x64)

  Лог сборки: _build_linux_100149.log

  Состав артефактов соответствует релизам v1.0.1.27..v1.0.1.48:
    Workspace1C-linux-x64 - AppImage/deb не публикуются
    (пакетные скрипты package/linux требуют Linux-окружение; в прошлых релизах
    AppImage/deb не собирались и не публиковались).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$Root    = $PSScriptRoot
$Version = '1.0.1.49'
$LinuxLog = Join-Path $Root '_build_linux_100149.log'
$OutDir  = Join-Path $Root ("publish\out-" + $Version)

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
    Stamp "=== Publish Linux artifact v$Version (linux-x64), build only ==="
    Stamp "No git add/commit/push, no GitHub release, no Telegram."

    # --- 1. Linux build (cross-compile from Windows, ForceLinux=true) -----------
    Stamp "[1/3] Linux: build-linux-single-file.ps1 (Avalonia, net10.0, linux-x64, self-contained single-file, -p:ForceLinux=true)"
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

    # --- 2. Verify artifact -----------------------------------------------------
    Stamp "[2/3] Verify artifact"
    Stamp "-- Linux (dist) --"
    # ELF-бинарник не имеет NT-версии: как в _build_linux_v100148.ps1 проверяем без ExpectedVersion,
    # а версию берём из промежуточной Workspace1C.dll (bin\Release\net10.0\linux-x64).
    Show-Artifact -Path $LinBin -ExpectedVersion ''
    $MidDll = Join-Path $Root 'Workspace1C\bin\Release\net10.0\linux-x64\Workspace1C.dll'
    if (Test-Path $MidDll) {
        $dllVer = (Get-Item $MidDll).VersionInfo.FileVersion
        $dllProdBase = ((Get-Item $MidDll).VersionInfo.ProductVersion -split '\+')[0]
        Stamp ("  Intermediate DLL FileVersion: " + $dllVer + " (expected " + $Version + ")")
        if ($dllVer -ne $Version) { Stamp "  WARNING: intermediate DLL FileVersion mismatch!"; $script:Fail = 1 }
        Stamp ("  Intermediate DLL ProductVersion: " + $dllProdBase + " (expected " + $Version + "*)")
        if ($dllProdBase -ne $Version) { Stamp "  WARNING: intermediate DLL ProductVersion mismatch!"; $script:Fail = 1 }
    } else {
        Stamp "  WARNING: intermediate DLL not found: $MidDll (version not verified)"
    }

    # --- 3. Copy to publish\out-1.0.1.49 -----------------------------------------
    Stamp "[3/3] Copy artifact to $OutDir"
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    if (Test-Path $LinBin) { Copy-Item -Force $LinBin (Join-Path $OutDir 'Workspace1C-linux-x64') }
    Stamp ("Artifacts in " + $OutDir + ":")
    Get-ChildItem $OutDir | ForEach-Object {
        Stamp ("  " + $_.Name + "  " + $_.Length + " bytes (" + [math]::Round($_.Length / 1MB, 1) + " MB)  SHA-256: " + (Get-FileHash -Algorithm SHA256 $_.FullName).Hash)
    }

    if ($script:Fail -eq 0) {
        Stamp ("=== DONE: " + $OutDir + "\Workspace1C-linux-x64 ===")
    } else {
        Stamp "=== BUILD FAILED (see errors above) ==="
    }
} *>&1 | Out-Null

# Windows PowerShell 5.1 пишет Tee-Object в UTF-16: перекодируем лог в UTF-8 (с BOM).
foreach ($logPath in @($LinuxLog)) {
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
