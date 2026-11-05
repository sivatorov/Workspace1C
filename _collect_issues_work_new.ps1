# Сбор и анализ открытых issues репозитория sivatorov/Workspace1C
# Результат: _issues_to_work_new.md (issues для работы: (a) без комментариев,
# (b) последний комментарий НЕ от sivatorov) и _issues_open_new.json (сырые данные)
$ErrorActionPreference = 'Stop'
$Repo = 'sivatorov/Workspace1C'
$OutMd   = Join-Path $PSScriptRoot '_issues_to_work_new.md'
$OutJson = Join-Path $PSScriptRoot '_issues_open_new.json'
$Owner = 'sivatorov'

Write-Host "Получение списка открытых issues..."
$issues = gh issue list -R $Repo --state open --limit 200 --json number,title,body,createdAt,updatedAt,comments,author,labels | ConvertFrom-Json
Write-Host ("Найдено открытых issues: {0}" -f $issues.Count)

$analyzed = @()
$i = 0
foreach ($iss in $issues) {
    $i++
    $n = $iss.number
    Write-Host ("  [{0}/{1}] issue #{2}: получение комментариев..." -f $i, $issues.Count, $n)
    $comments = @()
    $page = 1
    while ($true) {
        $pageOut = gh api "repos/$Repo/issues/$n/comments?per_page=100&page=$page" 2>$null | Out-String
        $parsed = $pageOut | ConvertFrom-Json
        $batch = @($parsed)
        if ($batch.Count -eq 1 -and $batch[0] -is [System.Array]) { $batch = @($batch[0]) }
        if ($batch.Count -eq 0) { break }
        $comments += $batch
        if ($batch.Count -lt 100) { break }
        $page++
    }
    $lastAuthor = $null
    if ($comments.Count -gt 0) { $lastAuthor = $comments[-1].user.login }
    $needsWork = ($comments.Count -eq 0) -or ($lastAuthor -ne $Owner)
    if ($comments.Count -eq 0) { $reason = 'нет комментариев' }
    elseif ($needsWork) { $reason = "последний комментарий от '$lastAuthor' (не $Owner)" }
    else { $reason = 'отработан (последний комментарий от sivatorov)' }

    $commentsForJson = @($comments | ForEach-Object {
        [pscustomobject]@{
            author    = $_.user.login
            createdAt = $_.created_at
            body      = $_.body
        }
    })

    $analyzed += [pscustomobject]@{
        Number             = $n
        Title              = $iss.title
        Body               = $iss.body
        CreatedAt          = $iss.createdAt
        UpdatedAt          = $iss.updatedAt
        Author             = $iss.author.login
        Labels             = @(($iss.labels | ForEach-Object { $_.name }))
        CommentCount       = $comments.Count
        LastCommentAuthor  = $lastAuthor
        NeedsWork          = $needsWork
        Reason             = $reason
        Comments           = $commentsForJson
    }
}

# --- Версия проекта ---
$csproj = Join-Path $PSScriptRoot 'Workspace1C/Workspace1C.csproj'
$versionLine = (Select-String -Path $csproj -Pattern '<Version>(.*?)</Version>' | Select-Object -First 1).Matches[0].Groups[1].Value
$asmVersion  = (Select-String -Path $csproj -Pattern '<AssemblyVersion>(.*?)</AssemblyVersion>' | Select-Object -First 1).Matches[0].Groups[1].Value
$fileVersion = (Select-String -Path $csproj -Pattern '<FileVersion>(.*?)</FileVersion>' | Select-Object -First 1).Matches[0].Groups[1].Value

# --- CHANGELOG: последняя запись ---
$changelogPath = Join-Path $PSScriptRoot 'CHANGELOG.md'
$changelogEntry = 'не найден'
if (-not (Test-Path $changelogPath)) { $changelogPath = Join-Path $PSScriptRoot 'docs/CHANGELOG.md' }
if (-not (Test-Path $changelogPath)) { $changelogPath = Join-Path $PSScriptRoot 'Workspace1C/CHANGELOG.md' }
if (Test-Path $changelogPath) {
    $firstHeading = Select-String -Path $changelogPath -Pattern '^##\s' | Select-Object -First 1
    if ($firstHeading) { $changelogEntry = $firstHeading.Line.Trim() }
}

# --- Последний релиз: git tag и gh release ---
$lastTag = git describe --tags --abbrev=0 2>$null
$releaseInfo = $null
try { $releaseInfo = gh release view -R $Repo --json tagName,publishedAt | ConvertFrom-Json } catch {}

$needWork = @($analyzed | Where-Object { $_.NeedsWork })
$doneWork = @($analyzed | Where-Object { -not $_.NeedsWork })

# --- JSON: сырые данные ---
$versionInfo = [pscustomobject]@{
    csprojVersion     = $versionLine
    assemblyVersion   = $asmVersion
    fileVersion       = $fileVersion
    changelogPath     = (Resolve-Path $changelogPath -ErrorAction SilentlyContinue).Path
    changelogEntry    = $changelogEntry
    lastGitTag        = $lastTag
    lastReleaseTag    = $releaseInfo.tagName
    lastReleaseDate   = $releaseInfo.publishedAt
}
$jsonPayload = [pscustomobject]@{
    collectedAt  = (Get-Date).ToString('o')
    repo         = $Repo
    versionInfo  = $versionInfo
    totalOpen    = $analyzed.Count
    needsWork    = @($needWork | ForEach-Object { $_.Number })
    ignored      = @($doneWork | ForEach-Object { $_.Number })
    issues       = $analyzed
}
$jsonPayload | ConvertTo-Json -Depth 6 | Out-File -FilePath $OutJson -Encoding utf8
Write-Host "Сырые данные сохранены: $OutJson"

# --- Формирование отчёта (только issues для работы: (a) и (b)) ---
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("# Issues для работы — sivatorov/Workspace1C")
[void]$sb.AppendLine()
[void]$sb.AppendLine("_Дата анализа: $((Get-Date).ToString('yyyy-MM-dd HH:mm')) (UTC$((Get-TimeZone).BaseUtcOffset.Hours))_")
[void]$sb.AppendLine()
[void]$sb.AppendLine("## Текущая версия")
[void]$sb.AppendLine()
[void]$sb.AppendLine("- **Версия проекта** ([Workspace1C/Workspace1C.csproj](Workspace1C/Workspace1C.csproj), `<Version>`): ``$versionLine``")
if ($asmVersion) { [void]$sb.AppendLine("- AssemblyVersion: ``$asmVersion``") }
if ($fileVersion) { [void]$sb.AppendLine("- FileVersion: ``$fileVersion``") }
[void]$sb.AppendLine("- **Последняя запись в [$changelogPath]**: $changelogEntry")
[void]$sb.AppendLine("- **Последний git-тег**: ``$lastTag``")
if ($releaseInfo) { [void]$sb.AppendLine("- **Последний GitHub-релиз**: $($releaseInfo.tagName) (опубликован $($releaseInfo.publishedAt))") }
[void]$sb.AppendLine()
[void]$sb.AppendLine("## Сводка")
[void]$sb.AppendLine()
[void]$sb.AppendLine("- Всего открытых issues: **$($analyzed.Count)**")
[void]$sb.AppendLine("- Требуют работы (категории a и b): **$($needWork.Count)** → № $($needWork.Number -join ', ')")
[void]$sb.AppendLine("- Игнорируются (последний комментарий от sivatorov, ждут реакции автора): **$($doneWork.Count)** → № $($doneWork.Number -join ', ')")
[void]$sb.AppendLine()

foreach ($a in ($needWork | Sort-Object Number)) {
    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("## Issue #$($a.Number): $($a.Title)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("- **Автор**: $($a.Author)  ")
    [void]$sb.AppendLine("- **Создан**: $($a.CreatedAt)  ")
    [void]$sb.AppendLine("- **Обновлён**: $($a.UpdatedAt)")
    if ($a.Labels.Count -gt 0) { [void]$sb.AppendLine("- **Метки**: $($a.Labels -join ', ')") }
    [void]$sb.AppendLine("- **Комментариев**: $($a.CommentCount)  ")
    [void]$sb.AppendLine("- **Причина**: $($a.Reason)")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("### Описание")
    [void]$sb.AppendLine()
    $body = if ([string]::IsNullOrWhiteSpace($a.Body)) { '_(без описания)_' } else { $a.Body }
    [void]$sb.AppendLine($body)
    [void]$sb.AppendLine()
    if ($a.Comments.Count -gt 0) {
        [void]$sb.AppendLine("### Комментарии")
        [void]$sb.AppendLine()
        foreach ($c in $a.Comments) {
            [void]$sb.AppendLine("**@$($c.author)** ($($c.createdAt)):")
            [void]$sb.AppendLine()
            [void]$sb.AppendLine($c.body)
            [void]$sb.AppendLine()
        }
    }
}

$sb.ToString() | Out-File -FilePath $OutMd -Encoding utf8
Write-Host "Отчёт сохранён: $OutMd"
Write-Host ""
Write-Host ("ИТОГ: открытых issues: {0}; требуют работы: {1} (№ {2}); отработаны: {3}" -f $analyzed.Count, $needWork.Count, ($needWork.Number -join ', '), $doneWork.Count)
