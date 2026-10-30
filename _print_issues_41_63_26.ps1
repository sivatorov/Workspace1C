$json = Get-Content _issues_full.json -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($num in @(41, 63, 26)) {
    $issue = $json.issues | Where-Object { [int]$_.number -eq $num }
    Write-Host ("===== ISSUE #{0} - {1} =====" -f $issue.number, $issue.title)
    $last3 = $issue.comments | Sort-Object { [datetime]$_.createdAt } | Select-Object -Last 3
    foreach ($c in $last3) {
        Write-Host ("--- [{0} {1}] ---" -f $c.authorLogin, $c.createdAt)
        Write-Host $c.body
        Write-Host ""
    }
}
