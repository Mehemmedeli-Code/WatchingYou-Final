# Runs tools/api-check.mjs against a throwaway copy of the site, never the real one.
#
# The check rents films, books and refunds seats, sends messages and opens help chats, so it
# must not touch MovieRentalDB: its rows would show up to real people (they did, once). This
# starts the built Debug host on https://localhost:7140 with its own database, which the
# development bootstrapper creates and seeds from scratch, runs the check, and drops it.
#
#   powershell -File tools\api-check.ps1        (build first: dotnet build src\MovieRental.Host)

$ErrorActionPreference = 'Stop'
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
$env:Notifications__Sms__Provider = 'Console'
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
    node (Join-Path $PSScriptRoot 'api-check.mjs') https://localhost:7140 $log
    $result = $LASTEXITCODE
}
finally {
    Stop-Process -Id $site.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep 1
    sqlcmd -S $server -I -Q "IF DB_ID('$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END" | Out-Null
}
exit $result
