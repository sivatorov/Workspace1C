<#
  _build_v100143.ps1 - СЃР±РѕСЂРєР° Р°СЂС‚РµС„Р°РєС‚РѕРІ СЂРµР»РёР·Р° 1.0.1.43 (Windows + Linux) Р‘Р•Р— git/release.

  РЎС…РµРјР° РїРѕРІС‚РѕСЂСЏРµС‚ РїСЂРѕС€Р»С‹Р№ С†РёРєР» РїСѓР±Р»РёРєР°С†РёРё (_build_v100142.ps1 / CI release.yml):
    * Windows: Workspace1C\build-windows-single-file.ps1
               -> Workspace1C\dist\win-x64\Workspace1C.exe
               (WPF, net10.0-windows, win-x64, self-contained single-file)
    * Linux:   Workspace1C\build-linux-single-file.ps1  (-p:ForceLinux=true)
               -> Workspace1C\dist\linux-x64\Workspace1C
               (Avalonia, net10.0, linux-x64, self-contained single-file)

  Р РµР·СѓР»СЊС‚Р°С‚ РєРѕРїРёСЂСѓРµС‚СЃСЏ РІ publish\out-1.0.1.43\ РїРѕРґ РёРјРµРЅР°РјРё СЂРµР»РёР·РЅС‹С… asset'РѕРІ
  (РєР°Рє РІ .github/workflows/release.yml Рё СЂРµР»РёР·Р°С… v1.0.1.27..v1.0.1.42):
    Workspace1C.exe              (win-x64)
    Workspace1C-linux-x64        (linux-x64)

  Р›РѕРіРё СЃР±РѕСЂРєРё: _publish_100143.log, _build_linux_100143.log

  РџСЂРёРјРµС‡Р°РЅРёРµ: РїРµСЂРІР°СЏ СЃР±РѕСЂРєР° WPF РїРѕСЃР»Рµ РѕС‡РёСЃС‚РєРё obj РјРѕР¶РµС‚ СѓРїР°СЃС‚СЊ (РёР·РІРµСЃС‚РЅР°СЏ РѕСЃРѕР±РµРЅРЅРѕСЃС‚СЊ
  .NET 10 RC SDK temp-project) - РїСЂРё РїР°РґРµРЅРёРё РїСЂРѕСЃС‚Рѕ РїРѕРІС‚РѕСЂРёС‚СЊ РїСЂРѕРіРѕРЅ СЃРєСЂРёРїС‚Р°.

  РЎРѕСЃС‚Р°РІ Р°СЂС‚РµС„Р°РєС‚РѕРІ СЃРѕРѕС‚РІРµС‚СЃС‚РІСѓРµС‚ СЂРµР»РёР·Р°Рј v1.0.1.27..v1.0.1.42:
    Workspace1C.exe, Workspace1C-linux-x64 - AppImage/deb РЅРµ РїСѓР±Р»РёРєСѓСЋС‚СЃСЏ
    (РїР°РєРµС‚РЅС‹Рµ СЃРєСЂРёРїС‚С‹ package/linux С‚СЂРµР±СѓСЋС‚ Linux-РѕРєСЂСѓР¶РµРЅРёРµ; РІ РїСЂРѕС€Р»С‹С… СЂРµР»РёР·Р°С…
    AppImage/deb РЅРµ СЃРѕР±РёСЂР°Р»РёСЃСЊ Рё РЅРµ РїСѓР±Р»РёРєРѕРІР°Р»РёСЃСЊ).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$Root    = $PSScriptRoot
$Version = '1.0.1.43'
$LogFile = Join-Path $Root '_publish_100143.log'
$LinuxLog = Join-Path $Root '_build_linux_100143.log'
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

function Show-Artifact {
    param([string]$Path, [string]$ExpectedVersion)
    if (Test-Path $Path) {
        $fi = Get-Item $Path
        Stamp ("Artifact: " + $fi.FullName)
        Stamp ("  size: " + $fi.Length + " bytes (" + [math]::Round($fi.Length / 1MB, 1) + " MB)")
        if ($ExpectedVersion -ne '') {
            # Issue #89: РїСЂРѕРІРµСЂСЏРµРј РћР‘Р• РІРµСЂСЃРёРё - FileVersion Рё ProductVersion
            # (InformationalVersion), С‡С‚РѕР±С‹ СЂР°СЃС…РѕР¶РґРµРЅРёРµ Р»РѕРІРёР»РѕСЃСЊ РЅР° СЌС‚Р°РїРµ СЃР±РѕСЂРєРё.
            # РЎСѓС„С„РёРєСЃ В«+<sha>В» РІ InformationalVersion .NET SDK РґРѕР±Р°РІР»СЏРµС‚ Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё
            # РїСЂРё СЃР±РѕСЂРєРµ РёР· git-СЂРµРїРѕР·РёС‚РѕСЂРёСЏ (SourceRevisionId); РїСЂРёР»РѕР¶РµРЅРёРµ СЃР°РјРѕ
            # РѕС‚Р±СЂР°СЃС‹РІР°РµС‚ РµРіРѕ (VersionInfo.StripHash), РїРѕСЌС‚РѕРјСѓ СЃСЂР°РІРЅРёРІР°РµРј Р±Р°Р·Сѓ РґРѕ В«+В».
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

    # --- 4. Copy to publish\out-1.0.1.43 -----------------------------------------
    Stamp "[4/5] Copy artifacts to $OutDir"
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    if (Test-Path $WinExe) { Copy-Item -Force $WinExe (Join-Path $OutDir 'Workspace1C.exe') }
    if (Test-Path $LinBin) { Copy-Item -Force $LinBin (Join-Path $OutDir 'Workspace1C-linux-x64') }
    Stamp "[5/5] Artifacts in $OutDir"
    Get-ChildItem $OutDir | ForEach-Object {
        Stamp ("  " + $_.Name + "  " + $_.Length + " bytes (" + [math]::Round($_.Length / 1MB, 1) + " MB)  SHA-256: " + (Get-FileHash -Algorithm SHA256 $_.FullName).Hash)
    }

    if ($script:Fail -eq 0) {
        Stamp "=== DONE: publish\out-1.0.1.43\Workspace1C.exe + Workspace1C-linux-x64 ==="
    } else {
        Stamp "=== BUILD FAILED (see errors above) ==="
    }
} *>&1 | Tee-Object -FilePath $LogFile

# Windows PowerShell 5.1 РїРёС€РµС‚ Tee-Object РІ UTF-16: РїРµСЂРµРєРѕРґРёСЂСѓРµРј Р»РѕРі РІ UTF-8 (СЃ BOM).
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
