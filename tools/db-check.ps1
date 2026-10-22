# Read-only health checks of the site's database: every business rule in db-checks\invariants.sql
# should count 0, and no link between modules in db-checks\orphans.sql should point at nothing
# (the modules keep no foreign keys between their schemas, so this is what watches those links).
#
#   powershell -File tools\db-check.ps1                 # MovieRentalDB
#   powershell -File tools\db-check.ps1 -Database X     # any other copy
#
# Prints each problem found and exits 1 when there is any; tools\backup-db.ps1 runs it daily.
param([string]$Database = "MovieRentalDB")

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "write-report.ps1")
$checks = Join-Path $PSScriptRoot "db-checks"
$pipe = [System.IO.Directory]::GetFiles('\\.\pipe\') | Where-Object { $_ -like '*LOCALDB*' } | Select-Object -First 1
$server = if ($pipe) { 'np:' + $pipe } else { '(localdb)\MSSQLLocalDB' }

function Run([string]$file) {
    $rows = & sqlcmd -S $server -d $Database -b -I -W -h -1 -s "|" -i (Join-Path $checks $file) 2>&1
    if ($LASTEXITCODE -ne 0) { throw "$file failed: $rows" }
    $rows | Where-Object { $_ -and $_.Trim() }
}

$problems = @()
foreach ($row in Run "invariants.sql") {
    $name, $count = $row -split '\|'
    if ([int]$count -ne 0) { $problems += "rule broken ($count): $name" }
}
foreach ($row in Run "orphans.sql") {
    $column, $refs, $orphans, $fk = $row -split '\|'
    if ([int]$orphans -ne 0) { $problems += "points at nothing ($orphans of $refs): $column" }
}

# The real database's result goes to the admin panel (and to the owner, when it has problems).
if ($Database -eq "MovieRentalDB") {
    $summary = if ($problems.Count -eq 0) { "All business rules hold; every link between modules points at a row." } else { "$($problems.Count) problem(s)." }
    Write-CheckReport "db" $summary $problems
}

if ($problems.Count -eq 0) { "Database checks: all clear ($Database)"; exit 0 }
"Database checks found $($problems.Count) problem(s) in ${Database}:"
$problems | ForEach-Object { "  $_" }
exit 1
