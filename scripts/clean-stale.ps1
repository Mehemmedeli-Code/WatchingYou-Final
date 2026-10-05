<#
    Lists files this version of the project no longer uses. It does not delete anything.

    The repository keeps every file it has ever had — that is a rule of this project, not a
    default. A file that must stop compiling is left in place and excluded from its project
    with <Compile Remove="..."/>, the way ConsoleSenders.cs is in the Identity module. This
    script only reports, so nothing it finds can end up as a deletion in a commit.

        .\scripts\clean-stale.ps1
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# Files earlier versions had and this one no longer uses.
$stale = @(
    'src\MovieRental.Host\Infrastructure\Assistant',
    'src\MovieRental.Host\Pages\Support.cshtml',
    'src\MovieRental.Host\Pages\Support.cshtml.cs',
    'client\src\pages\SupportPage.tsx',
    'client\src\components\HelpThread.tsx',
    'client\src\components\ui\globe-3d.tsx',
    'src\MovieRental.Host\wwwroot\globe'
)

$found = 0
foreach ($relative in $stale) {
    if (Test-Path (Join-Path $root $relative)) {
        Write-Host "present, unused:  $relative" -ForegroundColor Yellow
        $found++
    }
}

if ($found -eq 0) {
    Write-Host "Nothing unused found." -ForegroundColor Green
} else {
    Write-Host "`n$found unused item(s). Left in place. If one breaks the build, exclude it from its" -ForegroundColor Green
    Write-Host "project with <Compile Remove> rather than deleting it." -ForegroundColor Green
}
