<#
  _build_v100125_test1.ps1 - сборка TEST1-артефактов prerelease v1.0.1.25-test1
  с ПОНИЖЕННОЙ версией 1.0.1.24 (issue #89: в прошлый раз prerelease v1.0.1.24-test1
  7OH «не нашёл», теперь он обновился на стабильный v1.0.1.24 и ждёт следующий релиз —
  сборка с версией 1.0.1.24 получит предложение обновиться на стабильный v1.0.1.25
  сразу после его публикации — путь с фиксом будет пройден).

  Зачем пониженная версия:
    * автообновление сравнивает версии через GitHubReleaseService.IsNewerThan
      (Version.TryParse, 4 части) — сборка с версией 1.0.1.25 НЕ получит предложение
      обновиться на стабильный v1.0.1.25 (версии равны), и полный цикл обновления
      нельзя будет проверить;
    * сборка с версией 1.0.1.24 получит предложение обновиться на стабильный
      v1.0.1.25 сразу после его публикации — путь с фиксом будет пройден.

  Фикс v1.0.1.23-test1 (issue #89, репорт 7OH «Скачал "-test1", но увы
  версию показывает полную 22 и не видит обновления») сохранён:
    * в Workspace1C.csproj версия задана ЧЕТЫРЬМЯ отдельными свойствами, в т.ч.
      ЯВНЫМ InformationalVersion — пониженная версия передаётся ВСЕМИ ЧЕТЫРЬМЯ
      свойствами: -p:Version, -p:AssemblyVersion, -p:FileVersion,
      -p:InformationalVersion;
    * в Show-Artifact проверяется ProductVersion (InformationalVersion),
      а не только FileVersion — mismatch ловится на этапе сборки, а не пользователем;
    * InformationalVersion должна быть чистой «1.0.1.24» (без суффикса «-test1»),
      иначе TryParseVersion не распарсит текущую версию и обновление не будет
      предложено.

  Схема повторяет _build_v100124_test1.ps1 (build-windows-single-file.ps1 /
  build-linux-single-file.ps1), вывод — в отдельные dist-каталоги, чтобы не
  трогать релизные артефакты.

  Результат: publish\out-1.0.1.25-test1\Workspace1C.exe и Workspace1C-linux-x64
  (FileVersion/ProductVersion 1.0.1.24).

  Логи сборки: _publish_100125_test1.log, _build_linux_100125_test1.log
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$Root    = $PSScriptRoot
$Version = '1.0.1.24'   # ПОНИЖЕННАЯ версия для prerelease (см. шапку)
$LogFile = Join-Path $Root '_publish_100125_test1.log'
$LinuxLog = Join-Path $Root '_build_linux_100125_test1.log'
$OutDir  = Join-Path $Root ("publish\out-1.0.1.25-test1")

$Project = Join-Path $Root 'Workspace1C\Workspace1C.csproj'
$WinDist = Join-Path $Root 'Workspace1C\dist\win-x64-test1'
$WinExe  = Join-Path $WinDist 'Workspace1C.exe'
$LinDist = Join-Path $Root 'Workspace1C\dist\linux-x64-test1'
$LinBin  = Join-Path $LinDist 'Workspace1C'

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
            # Issue #89: проверяем ОБЕ версии — FileVersion (FileVersion) и
            # ProductVersion (InformationalVersion). Раньше mismatch ловился
            # пользователем: -p:Version переопределял FileVersion, но явное
            # InformationalVersion в csproj оставалось «полной» версией.
            $fileVer = $fi.VersionInfo.FileVersion
            $prodVer = $fi.VersionInfo.ProductVersion
            Stamp ("  FileVersion: " + $fileVer + " (expected " + $ExpectedVersion + ")")
            if ($fileVer -ne $ExpectedVersion) {
                Stamp "  WARNING: FileVersion mismatch!"
                $script:Fail = 1
            }
            # SDK при сборке из git-репозитория добавляет к InformationalVersion
            # суффикс «+<sha>» (SourceRevisionId); приложение отбрасывает его
            # (VersionInfo.StripHash), поэтому сравниваем базу до «+».
            $prodVerBase = ($prodVer -split '\+')[0]
            Stamp ("  ProductVersion: " + $prodVer + " (expected " + $ExpectedVersion + "*)")
            if ($prodVerBase -ne $ExpectedVersion) {
                Stamp ("  WARNING: ProductVersion (InformationalVersion) mismatch! " +
                      "Пониженная версия передана не всеми 4 свойствами (-p:Version/AssemblyVersion/FileVersion/InformationalVersion)?")
                $script:Fail = 1
            }
        }
        Stamp ("  SHA-256: " + (Get-FileHash -Algorithm SHA256 $Path).Hash)
    } else {
        $script:Fail = 1
        Stamp "ERROR: artifact not found: $Path"
    }
}

function Clear-DistExtra {
    param([string]$Dist, [string]$KeepName)
    Get-ChildItem $Dist -Recurse | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
        if ($_.FullName -eq (Join-Path $Dist $KeepName)) { return }
        if ($_.PSIsContainer) {
            Remove-Item $_.FullName -Recurse -Force
        } else {
            Remove-Item $_.FullName -Force
        }
    }
}

& {
    Stamp "=== Publish TEST1 artifacts v$Version (Windows + Linux), build only ==="
    Stamp "Prerelease v1.0.1.25-test1: пониженная версия для проверки полного цикла автообновления (#89)."
    Stamp "Версия переопределяется ВСЕМИ ЧЕТЫРЬМЯ свойствами: Version, AssemblyVersion, FileVersion, InformationalVersion."
    Stamp "No git add/commit/push, no GitHub release, no Telegram."

    # Общие свойства пониженной версии (issue #89): все 4, включая явное InformationalVersion.
    $VersionProps = @(
        "-p:Version=$Version",
        "-p:AssemblyVersion=$Version",
        "-p:FileVersion=$Version",
        "-p:InformationalVersion=$Version"
    )

    # --- 1. Windows build (WPF, net10.0-windows, win-x64) ----------------------
    Stamp "[1/5] Windows: dotnet publish $($VersionProps -join ' ') (WPF, net10.0-windows, win-x64, self-contained single-file)"
    dotnet publish $Project `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishReadyToRun=false `
        $VersionProps `
        -o $WinDist
    if ($LASTEXITCODE -ne 0) {
        # Известная особенность .NET 10 RC SDK temp-project: первый publish после
        # очистки obj может упасть — повторить один раз.
        Stamp "Windows build failed ($LASTEXITCODE) — retry once"
        dotnet publish $Project `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true `
            -p:PublishReadyToRun=false `
            $VersionProps `
            -o $WinDist
        if ($LASTEXITCODE -ne 0) {
            $script:Fail = 1
            Stamp "ERROR: Windows build failed with exit code $LASTEXITCODE"
        } else {
            Stamp "Windows build OK (exit 0, retry)"
        }
    } else {
        Stamp "Windows build OK (exit 0)"
    }
    if (Test-Path $WinDist) { Clear-DistExtra -Dist $WinDist -KeepName 'Workspace1C.exe' }

    # --- 2. Linux build (cross-compile, ForceLinux=true) ------------------------
    Stamp "[2/5] Linux: dotnet publish $($VersionProps -join ' ') -p:ForceLinux=true (Avalonia, net10.0, linux-x64)"
    dotnet restore $Project -p:ForceLinux=true
    if ($LASTEXITCODE -ne 0) {
        $script:Fail = 1
        Stamp "ERROR: Linux restore failed with exit code $LASTEXITCODE"
    } else {
        dotnet publish $Project `
            -c Release `
            -r linux-x64 `
            --self-contained true `
            -p:ForceLinux=true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true `
            -p:PublishReadyToRun=false `
            $VersionProps `
            -o $LinDist
        if ($LASTEXITCODE -ne 0) {
            $script:Fail = 1
            Stamp "ERROR: Linux build failed with exit code $LASTEXITCODE"
        } else {
            Stamp "Linux build OK (exit 0)"
        }
    }
    if (Test-Path $LinDist) { Clear-DistExtra -Dist $LinDist -KeepName 'Workspace1C' }

    # --- 3. Verify artifacts ----------------------------------------------------
    Stamp "[3/5] Verify artifacts"
    Stamp "-- Windows (dist) --"
    Show-Artifact -Path $WinExe -ExpectedVersion $Version
    Stamp "-- Linux (dist) --"
    Show-Artifact -Path $LinBin -ExpectedVersion ''

    # --- 4. Copy to publish\out-1.0.1.25-test1 ----------------------------------
    Stamp "[4/5] Copy artifacts to $OutDir"
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    if (Test-Path $WinExe) { Copy-Item -Force $WinExe (Join-Path $OutDir 'Workspace1C.exe') }
    if (Test-Path $LinBin) { Copy-Item -Force $LinBin (Join-Path $OutDir 'Workspace1C-linux-x64') }
    Stamp "[5/5] Artifacts in $OutDir"
    Get-ChildItem $OutDir | ForEach-Object {
        Stamp ("  " + $_.Name + "  " + $_.Length + " bytes (" + [math]::Round($_.Length / 1MB, 1) + " MB)  SHA-256: " + (Get-FileHash -Algorithm SHA256 $_.FullName).Hash)
    }

    if ($script:Fail -eq 0) {
        Stamp "=== DONE: publish\out-1.0.1.25-test1\Workspace1C.exe + Workspace1C-linux-x64 (FileVersion/ProductVersion $Version) ==="
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
