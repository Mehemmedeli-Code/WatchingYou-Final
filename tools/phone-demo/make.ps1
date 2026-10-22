# Re-records the phone video beside the poster ribbon on the home page
# (src\MovieRental.Host\wwwroot\media\phone-demo.mp4 and .jpg).
#
#   dotnet build src\MovieRental.Host        (the Debug host)
#   powershell -File tools\phone-demo\make.ps1
#
# Runs a throwaway copy of the site on https://localhost:7142 with its own database, created and
# seeded for the run and dropped after it (as tools\api-check.ps1 does), so every take starts
# from the same messages. Test accounts on, e-mail written to the console; never the real site
# or MovieRentalDB. Needs Microsoft Edge and ffmpeg. What the phone does is in record.mjs.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$hostDir = Join-Path $root 'src\MovieRental.Host'
$exe = Join-Path $hostDir 'bin\Debug\net10.0\MovieRental.Host.exe'
$pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*LOCALDB*' } | Select-Object -First 1
$server = if ($pipe) { "np:$pipe" } else { '(localdb)\MSSQLLocalDB' }
$database = 'WatchingYouPhoneDemo'

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Notifications__Email__Provider = 'Console'
$env:Seed__TestAccounts = 'true'
$env:ConnectionStrings__Default = "Server=$server;Database=$database;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"

try {
    # The demo people (three of them write to the customer) and the customer on the globe, which
    # is what lets strangers write. This first start also creates and seeds the database.
    $env:ASPNETCORE_URLS = 'https://localhost:7149'
    Push-Location $hostDir
    try { & $exe --seed-demo-people | Out-Null } finally { Pop-Location }
    # A man, so the SearchingYou searches for men and the mixed ones are his to join.
    sqlcmd -S $server -d $database -I -Q "UPDATE [identity].Users SET ShareOnGlobe = 1, City = 'Baku', Gender = 'male' WHERE Email = 'customer@reelandrow.test'" | Out-Null

    $env:ASPNETCORE_URLS = 'https://localhost:7142'
    $site = Start-Process -FilePath $exe -WorkingDirectory $hostDir -WindowStyle Hidden -PassThru
    for ($i = 0; $i -lt 90; $i++) {
        Start-Sleep 2
        curl.exe -sk -o NUL -f https://localhost:7142/api/health
        if ($LASTEXITCODE -eq 0) { break }
    }
    $env:NODE_TLS_REJECT_UNAUTHORIZED = '0'   # the development certificate, this machine only
    node (Join-Path $PSScriptRoot 'messages.mjs')
    node (Join-Path $PSScriptRoot 'record.mjs')
}
finally {
    if ($site) { Stop-Process -Id $site.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep 1
    sqlcmd -S $server -I -Q "IF DB_ID('$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END" | Out-Null
}
