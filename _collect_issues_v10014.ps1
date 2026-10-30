# Сбор всех открытых issues репозитория sivatorov/Workspace1C для цикла 1.0.1.4/1.0.1.5
# Результат: _issues_full_v10014.json (полный структурированный дамп: issue + комментарии)
# Адаптация _collect_issues.ps1 — новые имена выходных файлов, чтобы не перезаписывать старые.
# Локальный рабочий скрипт, не для коммита (префикс _).
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repo = 'sivatorov/Workspace1C'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$jsonOut = Join-Path $root '_issues_full_v10014.json'
$mdOut   = Join-Path $root '_issues_summary_v10014.md'

# gh api отдаёт JSON одной строкой/массивом строк; конвейерная передача строк в
# ConvertFrom-Json в PS 5.1 ломает массивы. Поэтому всегда склеиваем через Out-String.
function Get-GhJson {
    param([string]$Url)
    $raw = gh api $Url 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($raw | Out-String) }
    return (($raw | Out-String) | ConvertFrom-Json)
}

Write-Host '== Шаг 1. Список открытых issues =='
# state=open; per_page=100 — максимум для issues endpoint. При необходимости
# дописывается пагинация (page=2..N), если вернётся ровно 100 записей.
$all = @()
$page = 1
do {
    $issues = Get-GhJson "repos/$repo/issues?state=open&per_page=100&page=$page"
    if ($null -eq $issues) { break }
    Write-Host ("  страница {0}: записей {1}" -f $page, @($issues).Count)
    $all += @($issues)
    $page++
} while (@($issues).Count -eq 100)

# Отбрасываем pull-request'ы (issues endpoint возвращает и PR вместе с issues).
$realIssues = @($all | Where-Object { -not $_.pull_request })
Write-Host ("Всего записей: {0}; открытых issues (без PR): {1}" -f @($all).Count, $realIssues.Count)

$errors = @()
$result = @()

foreach ($issue in $realIssues) {
    $num = [int]$issue.number
    Write-Host ("  #[{0}] {1}" -f $num, $issue.title)
    try {
        $full = Get-GhJson "repos/$repo/issues/$num"

        $rawComments = Get-GhJson "repos/$repo/issues/$num/comments?per_page=100"
        if ($null -eq $rawComments) { $rawComments = @() }
        $comments = @($rawComments)

        $commentItems = @()
        foreach ($c in $comments) {
            $commentItems += [pscustomobject]@{
                id                = $c.id
                authorLogin       = $c.user.login
                authorAssociation = $c.author_association
                createdAt         = $c.created_at
                updatedAt         = $c.updated_at
                body              = $c.body
                url               = $c.html_url
            }
        }

        $result += [pscustomobject]@{
            number         = $num
            title          = $full.title
            state          = $full.state
            createdAt      = $full.created_at
            updatedAt      = $full.updated_at
            authorLogin    = $full.user.login
            labels         = @($full.labels | ForEach-Object { $_.name })
            body           = $full.body
            url            = $full.html_url
            commentsCount  = $commentItems.Count
            comments       = $commentItems
        }
    } catch {
        $errors += "[#$num] $_"
    }
}

$result = @($result | Sort-Object { [int]$_.number })

$collectedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$dump = [pscustomobject]@{
    repo        = $repo
    collectedAt = $collectedAt
    totalOpen   = $result.Count
    issues      = $result
}

$json = $dump | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($jsonOut, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("OK: {0}" -f $jsonOut)

if ($errors.Count -gt 0) {
    Write-Host 'ВНИМАНИЕ (ошибки при сборе):'
    $errors | ForEach-Object { Write-Host "  $_" }
}

# ---- Шаг 2. Сводка .md ----
function Get-Flat([string]$s, [int]$maxLen) {
    if ($null -eq $s) { return '' }
    $t = $s -replace '\r\n', ' '
    $t = $t -replace '\n', ' '
    $t = $t -replace '\t', ' '
    $t = $t.Trim()
    if ($t.Length -gt $maxLen) { $t = $t.Substring(0, $maxLen) + '…' }
    return $t
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# Сводка открытых issues — sivatorov/Workspace1C")
$lines.Add("")
$lines.Add("- Собрано: $collectedAt (UTC)")
$lines.Add("- Открыто issues: $($result.Count)")
$lines.Add("")

foreach ($it in $result) {
    $num = $it.number
    $title = $it.title
    $created = $it.createdAt
    $updated = $it.updatedAt
    $cc = [int]$it.commentsCount
    $lastComment = $null
    if ($cc -gt 0) { $lastComment = $it.comments[$cc - 1] }

    $labelsStr = ''
    if ($it.labels -and $it.labels.Count -gt 0) {
        $labelsStr = ' Метки: ' + (($it.labels | ForEach-Object { "`"$_`"" }) -join ', ')
    }

    $lines.Add("## #$num — $title (автор @$($it.authorLogin))")
    $lines.Add("")
    $lines.Add("- Создан: $created; обновлён: $updated$labelsStr")
    if ($cc -eq 0) {
        $lines.Add("- Комментариев: **0**")
    } else {
        $lcAuthor = $lastComment.authorLogin
        $lcDate = $lastComment.createdAt
        $lines.Add("- Комментариев: **$cc**, последний — **@$lcAuthor** ($lcDate)")
    }
    $lines.Add("")
    $essence = Get-Flat ([string]$it.body) 250
    if ($essence -eq '') { $essence = '(описание отсутствует)' }
    $lines.Add("Суть: $essence")
    $lines.Add("")
    if ($cc -gt 0) {
        $lcText = Get-Flat ([string]$lastComment.body) 250
        $lines.Add("Последний комментарий (@$($lastComment.authorLogin), $($lastComment.createdAt)): $lcText")
    } else {
        $lines.Add("Последний комментарий: нет")
    }
    $lines.Add("")
}

$md = ($lines -join "`r`n")
[System.IO.File]::WriteAllText($mdOut, $md, (New-Object System.Text.UTF8Encoding($false)))
Write-Host ("OK: {0}" -f $mdOut)
Write-Host 'Готово.'
