<#
.SYNOPSIS
  Сборка ОДНОГО исполняемого файла для Windows (WPF, net10.0-windows, win-x64).

.DESCRIPTION
  Результат: self-contained single-file исполняемый файл
    dist/win-x64/Workspace1C.exe
  (без папок, без .dll и .pdb рядом — только один исполняемый файл).

  Требуется: Windows + .NET SDK 10 (>= 10.0.400).

.USAGE
  .\build-windows-single-file.ps1                # Release, RID win-x64 (с модулями ПРО)
  .\build-windows-single-file.ps1 -Configuration Debug   # другой конфиг
  .\build-windows-single-file.ps1 -RID win-arm64         # другой RID
  .\build-windows-single-file.ps1 -AllowCommunity        # публичная сборка без ПРО

  Основная программа выпускается С модулями ПРО: они вкомпилированы в бинарь, а
  доступ к функциям 2.x закрывает лицензия (активация ключа — без перезапуска).
  Поэтому модули обязательны: без клона Workspace1C.Pro сборка падает.
  Публичную редакцию из открытых исходников собирают явно с -AllowCommunity.

  SKIP_PUBLISH=1 (переменная окружения) пропускает dotnet publish
  (удобно для быстрой проверки синтаксиса).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$RID = 'win-x64',
    # Явно разрешить публичную сборку без модулей ПРО (по умолчанию модули обязательны).
    [switch]$AllowCommunity
)

$ErrorActionPreference = 'Stop'

$Root       = $PSScriptRoot
$Project    = Join-Path $Root 'Workspace1C.csproj'
$Dist       = Join-Path $Root "dist\$RID"

# На Windows выбирается TFM net10.0-windows (WPF), поэтому проверяем ОС.
if ($env:OS -ne 'Windows_NT') {
    Write-Host "!! Скрипт предназначен для запуска НА Windows (TFM net10.0-windows/WPF задаётся по ОС)." -ForegroundColor Yellow
    Write-Host "   На другой ОС кросс-компиляция даст Linux/Avalonia и не запустится на Windows." -ForegroundColor Yellow
    exit 1
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "!! .NET SDK не найден. Установите .NET SDK 10." -ForegroundColor Red
    exit 1
}

# Модули ПРО обязательны для основной программы; публичная сборка — только явно.
$requirePro = -not $AllowCommunity
# Публичная редакция: модули ПРО не включаются, даже если клон найден.
$buildProps = @()
if ($AllowCommunity) { $buildProps += '-p:CommunityEdition=true' }

Write-Host "==> Конфигурация: $Configuration" -ForegroundColor Cyan
Write-Host "==> RID:          $RID" -ForegroundColor Cyan
Write-Host "==> Цель:         $Dist" -ForegroundColor Cyan
Write-Host "==> Редакция:     $(if ($requirePro) { 'ПРО (модули обязательны)' } else { 'публичная (-AllowCommunity)' })" -ForegroundColor Cyan

Write-Host "==> Restore" -ForegroundColor Cyan
dotnet restore $Project
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Модули ПРО: csproj сам ищет клон Workspace1C.Pro (явный путь — ProSourceDir
# в переменной окружения или -p:ProSourceDir=<путь к src\>). Проверка — ДО publish.
$hasProProperty = (& dotnet msbuild $Project -nologo -getProperty:HasProModules @buildProps 2>$null) -match 'true'
if ($hasProProperty) {
    Write-Host "==> Модули ПРО: найдены (PRO_MODULES) — доступ откроет лицензия" -ForegroundColor Green
} else {
    Write-Host "==> Модули ПРО: НЕ найдены — функции 2.x в бинарь не войдут" -ForegroundColor Yellow
    Write-Host "    Укажите клон: `$env:ProSourceDir='<путь к Workspace1C.Pro\src\>' или -p:ProSourceDir=..." -ForegroundColor Yellow
    if ($requirePro) {
        Write-Host "!! Основная программа собирается с модулями ПРО: сборка прервана до publish." -ForegroundColor Red
        Write-Host "   Публичная редакция без ПРО — только явно: -AllowCommunity" -ForegroundColor Red
        exit 1
    }
}
if (-not $requirePro) {
    Write-Host "!! ВНИМАНИЕ: -AllowCommunity — собирается публичная редакция без функций 2.x." -ForegroundColor Yellow
}

if ($env:SKIP_PUBLISH -eq '1') {
    Write-Host "==> SKIP_PUBLISH=1 — публикация пропущена." -ForegroundColor Yellow
    exit 0
}

Write-Host "==> Publish (self-contained single-file)" -ForegroundColor Cyan
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
dotnet publish $Project `
    -c $Configuration `
    -r $RID `
    --self-contained true `
    @buildProps `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=false `
    -o $Dist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Оставляем ТОЛЬКО исполняемый файл Workspace1C.exe:
# удаляем .pdb, .json, .xml и любые другие файлы и папки в выходной папке.
# Встроенные языки (ru, en) уже в ресурсах сборки, поэтому внешняя папка
# Localization/Languages здесь не нужна (см. LocalizationManager.cs).
Get-ChildItem $Dist -Recurse | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
    if ($_.FullName -eq (Join-Path $Dist 'Workspace1C.exe')) { return }
    if ($_.PSIsContainer) {
        Write-Host "    (удаляем лишнюю папку: $($_.FullName.Substring($Dist.Length)))" -ForegroundColor DarkGray
        Remove-Item $_.FullName -Recurse -Force
    } else {
        Write-Host "    (удаляем лишний файл: $($_.FullName.Substring($Dist.Length)))" -ForegroundColor DarkGray
        Remove-Item $_.FullName -Force
    }
}

$exe = Join-Path $Dist 'Workspace1C.exe'
if (-not (Test-Path $exe)) {
    Write-Host "!! Исполняемый файл не найден: $exe" -ForegroundColor Red
    exit 1
}

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "==> Собран один исполняемый файл:" -ForegroundColor Green
Write-Host "    $exe ($size MB)" -ForegroundColor Green

# Проверка по самому бинарю: proModules=true — код 2.x действительно вошёл
# в сборку, и лицензия откроет функции сразу после активации.
$proModules = $hasProProperty
if ($RID -eq 'win-x64') {
    $tmpOut = [System.IO.Path]::GetTempFileName()
    try {
        # WPF-приложение — GUI-подсистема: обычный `& exe` не отдаёт stdout,
        # поэтому читаем вывод через RedirectStandardOutput.
        $proc = Start-Process -FilePath $exe -ArgumentList '--license-info', '--json' `
            -NoNewWindow -Wait -PassThru -RedirectStandardOutput $tmpOut
        if ($proc.ExitCode -eq 0) {
            $json = Get-Content $tmpOut -Raw
            if (-not [string]::IsNullOrWhiteSpace($json)) {
                $proModules = [bool](($json | ConvertFrom-Json).data.proModules)
            }
        }
    } catch {
        Write-Host "!! Не удалось прочитать --license-info: $($_.Exception.Message)" -ForegroundColor Yellow
    } finally {
        Remove-Item $tmpOut -Force -ErrorAction SilentlyContinue
    }
}
if ($proModules) {
    Write-Host "==> proModules=true: функции 2.x в бинаре и открываются лицензией (major=1)." -ForegroundColor Green
} else {
    Write-Host "==> proModules=false: это публичная сборка, функции 2.x в ней отсутствуют." -ForegroundColor Yellow
    if ($requirePro) {
        Write-Host "!! Бинарь без модулей ПРО — основная программа так выпускаться не должна." -ForegroundColor Red
        exit 1
    }
}
Write-Host "==> Готово. Запуск: $exe" -ForegroundColor Green