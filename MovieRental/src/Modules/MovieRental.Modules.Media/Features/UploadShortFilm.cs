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
    private const long MaxBytes = 512L * 1024 * 1024;
    private static readonly string[] AllowedExtensions = [".mp4", ".mov", ".webm", ".mkv"];

    public async Task<Result<ShortFilmSummary>> Handle(UploadShortFilmCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            return Result.Failure<ShortFilmSummary>(Error.Validation("Give your film a title."));
        if (command.SizeBytes is 0 or > MaxBytes)
            return Result.Failure<ShortFilmSummary>(Error.Validation("Upload a file between 1 byte and 512 MB."));

        var extension = Path.GetExtension(command.OriginalFileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            return Result.Failure<ShortFilmSummary>(Error.Validation($"Supported formats: {string.Join(", ", AllowedExtensions)}."));

        var userId = currentUser.RequireId();
        var contact = await users.GetContactAsync(userId, ct);

        // Stored under a generated name: the uploader's filename never reaches the file
        // system, so path traversal and collisions are both off the table.
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var folder = ShortFilmStorage.Folder(environment);
        Directory.CreateDirectory(folder);

        await using (var target = File.Create(Path.Combine(folder, storedName)))
            await command.Content.CopyToAsync(target, ct);

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
            ContentType = command.ContentType,
            SizeBytes = command.SizeBytes,
            SubmittedAtUtc = DateTime.UtcNow,
            ReviewDeadlineUtc = DateTime.UtcNow.AddDays(3)
        };

        db.ShortFilms.Add(film);
        await db.SaveChangesAsync(ct);
        return Result.Success(film.ToSummary(DateTime.UtcNow));
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
        .WithName("UploadShortFilm").WithTags("Shorts").RequireAuthorization()
        .DisableAntiforgery();
    }
}
