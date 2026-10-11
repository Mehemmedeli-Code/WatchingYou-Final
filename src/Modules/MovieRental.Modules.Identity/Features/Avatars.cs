using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// Profile pictures. Small, local and boring on purpose:
//  - the type comes from the file's first bytes, never from what the browser claims, and the
//    stored name is ours, so an upload cannot become a page or a path;
//  - files live under uploads/avatars (outside wwwroot, git-ignored) and are served with
//    nosniff and a long cache, since every new picture gets a new name.
// ponytail: local disk on one server; move to blob storage when the site runs on several.
public static class AvatarEndpoints
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private const string DefaultAvatar = "/icons/icon.svg";   // the eye logo

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/avatar", async (HttpRequest request, IdentityDbContext db, ICurrentUser currentUser,
                IHostEnvironment environment, CancellationToken ct) =>
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

                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
                if (user is null) return Results.NotFound();

                var folder = Folder(environment);
                Directory.CreateDirectory(folder);
                var name = $"{user.Id:N}-{Guid.NewGuid():N}{extension}";
                await using (var target = File.Create(Path.Combine(folder, name)))
                    await file.CopyToAsync(target, ct);

                DeleteOwnFile(environment, user.Id, user.AvatarUrl);
                user.AvatarUrl = $"/api/avatars/{name}";
                await db.SaveChangesAsync(ct);
                return Results.Ok(user.ToProfile());
            })
            .WithName("UploadAvatar").WithTags("Auth").RequireAuthorization()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxBytes + 256 * 1024))
            .RequireRateLimiting(AppPolicies.WriteRateLimit)
            .DisableAntiforgery();

        app.MapDelete("/api/auth/avatar", async (IdentityDbContext db, ICurrentUser currentUser,
                IHostEnvironment environment, CancellationToken ct) =>
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
                if (user is null) return Results.NotFound();
                DeleteOwnFile(environment, user.Id, user.AvatarUrl);
                user.AvatarUrl = null;
                await db.SaveChangesAsync(ct);
                return Results.Ok(user.ToProfile());
            })
            .WithName("RemoveAvatar").WithTags("Auth").RequireAuthorization();

        // The header's round picture. One stable URL for the page to use, whoever is signed in:
        // it redirects to their picture, or to the eye logo for no picture or no session.
        app.MapGet("/api/avatars/me", async (HttpContext http, IdentityDbContext db, ICurrentUser currentUser, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                if (currentUser.Id is not { } id) return Results.Redirect(DefaultAvatar);
                var url = await db.Users.Where(u => u.Id == id).Select(u => u.AvatarUrl).FirstOrDefaultAsync(ct);
                return Results.Redirect(string.IsNullOrEmpty(url) ? DefaultAvatar : url);
            })
            .WithName("MyAvatar").WithTags("Auth").AllowAnonymous();

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
            .WithName("GetAvatar").WithTags("Auth").AllowAnonymous();
    }

    private static string Folder(IHostEnvironment environment) =>
        Path.Combine(environment.ContentRootPath, "uploads", "avatars");

    private static void DeleteOwnFile(IHostEnvironment environment, Guid owner, string? url)
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

    private static MovieRental.SharedKernel.Results.Error Error(string message) =>
        MovieRental.SharedKernel.Results.Error.Validation(message);
}
