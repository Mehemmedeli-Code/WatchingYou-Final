<#
    Migrations for a modular monolith.

    Every module owns its own schema and its own migration history table, so a migration is
    always scoped to one DbContext. The host is the startup project because that is where the
    connection string and the module registrations live.

    First time:
        .\scripts\migrations.ps1 -Add Init
        .\scripts\migrations.ps1 -Update

    After changing an entity:
        .\scripts\migrations.ps1 -Add AddVideoUrl -Only Catalog
        .\scripts\migrations.ps1 -Update -Only Catalog

    Once migrations exist, set Database:UseMigrations to true in appsettings.Development.json.
    The development bootstrapper then stops dropping the database on every schema change,
    which is the whole point of doing this.
#>

param(
    [string] $Add,
    [switch] $Update,
    [switch] $Remove,
    [string] $Only
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$host_ = Join-Path $root 'src\MovieRental.Host'

$modules = @(
    @{ Name = 'Identity'; Context = 'IdentityDbContext'; Project = 'src\Modules\MovieRental.Modules.Identity' },
    @{ Name = 'Catalog';  Context = 'CatalogDbContext';  Project = 'src\Modules\MovieRental.Modules.Catalog'  },
    @{ Name = 'Rentals';  Context = 'RentalsDbContext';  Project = 'src\Modules\MovieRental.Modules.Rentals'  },
    @{ Name = 'Cinema';   Context = 'CinemaDbContext';   Project = 'src\Modules\MovieRental.Modules.Cinema'   },
    @{ Name = 'Media';    Context = 'MediaDbContext';    Project = 'src\Modules\MovieRental.Modules.Media'    }
)

if ($Only) { $modules = $modules | Where-Object { $_.Name -eq $Only } }
if (-not $modules) { throw "No module matched '$Only'." }

foreach ($m in $modules) {
    $project = Join-Path $root $m.Project

    if ($Add) {
        Write-Host "→ $($m.Name): adding migration '$Add'" -ForegroundColor Cyan
        dotnet ef migrations add $Add -c $m.Context -o Persistence/Migrations -p $project -s $host_
    }
    if ($Remove) {
        Write-Host "→ $($m.Name): removing last migration" -ForegroundColor Yellow
        dotnet ef migrations remove -c $m.Context -p $project -s $host_
    }
    if ($Update) {
        Write-Host "→ $($m.Name): applying to the database" -ForegroundColor Green
        dotnet ef database update -c $m.Context -p $project -s $host_
    }
}

Write-Host "`nDone." -ForegroundColor Green
