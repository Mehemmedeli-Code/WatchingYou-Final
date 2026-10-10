using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Cinema.Persistence;

namespace MovieRental.Host.Pages;

/// <summary>Instagram's "Locations", for a cinema: every venue with its address and halls, read
/// from the same tables the seat maps use, so the page never disagrees with the booking screens.</summary>
public sealed class LocationsModel(IPageShellFactory shell, CinemaDbContext db) : AppPageModel
{
    public sealed record HallRow(string Name, string? Format, int Seats);
    public sealed record VenueRow(string Name, string City, string? Address, double Latitude, double Longitude, IReadOnlyList<HallRow> Halls);

    public IReadOnlyList<VenueRow> Venues { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken ct)
    {
        View = shell.Create("footer.locations", "locations.lede", "locations", "info");
        Venues = await db.Venues.AsNoTracking()
            .OrderBy(v => v.Name)
            .Select(v => new VenueRow(v.Name, v.City, v.Address, v.Latitude, v.Longitude,
                v.Halls.OrderBy(h => h.Name).Select(h => new HallRow(h.Name, h.Format, h.Rows * h.SeatsPerRow)).ToList()))
            .ToListAsync(ct);
    }
}
