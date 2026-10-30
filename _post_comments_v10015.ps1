# Publish comments to issues #71, #75, #79, #83, #87 (version 1.0.1.5)
# Comment body comes from _issue<NN>_comment_v10015.md (UTF-8 drafts).
# Uses GitHub REST API via Invoke-RestMethod; token from 'gh auth token' (sivatorov).
# Issues are NOT closed.

$ErrorActionPreference = 'Stop'

$issues = @(
    @{ Number = 71; File = '_issue71_comment_v10015.md' },
    @{ Number = 75; File = '_issue75_comment_v10015.md' },
    @{ Number = 79; File = '_issue79_comment_v10015.md' },
    @{ Number = 83; File = '_issue83_comment_v10015.md' },
    @{ Number = 87; File = '_issue87_comment_v10015.md' }
)

$token = (gh auth token).Trim()
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Cannot get GitHub token (gh auth token returned empty)."
}
$headers = @{
    Authorization = "Bearer $token"
    'User-Agent'  = 'Workspace1C-release-bot'
    Accept        = 'application/vnd.github+json'
}

foreach ($item in $issues) {
    $filePath = (Resolve-Path -Path $item.File).Path
    $text = [System.IO.File]::ReadAllText($filePath, [System.Text.Encoding]::UTF8)
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "Empty comment body for issue #$($item.Number) ($($item.File))"
    }

    # Build JSON manually: ConvertTo-Json of a plain string gives a quoted JSON literal.
    $payload = '{"body":' + (ConvertTo-Json $text) + '}'
    $uri = "https://api.github.com/repos/sivatorov/Workspace1C/issues/$($item.Number)/comments"

    Write-Host "Posting comment to issue #$($item.Number) ..."
    $resp = Invoke-RestMethod -Uri $uri -Method Post -Headers $headers `
        -ContentType 'application/json; charset=utf-8' -Body $payload
    Write-Host "OK #$($item.Number): $($resp.html_url)"
}

Write-Host "Done: comments published to all 5 issues."