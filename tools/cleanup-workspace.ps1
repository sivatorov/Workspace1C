<#
.SYNOPSIS
    Очистка рабочего каталога Workspace1C от служебного мусора разработки.

.DESCRIPTION
    Ищет в корне репозитория временные файлы сборок, комментарии к issues, логи,
    старые выкладки publish/ и т.п. и по умолчанию ПЕРЕНОСИТ их в
    .work\_to_delete\<дата-время>\ (ничего не удаляется безвозвратно).
    С ключом -Delete удаляет сразу. С ключом -WhatIf только показывает список.

    Исходный код (Workspace1C\, Workspace1C.Tests\), документация, design\,
    package\, tools\ и .git не затрагиваются.

.EXAMPLE
    .\tools\cleanup-workspace.ps1 -WhatIf      # посмотреть, что будет перенесено
    .\tools\cleanup-workspace.ps1              # перенести в .work\_to_delete\...
    .\tools\cleanup-workspace.ps1 -Delete      # удалить безвозвратно
    .\tools\cleanup-workspace.ps1 -KeepPublish 2   # оставить 2 последние выкладки в publish\
#>
[CmdletBinding()]
param(
    [switch]$Delete,
    [switch]$WhatIf,
    [int]$KeepPublish = 0
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path (Join-Path $root 'Workspace1C.slnx'))) {
    throw "Скрипт должен лежать в tools\ репозитория Workspace1C (не найден Workspace1C.slnx в $root)."
}

# Шаблоны в КОРНЕ репозитория (не рекурсивно).
$rootFilePatterns = @(
    '_*', '.tmp_*', '$*', '*.log', '*_out.txt', 'release_notes_*.md',
    'notes.md', 'diag_issue*.md', 'browser_issue*.png', '27_conf.zip',
    'plan-*.md', 'ui-audit-report-*.md', 'donat.png', 'copyright'
)
$rootDirs = @(
    '.tmp', '_analysis', '_27conf_extracted', 'plans', 'screenshots',
    'TestResults', 'release-artifacts', 'Workspace1C\publish'
)

$items = New-Object System.Collections.Generic.List[System.IO.FileSystemInfo]
foreach ($p in $rootFilePatterns) {
    Get-ChildItem -LiteralPath $root -Force -File -Filter $p -ErrorAction SilentlyContinue |
        ForEach-Object { if (-not $items.Contains($_)) { $items.Add($_) } }
    Get-ChildItem -LiteralPath $root -Force -Directory -Filter $p -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin @('.git', '.github', '.work') } |
        ForEach-Object { if (-not $items.Contains($_)) { $items.Add($_) } }
}
foreach ($d in $rootDirs) {
    $full = Join-Path $root $d
    if (Test-Path -LiteralPath $full) { $items.Add((Get-Item -LiteralPath $full -Force)) }
}

# publish\: всё, кроме $KeepPublish самых свежих подкаталогов/файлов.
$publish = Join-Path $root 'publish'
if (Test-Path -LiteralPath $publish) {
    Get-ChildItem -LiteralPath $publish -Force |
        Sort-Object LastWriteTime -Descending |
        Select-Object -Skip $KeepPublish |
        ForEach-Object { $items.Add($_) }
}

# Отдельные tracked-файлы, которые git сам удалит после слияния ветки, здесь
# тоже попадут в список — это безопасно: при переносе они вернутся через git pull.
$items = $items | Sort-Object FullName -Unique
if (-not $items) { Write-Host 'Мусора не найдено.'; return }

function Get-Size($fsi) {
    if ($fsi -is [System.IO.DirectoryInfo]) {
        (Get-ChildItem -LiteralPath $fsi.FullName -Recurse -Force -File -ErrorAction SilentlyContinue |
            Measure-Object Length -Sum).Sum
    } else { $fsi.Length }
}

$total = 0
foreach ($i in $items) { $total += [int64](Get-Size $i) }
Write-Host ("Найдено объектов: {0}, объём: {1:N1} МБ" -f $items.Count, ($total / 1MB))

if ($WhatIf) {
    $items | ForEach-Object { Write-Host ('  ' + $_.FullName.Substring($root.Length + 1)) }
    return
}

if ($Delete) {
    foreach ($i in $items) { Remove-Item -LiteralPath $i.FullName -Recurse -Force }
    Write-Host 'Удалено.'
} else {
    $dest = Join-Path $root ('.work\_to_delete\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $dest -Force | Out-Null
    foreach ($i in $items) {
        $rel = $i.FullName.Substring($root.Length + 1)
        $target = Join-Path $dest $rel
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Move-Item -LiteralPath $i.FullName -Destination $target -Force
    }
    Write-Host "Перенесено в $dest"
    Write-Host 'Проверьте, что всё собирается, и удалите каталог .work\_to_delete вручную.'
}
