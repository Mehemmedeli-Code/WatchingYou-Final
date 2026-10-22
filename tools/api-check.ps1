# Runs tools/api-check.mjs (the flows), tools/api-contract.mjs (every operation against its
# documented answers) and tools/searching-check.mjs (SearchingYou and the gender rules) on a
# throwaway copy of the site, never the real one.
#
# The check rents films, books and refunds seats, sends messages and opens help chats, so it
# must not touch MovieRentalDB: its rows would show up to real people (they did, once). This
# starts the built Debug host on https://localhost:7140 with its own database, which the
# development bootstrapper creates and seeds from scratch, runs the check, and drops it.
#
#   powershell -File tools\api-check.ps1        (build first: dotnet build src\MovieRental.Host)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'write-report.ps1')
# Every line the checks print, for the report: the FAIL lines are its problems.
$lines = [System.Collections.Generic.List[string]]::new()
function Keep { process { $line = "$_"; $lines.Add($line); $line } }
$root = Split-Path $PSScriptRoot -Parent
$hostDir = Join-Path $root 'src\MovieRental.Host'
$exe = Join-Path $hostDir 'bin\Debug\net10.0\MovieRental.Host.exe'
$database = 'WatchingYouApiCheck'
$log = Join-Path $env:TEMP 'watchingyou-api-check.log'

# The site's own LocalDB may belong to another logon session; its named pipe works from any.
$pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*LOCALDB*' } | Select-Object -First 1
$server = if ($pipe) { 'np:' + $pipe } else { '(localdb)\MSSQLLocalDB' }

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'https://localhost:7140'
$env:Notifications__Email__Provider = 'Console'   # codes go to the log, where the check reads them
$env:Seed__TestAccounts = 'true'   # the test logins (passwords in %LOCALAPPDATA%\WatchingYou\test-logins.json); a throwaway database only
$env:ConnectionStrings__Default = "Server=$server;Database=$database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"

$site = Start-Process -FilePath $exe -WorkingDirectory $hostDir -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err"
try {
    # Creating and seeding a fresh database takes a while the first time.
    for ($i = 0; $i -lt 90; $i++) {
        Start-Sleep 2
        curl.exe -sk -o NUL -f https://localhost:7140/api/health
        if ($LASTEXITCODE -eq 0) { break }
    }
    node (Join-Path $PSScriptRoot 'api-check.mjs') https://localhost:7140 $log | Keep
    $result = $LASTEXITCODE
    node (Join-Path $PSScriptRoot 'api-contract.mjs') https://localhost:7140 $log | Keep
    if ($LASTEXITCODE -ne 0) { $result = $LASTEXITCODE }

    # SearchingYou and the gender rules need people to join each other: the demo people, seeded
    # into this same throwaway database, then the site again for tools/searching-check.mjs.
    Stop-Process -Id $site.Id -Force
    Start-Sleep 1
    Push-Location $hostDir
    & $exe --seed-demo-people 2>&1 | Select-String 'demo people' | Select-Object -First 1
    Pop-Location
    $site = Start-Process -FilePath $exe -WorkingDirectory $hostDir -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$log.3" -RedirectStandardError "$log.3.err"
    for ($i = 0; $i -lt 90; $i++) {
        Start-Sleep 2
        curl.exe -sk -o NUL -f https://localhost:7140/api/health
        if ($LASTEXITCODE -eq 0) { break }
    }
    node (Join-Path $PSScriptRoot 'searching-check.mjs') https://localhost:7140 "$log.3" $database | Keep
    if ($LASTEXITCODE -ne 0) { $result = $LASTEXITCODE }

    # Every link each role can reach, in a real (headless) Edge: errors, "nothing here", script errors.
    node (Join-Path $PSScriptRoot 'link-check.mjs') https://localhost:7140 | Keep
    if ($LASTEXITCODE -ne 0) { $result = $LASTEXITCODE }

    # The same database started the way the real site starts, without Seed:TestAccounts: the
    # test logins, whose passwords are public, must come out closed.
    Stop-Process -Id $site.Id -Force
    Start-Sleep 1
    Remove-Item Env:Seed__TestAccounts
    $site = Start-Process -FilePath $exe -WorkingDirectory $hostDir -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$log.2" -RedirectStandardError "$log.2.err"
    for ($i = 0; $i -lt 90; $i++) {
        Start-Sleep 2
        curl.exe -sk -o NUL -f https://localhost:7140/api/health
        if ($LASTEXITCODE -eq 0) { break }
    }
    $open = sqlcmd -S $server -d $database -I -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM [identity].Users WHERE Email LIKE '%@reelandrow.test' AND IsDeleted = 0"
    if ("$open".Trim() -eq '0') { 'ok   test logins are closed on a start without Seed:TestAccounts' | Keep }
    else { "FAIL test logins still open on a start without Seed:TestAccounts: $open" | Keep; $result = 1 }
}
finally {
    Stop-Process -Id $site.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep 1
    sqlcmd -S $server -I -Q "IF DB_ID('$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END" | Out-Null
}
# The report for the admin panel and the owner's e-mail: the totals, and every FAIL line.
$failed = @($lines | Where-Object { $_ -match '^\s*FAIL' } | ForEach-Object { $_.Trim() })
if ($result -ne 0 -and $failed.Count -eq 0) { $failed = @("The check stopped with exit code $result before it finished.") }
$totals = ($lines | Where-Object { $_ -match 'checks passed|operations,|/\d+ ok|pages, \d+ problem' } | ForEach-Object { $_.Trim() }) -join ' | '
Write-CheckReport "api" $totals $failed
exit $result
