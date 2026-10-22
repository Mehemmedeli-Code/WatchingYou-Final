# Keeps WatchingYou running. Started by the Windows task "WatchingYou" at sign-in (and on
# demand); it starts the database, starts the site, and starts it again if it ever stops.
# Everything it does is written to logs\site.log next to the project.
#
#   Stop the site:        powershell -File tools\stop-site.ps1
#   Back up right now:    powershell -File tools\backup-db.ps1 -Force   (it runs daily on its own)
#   Start it again:       schtasks /run /tn WatchingYou
#   Remove the autostart: schtasks /delete /tn WatchingYou /f

# One keeper at a time. Two of them fought over the port and kept restarting each other's
# site (and the database) — a second copy now simply leaves.
$created = $false
$mutex = New-Object System.Threading.Mutex($true, "Local\WatchingYouSiteKeeper", [ref]$created)
if (-not $created) { exit 0 }

$root = Split-Path -Parent $PSScriptRoot
$hostDir = Join-Path $root "src\MovieRental.Host"
$exe = Join-Path $hostDir "bin\Release\net10.0\MovieRental.Host.exe"
$logDir = Join-Path $root "logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir "site.log"

function Say($text) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $text" | Add-Content -Path $log -Encoding utf8 }

function Start-Database {
    & sqllocaldb start MSSQLLocalDB 2>&1 | Out-Null
    Start-Sleep 1
    if (& sqllocaldb info MSSQLLocalDB | Select-String "State:\s+Running") { return }
    # Only when a normal start has really failed: LocalDB left "stopped" while its old process
    # still holds the files. Clear that process (LocalDB's own only — never a full SQL Server
    # on this machine) and start again.
    Say "LocalDB would not start; clearing a stuck LocalDB process."
    & sqllocaldb stop MSSQLLocalDB -k 2>&1 | Out-Null
    Get-Process sqlservr -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\LocalDB\*' } | Stop-Process -Force
    Start-Sleep 3
    & sqllocaldb start MSSQLLocalDB 2>&1 | Out-Null
}

# The same settings the project's launch profile uses.
$env:ASPNETCORE_ENVIRONMENT = "Development"
# Every address of this machine, not only localhost, so a phone on the same Wi-Fi can reach it
# (Windows Firewall still decides whether it may; see tools\test-accounts.md).
$env:ASPNETCORE_URLS = "https://*:7139;http://*:5139"
# The owner is e-mailed when a nightly check finds a problem (see HealthReports.cs).
$env:Health__MailOwner = "true"

# A site left over from before would hold the port.
Get-Process MovieRental.Host -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep 1

# The phone app's dev server has a keeper of its own, so the two restart independently.
Start-Process -FilePath "powershell.exe" -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", (Join-Path $PSScriptRoot "run-mobile.ps1") -WindowStyle Hidden

try {
    while ($true) {
        Start-Database
        Say "Starting the site."
        $site = Start-Process -FilePath $exe -WorkingDirectory $hostDir -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $logDir "site-output.log") -RedirectStandardError (Join-Path $logDir "site-error.log")
        # While the site runs, a backup every day (tools\backup-db.ps1 does nothing until the
        # newest one is a day old). A failed backup is logged; it never stops the site.
        while (-not $site.WaitForExit(600000)) {
            $result = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "backup-db.ps1") 2>&1
            if ($LASTEXITCODE -ne 0) { Say "Backup FAILED: $result" } elseif ($result) { Say "$result" }

            # The full API check, every night after 3 o'clock, on its own throwaway copy of the site
            # (tools\api-check.ps1). Its report goes where the admin panel and the owner's e-mail
            # read it; this log only says it ran.
            $report = Join-Path $env:USERPROFILE "WatchingYouBackups\checks\api-check.json"
            $last = if (Test-Path $report) { (Get-Item $report).LastWriteTime } else { [datetime]::MinValue }
            if ((Get-Date).Hour -ge 3 -and $last.Date -lt (Get-Date).Date) {
                Say "Nightly API check starting."
                & dotnet build $hostDir -c Debug -p:SkipClientBuild=true --nologo -v q 2>&1 | Out-Null
                & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "api-check.ps1") 2>&1 | Out-Null
                Say "Nightly API check finished (exit code $LASTEXITCODE)."
            }
        }
        Say "The site stopped (exit code $($site.ExitCode)); starting it again in 5 seconds."
        Start-Sleep 5
    }
}
finally {
    $mutex.ReleaseMutex()
}
