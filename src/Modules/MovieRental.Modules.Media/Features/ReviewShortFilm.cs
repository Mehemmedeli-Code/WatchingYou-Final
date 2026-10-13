using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Media.Domain;
using MovieRental.Modules.Media.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Media.Features;

// Stage two. Admin only ever sees what Security has already inspected, and the stored
// report travels with it so the final call is made on evidence rather than a title.

public sealed record GetAdminQueueQuery : IQuery<IReadOnlyList<ShortFilmDetail>>;

internal sealed class GetAdminQueueHandler(MediaDbContext db)
    : IQueryHandler<GetAdminQueueQuery, IReadOnlyList<ShortFilmDetail>>
{
    public async Task<IReadOnlyList<ShortFilmDetail>> Handle(GetAdminQueueQuery query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var films = await db.ShortFilms.AsNoTracking()
            .Include(f => f.SecurityReport!).ThenInclude(r => r.Checks)
            .Include(f => f.Comments)
            .Where(f => f.Status == SubmissionStatus.SecurityCleared || f.Status == SubmissionStatus.SecurityFlagged)
            .OrderBy(f => f.ReviewDeadlineUtc)
            .ToListAsync(ct);

        return [.. films.Select(f => new ShortFilmDetail(
            f.ToSummary(now), f.SecurityReport?.ToDto(),
            [.. f.Comments.OrderBy(c => c.CreatedAtUtc).Select(c => c.ToDto())]))];
    }
}

public sealed record DecideShortFilmCommand(
    Guid Id, bool Approve, string? Note, ShortFilmOrigin? CorrectOriginTo) : ICommand<Result>;

internal sealed class DecideShortFilmHandler(
    MediaDbContext db, ICurrentUser currentUser, IUserDirectory users, IEmailSender email, IAuditLog audit)
    : ICommandHandler<DecideShortFilmCommand, Result>
{
    public async Task<Result> Handle(DecideShortFilmCommand command, CancellationToken ct)
    {
        var film = await db.ShortFilms
            .Include(f => f.SecurityReport!).ThenInclude(r => r.Checks)
            .FirstOrDefaultAsync(f => f.Id == command.Id, ct);

        if (film is null) return Result.Failure(Error.NotFound("Submission"));
        if (film.IsDecided) return Result.Failure(Error.Conflict("This submission has already been decided."));

        // The gate that makes the two-stage pipeline real rather than advisory.
        if (film.SecurityReport is null)
            return Result.Failure(Error.Conflict("Security has not filed a report for this submission yet."));

        // Overriding a flagged film is allowed, but never silently.
        if (film.SecurityReport.Verdict == SecurityVerdict.Flagged && command.Approve &&
            string.IsNullOrWhiteSpace(command.Note))
            return Result.Failure(Error.Validation("Security flagged this film. Record why you are approving it anyway."));

        if (command.CorrectOriginTo is { } corrected) film.Origin = corrected;

        film.Status = command.Approve ? SubmissionStatus.Approved : SubmissionStatus.Rejected;
        film.ReviewedAtUtc = DateTime.UtcNow;
        film.ReviewedByUserId = currentUser.RequireId();
        film.ReviewerNote = command.Note?.Trim();
        film.ApprovedAtUtc = command.Approve ? DateTime.UtcNow : null;

        await db.SaveChangesAsync(ct);

        // Overriding Security is exactly the act worth having on record.
        await audit.RecordAsync(new AuditEntry(
            command.Approve ? "shortfilm.approved" : "shortfilm.rejected",
            film.Title,
            film.SecurityReport.Verdict == SecurityVerdict.Flagged && command.Approve
                ? $"Approved over a Security flag: {command.Note}"
                : command.Note,
            film.Id), ct);

        await NotifyAsync(film, command.Approve, ct);
        return Result.Success();
    }

    private async Task NotifyAsync(ShortFilm film, bool approved, CancellationToken ct)
    {
        var contact = await users.GetContactAsync(film.UserId, ct);
        if (contact is null) return;

        var failures = film.SecurityReport?.Failures.ToArray() ?? [];
        var failureList = failures.Length == 0
            ? string.Empty
            : $"<p>{Lang.Pick("Keçməyən yoxlamalar:", "Checks that did not pass:", "Непройденные проверки:", "Geçmeyen kontroller:")}</p><ul>" +
              string.Join("", failures.Select(f =>
                  $"<li><strong>{f.Check}</strong>{(string.IsNullOrWhiteSpace(f.Note) ? "" : $" — {f.Note}")}</li>")) +
              "</ul>";

        var gallery = film.Origin == ShortFilmOrigin.AiGenerated ? "AI Catalog" : "Human Craft";
        var next = "<p>" + (approved
            ? film.Visibility == ShortFilmVisibility.Public
                ? Lang.Pick($"Film artıq {gallery} qalereyasında yayımdadır.", $"It is live in the {gallery} gallery now.",
                            $"Фильм уже опубликован в галерее {gallery}.", $"Film artık {gallery} galerisinde yayında.")
                : Lang.Pick($"Film təsdiqləndi. {gallery} qalereyasında görünməsi üçün Studiyada açıq edin.",
                            $"It is approved. Switch it to public in Studio whenever you want it to appear in {gallery}.",
                            $"Фильм одобрен. Сделайте его публичным в Студии, чтобы он появился в {gallery}.",
                            $"Film onaylandı. {gallery} galerisinde görünmesi için Stüdyo'da herkese açık yapın.")
            : Lang.Pick("Düzəliş edib yenidən göndərə bilərsiniz.", "You are welcome to revise it and submit again.",
                        "Вы можете доработать фильм и отправить его снова.", "Düzeltip yeniden gönderebilirsiniz.")) + "</p>";

        var subject = approved
            ? Lang.Pick($"\"{film.Title}\" təsdiqləndi", $"\"{film.Title}\" was approved", $"«{film.Title}» одобрен", $"\"{film.Title}\" onaylandı")
            : Lang.Pick($"\"{film.Title}\" qəbul olunmadı", $"\"{film.Title}\" was not accepted", $"«{film.Title}» не принят", $"\"{film.Title}\" kabul edilmedi");

        await email.SendAsync(new EmailRequest(contact.Email, subject,
            $"""
             <p>{Lang.Pick("Salam", "Hi", "Здравствуйте", "Merhaba")} {System.Net.WebUtility.HtmlEncode(contact.FullName)},</p>
             <p>{Lang.Pick("Göndərdiyiniz film yoxlanıldı:", "Your submission has been reviewed:", "Ваша работа проверена:", "Gönderiniz incelendi:")} <strong>{System.Net.WebUtility.HtmlEncode(film.Title)}</strong></p>
             {(string.IsNullOrWhiteSpace(film.ReviewerNote) ? "" : $"<p>{System.Net.WebUtility.HtmlEncode(film.ReviewerNote)}</p>")}
             {(string.IsNullOrWhiteSpace(film.SecurityReport?.Summary) ? "" : $"<p><em>{System.Net.WebUtility.HtmlEncode(film.SecurityReport!.Summary)}</em></p>")}
             {failureList}
             {next}
             """), ct);
    }
}

public static class ReviewShortFilmEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/shorts").WithTags("Shorts").RequireAuthorization(AppRoles.Admin);

        admin.MapGet("/queue", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetAdminQueueQuery(), ct)))
            .WithName("GetAdminShortsQueue");

        admin.MapPut("/{id:guid}/decision",
            async Task<Results<NoContent, BadRequest<Error>, Conflict<Error>, NotFound<Error>>> (
                Guid id, DecideShortFilmCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { Id = id }, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "conflict" => TypedResults.Conflict(result.Error),
                    _ => TypedResults.BadRequest(result.Error)
                };
            }).WithName("DecideShortFilmWithId");

        admin.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (
            Guid id, MediaDbContext db, CancellationToken ct) =>
        {
            var film = await db.ShortFilms.FirstOrDefaultAsync(f => f.Id == id, ct);
            if (film is null) return TypedResults.NotFound();

            db.ShortFilms.Remove(film);   // soft delete — the file stays for any appeal
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }).WithName("SoftDeleteShortFilmWithId");
    }
}
