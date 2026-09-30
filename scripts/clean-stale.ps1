<#
    Removes files that earlier versions of the project had and this one does not.

    Extracting a zip over an existing folder overwrites what changed but leaves behind what was
    deleted. A stale .cs file still compiles into the project and fails against code that has
    moved on — which is exactly the CS0117 and CS0101 errors that keep appearing.

    The reliable habit is to delete the folder before extracting. When that is not convenient,
    run this.

        .\scripts\clean-stale.ps1
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

# Paths this project no longer contains. Each was removed when a feature was replaced.
$stale = @(
    'src\MovieRental.Host\Infrastructure\Assistant',           # AI assistant, replaced by the Help service
    'src\MovieRental.Host\Pages\Support.cshtml',
    'src\MovieRental.Host\Pages\Support.cshtml.cs',
    'client\src\pages\SupportPage.tsx',
    'client\src\components\HelpThread.tsx',                    # ticket-style help, replaced by live chat
    'src\Modules\MovieRental.Modules.Identity\Infrastructure\ConsoleSenders.cs',
    'client\src\components\ui\globe-3d.tsx',                # three-fiber globe, replaced by the CSS one
    'src\MovieRental.Host\wwwroot\globe'                     # textures only that globe used
)

$removed = 0
foreach ($relative in $stale) {
    $path = Join-Path $root $relative
    if (Test-Path $path) {
        Remove-Item $path -Recurse -Force
        Write-Host "removed  $relative" -ForegroundColor Yellow
        $removed++
    }
}

# bin and obj hold compiled copies of the files just deleted.
Get-ChildItem $root -Include bin, obj -Recurse -Directory -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

if ($removed -eq 0) {
    Write-Host "`nNothing stale found. The folder is clean." -ForegroundColor Green
} else {
    Write-Host "`n$removed leftover(s) removed. Build again." -ForegroundColor Green
}
