# =============================================================================
#  Workspace 1C 1.0.1.5 release notification to Telegram
#  Reads message text as UTF-8 from _telegram_msg_v10015.txt
#  and sends via percent-encoding ([uri]::EscapeDataString) - no mojibake.
#
#  Bot token is taken from env vars, or from the active profile's
#  notifications.json (APPDATA). Chat id is forced to the required channel
#  -1004425443528 (priority over env/config).
# =============================================================================

$MessageFile = Join-Path $PSScriptRoot '_telegram_msg_v10015.txt'

$BotToken = $env:WORKSPACE1C_TG_BOT_TOKEN
$ChatId   = $env:WORKSPACE1C_TG_CHAT_ID

if ([string]::IsNullOrWhiteSpace($BotToken) -or [string]::IsNullOrWhiteSpace($ChatId)) {
    $ProfilesFile = Join-Path $env:APPDATA 'Workspace1C\profiles.json'
    if (Test-Path $ProfilesFile) {
        $Profiles = Get-Content $ProfilesFile -Raw | ConvertFrom-Json
        $ProfileId = $Profiles.LastProfileId
        $CfgFile = Join-Path $env:APPDATA "Workspace1C\profiles\$ProfileId\notifications.json"
        if (Test-Path $CfgFile) {
            $Cfg = Get-Content $CfgFile -Raw | ConvertFrom-Json
            if ([string]::IsNullOrWhiteSpace($BotToken)) { $BotToken = $Cfg.TelegramBotToken }
            if ([string]::IsNullOrWhiteSpace($ChatId))   { $ChatId   = $Cfg.TelegramChatId }
        }
    }
}

# Required channel: -1004425443528 (priority over env/config).
$ChatId = '-1004425443528'

if ([string]::IsNullOrWhiteSpace($BotToken) -or [string]::IsNullOrWhiteSpace($ChatId)) {
    Write-Error 'Bot token or chat id is not set.'
    exit 1
}

# Read message text strictly as UTF-8 (avoids ANSI misread on Windows PowerShell 5.1).
$Message = [System.IO.File]::ReadAllText($MessageFile, [System.Text.Encoding]::UTF8)

$ReleaseUrl = 'https://github.com/sivatorov/Workspace1C/releases/tag/v1.0.1.5'

$Uri = "https://api.telegram.org/bot$BotToken/sendMessage"
$Body = 'chat_id=' + [uri]::EscapeDataString($ChatId) +
        '&text=' + [uri]::EscapeDataString($Message) +
        '&disable_web_page_preview=false'

Write-Host "Sending release notification v1.0.1.5 to $ChatId ..."
try {
    $Response = Invoke-RestMethod -Uri $Uri -Method Post -Body $Body -ContentType 'application/x-www-form-urlencoded'
    if ($Response.ok) {
        Write-Host "OK: message sent (message_id=$($Response.result.message_id))."
    }
    else {
        Write-Error "Telegram error: $($Response.description)"
        exit 1
    }
}
catch {
    Write-Error "Failed to send message: $($_.Exception.Message)"
    exit 1
}
