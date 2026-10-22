# Backs up the site's database (MovieRentalDB) and its uploaded files, once a day.
#
# tools\run-site.ps1 calls this every few minutes; it does nothing until the newest backup is a
# day old. Run it by hand at any time with -Force for a backup right now:
#
#   powershell -File tools\backup-db.ps1 -Force
#
# Where: %USERPROFILE%\WatchingYouBackups (outside the project, so git and the zips never carry
# it). The database goes to MovieRentalDB_<date>.bak, the newest 14 are kept; uploads\ is
# mirrored to uploads\ beside them. The whole folder is then mirrored to OneDrive\WatchingYouBackups
# (-OffsiteFolder, on every backup), so a broken disk does not take the backups with it.
#
# Restoring (the site stopped first: powershell -File tools\stop-site.ps1):
#   sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "RESTORE DATABASE [MovieRentalDB] FROM DISK = N'<file>.bak' WITH REPLACE"
param(
    [switch]$Force,
    [string]$BackupFolder = (Join-Path $env:USERPROFILE "WatchingYouBackups"),
    # A second copy off this disk: OneDrive (synced to the cloud) when there is one. "" turns it off.
    [string]$OffsiteFolder = $(
        $oneDrive = if ($env:OneDrive) { $env:OneDrive } else { Join-Path $env:USERPROFILE "OneDrive" }
        if (Test-Path $oneDrive) { Join-Path $oneDrive "WatchingYouBackups" } else { "" })
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$uploads = Join-Path $root "src\MovieRental.Host\uploads"
$database = "MovieRentalDB"
$keep = 14

New-Item -ItemType Directory -Force -Path $BackupFolder | Out-Null
$newest = Get-ChildItem $BackupFolder -Filter "${database}_*.bak" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $Force -and $newest -and $newest.LastWriteTime -gt (Get-Date).AddHours(-24)) { exit 0 }

# The instance's own pipe works from any logon session; the (localdb) name only from its own.
$pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*LOCALDB*' } | Select-Object -First 1
$server = if ($pipe) { 'np:' + $pipe } else { '(localdb)\MSSQLLocalDB' }

$file = Join-Path $BackupFolder ("{0}_{1}.bak" -f $database, (Get-Date -Format "yyyy-MM-dd_HHmm"))
# COPY_ONLY: a side copy that leaves the database's own backup chain alone. CHECKSUM: a damaged
# page fails the backup now rather than the restore later.
$output = & sqlcmd -S $server -b -I -Q "BACKUP DATABASE [$database] TO DISK = N'$file' WITH COPY_ONLY, INIT, CHECKSUM" 2>&1
if ($LASTEXITCODE -ne 0) { throw "Database backup failed: $output" }

# Videos and pictures live on disk, not in the database. /MIR copies only what changed.
if (Test-Path $uploads) {
    & robocopy $uploads (Join-Path $BackupFolder "uploads") /MIR /R:1 /W:1 /NP /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying the uploads failed (robocopy exit $LASTEXITCODE)." }
}

Get-ChildItem $BackupFolder -Filter "${database}_*.bak" | Sort-Object LastWriteTime -Descending |
    Select-Object -Skip $keep | Remove-Item -Force

if ($OffsiteFolder) {
    & robocopy $BackupFolder $OffsiteFolder /MIR /R:1 /W:1 /NP /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying to $OffsiteFolder failed (robocopy exit $LASTEXITCODE)." }
}

"Backed up $database to $file" + $(if ($OffsiteFolder) { " and copied it to $OffsiteFolder" } else { "" })

# The daily health check rides along: broken business rules and links between modules that
# point at nothing (see tools\db-check.ps1). Reported, never a reason to fail the backup.
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "db-check.ps1") 2>&1 | ForEach-Object { "$_" }
exit 0
