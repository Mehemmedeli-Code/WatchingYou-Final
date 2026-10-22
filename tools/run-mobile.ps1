# Keeps the phone app's dev server (http://localhost:5174/mobile.html) running, the way
# run-site.ps1 keeps the site running. Started by run-site.ps1; output goes to logs\mobile.log.
#
#   Stop it:  powershell -File C:\wy\WatchingYou\tools\stop-site.ps1

$created = $false
$mutex = New-Object System.Threading.Mutex($true, "Local\WatchingYouMobileKeeper", [ref]$created)
if (-not $created) { exit 0 }

$root = Split-Path -Parent $PSScriptRoot
$client = Join-Path $root "client"
$logDir = Join-Path $root "logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir "site.log"

function Say($text) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  [mobile] $text" | Add-Content -Path $log -Encoding utf8 }

try {
    while ($true) {
        Say "Starting the phone app's dev server."
        $vite = Start-Process -FilePath "cmd.exe" -ArgumentList "/c", "npm run dev:mobile" -WorkingDirectory $client `
            -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $logDir "mobile-output.log") -RedirectStandardError (Join-Path $logDir "mobile-error.log")
        $vite.WaitForExit()
        Say "The phone app's dev server stopped; starting it again in 5 seconds."
        Start-Sleep 5
    }
}
finally {
    $mutex.ReleaseMutex()
}
