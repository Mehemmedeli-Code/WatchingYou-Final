using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

/// <summary>A picture already checked by the endpoint (size, and a JPEG/PNG/WebP by its bytes).</summary>
public sealed record SetAvatarCommand(IFormFile File, string Extension) : ICommand<Result<UserProfileResponse>>;

internal sealed class SetAvatarHandler(IdentityDbContext db, ICurrentUser currentUser, IHostEnvironment environment)
    : ICommandHandler<SetAvatarCommand, Result<UserProfileResponse>>
{
    public async Task<Result<UserProfileResponse>> Handle(SetAvatarCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
        if (user is null) return Result.Failure<UserProfileResponse>(Error.NotFound("Account"));

        var folder = AvatarEndpoints.Folder(environment);
        Directory.CreateDirectory(folder);
        var name = $"{user.Id:N}-{Guid.NewGuid():N}{command.Extension}";
        await using (var target = File.Create(Path.Combine(folder, name)))
            await command.File.CopyToAsync(target, ct);

        AvatarEndpoints.DeleteOwnFile(environment, user.Id, user.AvatarUrl);
        user.AvatarUrl = $"/api/avatars/{name}";
        await db.SaveChangesAsync(ct);
        return user.ToProfile();
    }
}

public sealed record RemoveAvatarCommand : ICommand<Result<UserProfileResponse>>;

internal sealed class RemoveAvatarHandler(IdentityDbContext db, ICurrentUser currentUser, IHostEnvironment environment)
    : ICommandHandler<RemoveAvatarCommand, Result<UserProfileResponse>>
{
    public async Task<Result<UserProfileResponse>> Handle(RemoveAvatarCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
        if (user is null) return Result.Failure<UserProfileResponse>(Error.NotFound("Account"));
        AvatarEndpoints.DeleteOwnFile(environment, user.Id, user.AvatarUrl);
        // Removing leaves the plain grey figure, not the eye: the eye is what a new account
        // starts with, and once taken away it does not come back by itself.
        user.AvatarUrl = AvatarEndpoints.BlankAvatar;
        await db.SaveChangesAsync(ct);
        return user.ToProfile();
    }
}

/// <summary>A country's flag as the picture; the code is checked against the flags the site has.</summary>
public sealed record SetFlagAvatarCommand(string Country) : ICommand<Result<UserProfileResponse>>;

internal sealed class SetFlagAvatarHandler(IdentityDbContext db, ICurrentUser currentUser, IHostEnvironment environment)
    : ICommandHandler<SetFlagAvatarCommand, Result<UserProfileResponse>>
{
    public async Task<Result<UserProfileResponse>> Handle(SetFlagAvatarCommand command, CancellationToken ct)
    {
        var code = command.Country.ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z]{2}$")
            || !File.Exists(Path.Combine(environment.ContentRootPath, "wwwroot", "app", "flags", code + ".svg")))
            return Result.Failure<UserProfileResponse>(Error.Validation("There is no flag for that country."));
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
        if (user is null) return Result.Failure<UserProfileResponse>(Error.NotFound("Account"));
        AvatarEndpoints.DeleteOwnFile(environment, user.Id, user.AvatarUrl);
        user.AvatarUrl = $"/app/flags/{code}.svg";
        await db.SaveChangesAsync(ct);
        return user.ToProfile();
    }
}

/// <summary>The signed-in person's picture address; null for none, or nobody signed in.</summary>
public sealed record MyAvatarQuery : IQuery<string?>;

internal sealed class MyAvatarHandler(IdentityDbContext db, ICurrentUser currentUser) : IQueryHandler<MyAvatarQuery, string?>
{
    public async Task<string?> Handle(MyAvatarQuery query, CancellationToken ct) =>
        currentUser.Id is { } id ? await db.Users.Where(u => u.Id == id).Select(u => u.AvatarUrl).FirstOrDefaultAsync(ct) : null;
}

// Profile pictures. Small, local and boring on purpose:
//  - the type comes from the file's first bytes, never from what the browser claims, and the
//    stored name is ours, so an upload cannot become a page or a path;
//  - files live under uploads/avatars (outside wwwroot, git-ignored) and are served with
//    nosniff and a long cache, since every new picture gets a new name.
// ponytail: local disk on one server; move to blob storage when the site runs on several.
public static class AvatarEndpoints
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const string DefaultAvatar = "/icons/avatar.svg";   // the eye, close up
    internal const string BlankAvatar = "/icons/avatar-blank.svg";   // after "remove": a grey figure

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/avatar", async (HttpRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                if (!request.HasFormContentType) return Results.BadRequest(Error("Send the picture as multipart/form-data."));
                var file = (await request.ReadFormAsync(ct)).Files.GetFile("file");
                if (file is null || file.Length == 0) return Results.BadRequest(Error("No picture was attached."));
                if (file.Length > MaxBytes) return Results.BadRequest(Error("The picture must be 2 MB or smaller."));

                var header = new byte[12];
                await using (var peek = file.OpenReadStream())
                    _ = await peek.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
                var extension = ExtensionOf(header);
                if (extension is null) return Results.BadRequest(Error("Use a JPEG, PNG or WebP picture."));

                return (await dispatcher.Send(new SetAvatarCommand(file, extension), ct)).ToHttp();
            })
            .WithName("UploadAvatar").WithTags("Auth").RequireAuthorization()
            .WithSummary("Upload my profile picture")
            .WithDescription("multipart/form-data with the picture in the field `file`: JPEG, PNG or WebP, 2 MB at most " +
                             "(the type is read from the file itself). Answers with the updated profile.")
            .Accepts<AvatarForm>("multipart/form-data")
            .Produces<UserProfileResponse>()
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status400BadRequest)
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status404NotFound)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxBytes + 256 * 1024))
            .RequireRateLimiting(AppPolicies.WriteRateLimit)
            .DisableAntiforgery();

        app.MapDelete("/api/auth/avatar", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(new RemoveAvatarCommand(), ct)).ToHttp())
            .WithName("RemoveAvatar").WithTags("Auth").RequireAuthorization()
            .WithSummary("Remove my profile picture (a plain grey figure from then on)")
            .Produces<UserProfileResponse>()
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status404NotFound);

        app.MapPut("/api/auth/avatar/flag/{country}", async (string country, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(new SetFlagAvatarCommand(country), ct)).ToHttp())
            .WithName("SetFlagAvatar").WithTags("Auth").RequireAuthorization()
            .WithSummary("Use a country's flag as my profile picture")
            .WithDescription("`country` is a two-letter code (az, tr, de…). Answers with the updated profile.")
            .Produces<UserProfileResponse>()
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status400BadRequest)
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status404NotFound);

        // The header's round picture. One stable URL for the page to use, whoever is signed in:
        // it redirects to their picture, or to the eye logo for no picture or no session.
        app.MapGet("/api/avatars/me", async (HttpContext http, IDispatcher dispatcher, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                var url = await dispatcher.Ask(new MyAvatarQuery(), ct);
                return Results.Redirect(string.IsNullOrEmpty(url) ? DefaultAvatar : url);
            })
            .WithName("MyAvatar").WithTags("Auth").AllowAnonymous()
            .WithSummary("My profile picture: redirects to it, or to the default eye")
            .Produces(StatusCodes.Status302Found);

        app.MapGet("/api/avatars/{file}", (string file, HttpContext http, IHostEnvironment environment) =>
            {
                // Only names this endpoint wrote: hex, a dash, hex, a known extension.
                if (!System.Text.RegularExpressions.Regex.IsMatch(file, "^[0-9a-f]{32}-[0-9a-f]{32}\\.(jpg|png|webp)$"))
                    return Results.NotFound();
                var path = Path.Combine(Folder(environment), file);
                if (!File.Exists(path)) return Results.NotFound();
                http.Response.Headers.XContentTypeOptions = "nosniff";
                http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                var type = Path.GetExtension(file) switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
                return Results.File(path, type);
            })
            .WithName("GetAvatar").WithTags("Auth").AllowAnonymous()
            .WithSummary("A profile picture file, by the name in a profile's `avatarUrl`")
            .Produces(StatusCodes.Status200OK, typeof(Stream), "image/jpeg", "image/png", "image/webp")
            .Produces<MovieRental.SharedKernel.Results.Error>(StatusCodes.Status404NotFound);
    }

    internal static string Folder(IHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "uploads", "avatars");

    internal static void DeleteOwnFile(IHostEnvironment environment, Guid owner, string? url)
    {
        if (url is null || !url.StartsWith("/api/avatars/", StringComparison.Ordinal)) return;
        // Stored names start with the owner's id: never delete a file that is someone else's,
        // whatever the column says.
        var name = Path.GetFileName(url);
        if (!name.StartsWith($"{owner:N}-", StringComparison.Ordinal)) return;
        var path = Path.Combine(Folder(environment), name);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string? ExtensionOf(byte[] h) =>
        h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF ? ".jpg"
        : h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47 ? ".png"
        : h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46 && h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50 ? ".webp"
        : null;

    /// <summary>The upload form, as the API reference shows it.</summary>
    private sealed record AvatarForm(IFormFile File);

    private static Error Error(string message) => MovieRental.SharedKernel.Results.Error.Validation(message);
}
