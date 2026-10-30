# Построение _issues_analysis_v10014.md из _issues_full_v10014.json
# Критерии отбора:
#   1) комментариев нет                          -> ОТОБРАН (работа по описанию)
#   2) последний комментарий НЕ от sivatorov      -> ОТОБРАН (ориентир - последние комментарии пользователей)
#   3) последний комментарий от sivatorov         -> ПРОПУЩЕН (считается обработанным)
# Локальный рабочий скрипт, не для коммита (префикс _).
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$jsonIn = Join-Path $root '_issues_full_v10014.json'
$mdOut  = Join-Path $root '_issues_analysis_v10014.md'

$repoUrl = 'https://github.com/sivatorov/Workspace1C'
$owner = 'sivatorov'

$d = Get-Content -Raw -Encoding UTF8 $jsonIn | ConvertFrom-Json
$issues = @($d.issues | Sort-Object { [int]$_.number })

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# Анализ открытых issues — sivatorov/Workspace1C")
$lines.Add("")
$lines.Add("Цикл разработки после релиза **1.0.1.4** (текущая версия программы).")
$lines.Add("- Собрано: $($d.collectedAt) (UTC)")
$lines.Add("- Всего открыто issues: $($d.totalOpen)")
$lines.Add("- Репозиторий: $repoUrl")
$lines.Add("")
$lines.Add("## Критерии отбора")
$lines.Add("")
$lines.Add("1. Issues **без комментариев** — отобраны (изменения на основе описания).")
$lines.Add("2. Issues, где **последний комментарий не от @$owner** — отобраны (ориентир — последние комментарии не от автора).")
$lines.Add("3. Issues, где **последний комментарий от @$owner** — пропущены (считаются уже обработанными).")
$lines.Add("")

# ---- Полный список с пометками ----
$lines.Add("## Полный список открытых issues")
$lines.Add("")
$lines.Add("| # | Issue | Автор | Комм. | Последний комментарий | Отобран? |")
$lines.Add("|---|-------|-------|-------|------------------------|----------|")

$selected = [System.Collections.Generic.List[object]]::new()
$reasons  = @{}

foreach ($it in $issues) {
    $num = [int]$it.number
    $cc = [int]$it.commentsCount
    $last = $null
    if ($cc -gt 0) { $last = $it.comments[$cc - 1] }

    if ($cc -eq 0) {
        $selectedFlag = 'ДА'
        $reason = 'Без комментариев — работа по описанию'
        $selected.Add($it)
    } elseif ($last.authorLogin -eq $owner) {
        $selectedFlag = 'нет'
        $reason = "Последний комментарий от @$owner ($($last.createdAt)) — уже обработан"
    } else {
        $selectedFlag = 'ДА'
        $reason = "Последний комментарий от @$($last.authorLogin) ($($last.createdAt)) — не от @$owner"
        $selected.Add($it)
    }
    $reasons[$num] = $reason

    $lc = ''
    if ($null -ne $last) { $lc = "@$($last.authorLogin), $($last.createdAt)" } else { $lc = '—' }
    $titleCell = ($it.title -replace '\|', '/')
    $lines.Add("| #$num | [$titleCell]($($it.url)) | @$($it.authorLogin) | $cc | $lc | $selectedFlag |")
}
$lines.Add("")
$lines.Add("Отобрано для дальнейшей работы: **$($selected.Count)** из $($issues.Count).")
$lines.Add("")

# ---- Детальные секции по отобранным ----
$lines.Add("---")
$lines.Add("")
$lines.Add("## Детально: отобранные issues")
$lines.Add("")

function Add-Section {
    param($it)
    $num = [int]$it.number
    $lines.Add("### #$num — $($it.title)")
    $lines.Add("")
    $lines.Add("- **Ссылка:** $($it.url)")
    $lines.Add("- **Автор:** @$($it.authorLogin) (создано $($it.createdAt), обновлено $($it.updatedAt))")
    $labels = @($it.labels)
    if ($labels.Count -gt 0) { $lines.Add("- **Метки:** " + (($labels | ForEach-Object { '"' + $_ + '"' }) -join ', ')) }
    $lines.Add("- **Обоснование отбора:** $($reasons[[int]$num])")
    $lines.Add("")
    $lines.Add("**Описание:**")
    $lines.Add("")
    $body = [string]$it.body
    if ($body.Trim() -eq '') { $body = '(описание отсутствует)' }
    foreach ($bl in ($body -split "`r?`n")) { $lines.Add($bl) }
    $lines.Add("")
    $cc = [int]$it.commentsCount
    if ($cc -eq 0) {
        $lines.Add("**Комментарии:** нет.")
        $lines.Add("")
    } else {
        $lines.Add("**Комментарии ($cc):**")
        $lines.Add("")
        $idx = 0
        foreach ($c in $it.comments) {
            $idx++
            $lines.Add("$idx. **@$($c.authorLogin)** — $($c.createdAt) — [$($c.url)]($($c.url))")
            $lines.Add("")
            foreach ($bl in ([string]$c.body -split "`r?`n")) { $lines.Add($bl) }
            $lines.Add("")
        }
    }
    $lines.Add("---")
    $lines.Add("")
}

foreach ($it in ($selected | Sort-Object { [int]$_.number })) {
    Add-Section -it $it
}

$md = ($lines -join "`r`n")
[System.IO.File]::WriteAllText($mdOut, $md, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("OK: {0}" -f $mdOut)
Write-Host ("Отобрано: {0}" -f $selected.Count)