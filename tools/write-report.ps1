# Leaves a check's result where the site reads it (src\MovieRental.Host\Infrastructure\HealthReports.cs):
# %USERPROFILE%\WatchingYouBackups\checks\<name>-check.json. The admin panel shows it, and the owner
# is e-mailed when it has problems. Dot-sourced by db-check.ps1 and api-check.ps1.
function Write-CheckReport([string]$Name, [string]$Summary, [string[]]$Problems) {
    $folder = Join-Path $env:USERPROFILE "WatchingYouBackups\checks"
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $report = [ordered]@{
        name = $Name
        atUtc = (Get-Date).ToUniversalTime().ToString("o")
        ok = ($Problems.Count -eq 0)
        summary = $Summary
        problems = @($Problems)
    }
    $json = ConvertTo-Json $report -Depth 3
    # Written beside, then moved over: the site never reads half a file.
    $file = Join-Path $folder "$Name-check.json"
    [System.IO.File]::WriteAllText("$file.tmp", $json)
    Move-Item -Force "$file.tmp" $file
}
