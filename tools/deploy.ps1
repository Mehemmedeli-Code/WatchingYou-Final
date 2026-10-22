# Puts the current code on the real site (https://localhost:7139), only once the full API check
# has passed on a throwaway copy:
#
#   powershell -File tools\deploy.ps1
#
#   1. the client is built and smoke-tested, the Debug host built;
#   2. tools\api-check.ps1 runs (the flows, every operation's contract, SearchingYou) — a failure
#      stops here, and the real site is not touched;
#   3. a backup of MovieRentalDB is taken (migrations run on the next start);
#   4. the site is stopped, built in Release, started again, and asked whether it is up.
#
# -SkipChecks leaves out step 2: for an emergency only, and the next nightly check still runs.
param([switch]$SkipChecks)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$hostProject = Join-Path $root "src\MovieRental.Host"

function Step([string]$text) { Write-Host "deploy: $text" }
function Run([string]$what, [scriptblock]$command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "deploy stopped: $what failed (exit code $LASTEXITCODE). The real site was not touched." }
}

Push-Location (Join-Path $root "client")
try {
    Step "client build and smoke test"
    Run "the client build" { npm run --silent build | Out-Null }
    Run "the client smoke test" { npm run --silent smoke | Out-Null }
}
finally { Pop-Location }

if (-not $SkipChecks) {
    Step "Debug build and the full API check (a few minutes)"
    Run "the Debug build" { dotnet build $hostProject -c Debug -p:SkipClientBuild=true --nologo -v q | Out-Null }
    Run "the API check" { powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "api-check.ps1") | Select-String "checks passed|operations,|/\d+ ok|pages, \d+ problem|FAIL" | ForEach-Object { Write-Host "  $_" } }
}

Step "backup"
Run "the backup" { powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "backup-db.ps1") -Force | Out-Null }

Step "stop, Release build, start"
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "stop-site.ps1") | Out-Null
Run "the Release build" { dotnet build $hostProject -c Release -p:SkipClientBuild=true --nologo -v q | Out-Null }
Start-ScheduledTask -TaskName WatchingYou

for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep 3
    $code = curl.exe -sk -o NUL -w "%{http_code}" https://localhost:7139/api/health
    if ($code -eq "200") { Step "the site is up"; exit 0 }
}
throw "deploy: the site did not answer within two minutes; see the logs beside tools\run-site.ps1."
