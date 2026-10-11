using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using MovieRental.Modules.Media.Domain;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Media.Features;

public sealed record UploadShortFilmCommand(
    string Title, string Synopsis, ShortFilmOrigin Origin, ShortFilmVisibility Visibility,
    string OriginalFileName, string ContentType, Stream Content, long SizeBytes)
    : ICommand<Result<ShortFilmSummary>>;

internal sealed class UploadShortFilmHandler(
    MediaDbContext db, ICurrentUser currentUser, IUserDirectory users, IHostEnvironment environment)
    : ICommandHandler<UploadShortFilmCommand, Result<ShortFilmSummary>>
{
    internal const long MaxBytes = 512L * 1024 * 1024;
    private const int MaxPending = 3;
    private static readonly string[] AllowedExtensions = [".mp4", ".mov", ".webm", ".mkv"];

    public async Task<Result<ShortFilmSummary>> Handle(UploadShortFilmCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            return Result.Failure<ShortFilmSummary>(Error.Validation("Give your film a title."));
        if (command.Title.Trim().Length > 120)
            return Result.Failure<ShortFilmSummary>(Error.Validation("The title can be 120 characters at most."));
        if ((command.Synopsis ?? "").Trim().Length > 2000)
            return Result.Failure<ShortFilmSummary>(Error.Validation("The synopsis can be 2000 characters at most."));
        if (!Enum.IsDefined(command.Origin) || !Enum.IsDefined(command.Visibility))
            return Result.Failure<ShortFilmSummary>(Error.Validation("Say whether the film is AI-generated or hand-crafted."));
        if (command.SizeBytes is 0 or > MaxBytes)
            return Result.Failure<ShortFilmSummary>(Error.Validation("Upload a file between 1 byte and 512 MB."));

        var extension = Path.GetExtension(command.OriginalFileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return Result.Failure<ShortFilmSummary>(Error.Validation($"Supported formats: {string.Join(", ", AllowedExtensions)}."));

        var userId = currentUser.RequireId();

        // Each pending film waits for a human reviewer and takes disk space until then.
        if (await db.ShortFilms.CountAsync(f => f.UserId == userId && f.Status == SubmissionStatus.Pending, ct) >= MaxPending)
            return Result.Failure<ShortFilmSummary>(Error.Validation($"You already have {MaxPending} films waiting for review. Wait for a decision first."));

        var contact = await users.GetContactAsync(userId, ct);

        // Stored under a generated name: the uploader's filename never reaches the file
        // system, so path traversal and collisions are both off the table.
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var folder = ShortFilmStorage.Folder(environment);
        Directory.CreateDirectory(folder);

        // The first bytes must say video: an MP4/QuickTime box ("ftyp", "moov", ...) or the
        // Matroska/WebM header. The extension alone let any file in as long as it was named .mp4.
        var header = new byte[12];
        var read = await command.Content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        if (read < header.Length || !LooksLikeVideo(header))
            return Result.Failure<ShortFilmSummary>(Error.Validation($"Supported formats: {string.Join(", ", AllowedExtensions)}."));

        var path = Path.Combine(folder, storedName);
        try
        {
            await using (var target = File.Create(path))
            {
                await target.WriteAsync(header, ct);
                await command.Content.CopyToAsync(target, ct);
            }
        }
        catch
        {
            // A cancelled or failed copy must not leave half a video on the disk.
            File.Delete(path);
            throw;
        }

        var film = new ShortFilm
        {
            UserId = userId,
            AuthorName = contact?.FullName ?? "Unknown",
            Title = command.Title.Trim(),
            Synopsis = command.Synopsis.Trim(),
            Origin = command.Origin,
            Visibility = command.Visibility,
            StoredFileName = storedName,
            OriginalFileName = Path.GetFileName(command.OriginalFileName),
            ContentType = ShortFilmStorage.ContentTypeFor(storedName),
            SizeBytes = command.SizeBytes,
            SubmittedAtUtc = DateTime.UtcNow,
            ReviewDeadlineUtc = DateTime.UtcNow.AddDays(3)
        };

        db.ShortFilms.Add(film);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // No row, no file: a rejected save used to leave the video on the disk for good.
            File.Delete(path);
            throw;
        }
        return Result.Success(film.ToSummary(DateTime.UtcNow));
    }

    private static bool LooksLikeVideo(byte[] h)
    {
        if (h[0] == 0x1A && h[1] == 0x45 && h[2] == 0xDF && h[3] == 0xA3) return true;   // Matroska / WebM
        var box = System.Text.Encoding.ASCII.GetString(h, 4, 4);
        return box is "ftyp" or "moov" or "mdat" or "wide" or "free" or "skip" or "pnot";  // MP4 / QuickTime
    }
}

public static class UploadShortFilmEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/shorts", async (HttpRequest request, IDispatcher dispatcher, CancellationToken ct) =>
            {
                if (!request.HasFormContentType) return Results.BadRequest(Error.Validation("Send the film as multipart/form-data."));

                var form = await request.ReadFormAsync(ct);
                var file = form.Files.GetFile("file");
                if (file is null) return Results.BadRequest(Error.Validation("No file was attached."));

                if (!Enum.TryParse<ShortFilmOrigin>(form["origin"].ToString(), true, out var origin))
                    return Results.BadRequest(Error.Validation("Say whether the film is AI-generated or hand-crafted."));

                var visibility = Enum.TryParse<ShortFilmVisibility>(form["visibility"].ToString(), true, out var v)
                    ? v
                    : ShortFilmVisibility.Private;

                await using var stream = file.OpenReadStream();
                var result = await dispatcher.Send(new UploadShortFilmCommand(
                    form["title"].ToString(), form["synopsis"].ToString(), origin, visibility,
                    file.FileName, file.ContentType, stream, file.Length), ct);

                return result.IsSuccess ? Results.Ok(result.Value) : Results.BadRequest(result.Error);
            })
        .WithName("UploadShortFilm").WithTags("Shorts").RequireAuthorization().RequireRateLimiting(AppPolicies.WriteRateLimit)
        // Kestrel (30 MB) and the form reader (128 MB) cut uploads off well below the 512 MB the
        // handler allows. Raise both to match, for this endpoint only.
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(UploadShortFilmHandler.MaxBytes + 1024 * 1024))
        .WithFormOptions(multipartBodyLengthLimit: UploadShortFilmHandler.MaxBytes + 1024 * 1024)
        .DisableAntiforgery();
    }
}
