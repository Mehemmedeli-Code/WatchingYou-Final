using MovieRental.SharedKernel.Abstractions;

namespace MovieRental.Modules.Cinema.Domain;

/// <summary>A cinema — the building people travel to.</summary>
public sealed class Venue : BaseEntity, ISoftDeletable
{
    public required string Name { get; set; }
    public string City { get; set; } = "Baku";
    public string? Address { get; set; }

    /// <summary>Where it is, so the map can put a pin on it. Editable by an admin — the seeded
    /// values are approximations, not surveyed positions.</summary>
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public List<Hall> Halls { get; set; } = [];

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

/// <summary>
/// A screening room, and the measurements the 3D preview needs to rebuild it.
///
/// The numbers live here rather than in the viewer because two halls are not the same room:
/// a small hall with a near screen gives a very different view from row A than a large one.
/// A preview that ignored that would be decoration rather than information.
/// </summary>
public sealed class Hall : BaseEntity, ISoftDeletable
{
    public Guid VenueId { get; set; }
    public Venue? Venue { get; set; }

    public required string Name { get; set; }

    /// <summary>Free text — "Dolby Atmos", "IMAX", "VIP recliners".</summary>
    public string? Format { get; set; }

    // --- seating ---
    public int Rows { get; set; } = 8;
    public int SeatsPerRow { get; set; } = 12;

    /// <summary>Seats on each side of the centre aisle. Rows = 2 x BlockColumns.</summary>
    public int BlockColumns { get; set; } = 6;

    public double AisleWidth { get; set; } = 1.9;
    public double RowPitch { get; set; } = 1.1;
    public double RowRise { get; set; } = 0.42;
    public double SeatWidth { get; set; } = 0.62;

    // --- the room, in metres ---
    public double RoomWidth { get; set; } = 16;
    public double RoomDepth { get; set; } = 24;
    public double RoomHeight { get; set; } = 8.4;

    // --- the screen ---
    public double ScreenWidth { get; set; } = 9.6;
    public double ScreenHeight { get; set; } = 4.2;

    /// <summary>Curvature radius. Larger is flatter; a flat screen is a very large number.</summary>
    public double ScreenCurveRadius { get; set; } = 17;

    /// <summary>Metres from the screen wall to the first row.</summary>
    public double FirstRowDistance { get; set; } = 7.4;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    public int Capacity => Rows * SeatsPerRow;
}
