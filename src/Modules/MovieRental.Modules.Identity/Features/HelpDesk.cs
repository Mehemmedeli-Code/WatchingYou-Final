using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// Help service: a customer writes, the Security desk reads and answers.
//
// What travels with a message is the sender's name, e-mail and what they wrote. Nothing else
// about the account is exposed to the desk — no password, obviously, but also no roles, no
// phone, no bookings. Staff answering a question do not need them, and a support screen that
// showed them would be a standing invitation to misuse.

public sealed record HelpReplyItem(
    Guid Id, string AuthorName, string AuthorRole, string Body, DateTime CreatedAtUtc);

public sealed record HelpThread(
    Guid Id, string UserEmail, string UserName, string Subject, string Body,
    string Status, DateTime CreatedAtUtc, IReadOnlyList<HelpReplyItem> Replies);

public sealed record SendHelpMessageCommand(string Subject, string Body) : ICommand<Result<HelpThread>>;

internal sealed class SendHelpMessageValidator : AbstractValidator<SendHelpMessageCommand>
{
    public SendHelpMessageValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
    }
}

internal sealed class SendHelpMessageHandler(IdentityDbContext db, ICurrentUser currentUser)
    : ICommandHandler<SendHelpMessageCommand, Result<HelpThread>>
{
    public async Task<Result<HelpThread>> Handle(SendHelpMessageCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
        if (user is null) return Result.Failure<HelpThread>(Error.NotFound("User"));

        // Taken from the signed-in account rather than a form field: a sender address anyone
        // could type is a sender address anyone could forge.
        var message = new SupportMessage
        {
            UserId = user.Id,
            UserEmail = user.Email,
            UserName = user.FullName,
            Subject = command.Subject.Trim(),
            Body = command.Body.Trim()
        };

        db.SupportMessages.Add(message);
        await db.SaveChangesAsync(ct);

        return Result.Success(HelpMapper.ToThread(message));
    }
}

public sealed record GetMyHelpThreadsQuery : IQuery<IReadOnlyList<HelpThread>>;

internal sealed class GetMyHelpThreadsHandler(IdentityDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMyHelpThreadsQuery, IReadOnlyList<HelpThread>>
{
    public async Task<IReadOnlyList<HelpThread>> Handle(GetMyHelpThreadsQuery query, CancellationToken ct)
    {
        var mine = await db.SupportMessages.AsNoTracking()
            .Include(m => m.Replies)
            .Where(m => m.UserId == currentUser.RequireId())
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(50)
            .ToListAsync(ct);

        return [.. mine.Select(HelpMapper.ToThread)];
    }
}

public sealed record GetHelpInboxQuery(bool IncludeClosed) : IQuery<IReadOnlyList<HelpThread>>;

internal sealed class GetHelpInboxHandler(IdentityDbContext db)
    : IQueryHandler<GetHelpInboxQuery, IReadOnlyList<HelpThread>>
{
    public async Task<IReadOnlyList<HelpThread>> Handle(GetHelpInboxQuery query, CancellationToken ct)
    {
        var inbox = db.SupportMessages.AsNoTracking().Include(m => m.Replies);

        var threads = await (query.IncludeClosed ? inbox : inbox.Where(m => m.Status != SupportStatus.Closed))
            // Oldest first: the person who has waited longest is served first.
            .OrderBy(m => m.Status == SupportStatus.Open ? 0 : 1)
            .ThenBy(m => m.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);

        return [.. threads.Select(HelpMapper.ToThread)];
    }
}

public sealed record ReplyToHelpCommand(Guid MessageId, string Body) : ICommand<Result<HelpReplyItem>>;

internal sealed class ReplyToHelpHandler(IdentityDbContext db, ICurrentUser currentUser)
    : ICommandHandler<ReplyToHelpCommand, Result<HelpReplyItem>>
{
    public async Task<Result<HelpReplyItem>> Handle(ReplyToHelpCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Body))
            return Result.Failure<HelpReplyItem>(Error.Validation("Write something first."));

        var message = await db.SupportMessages.FirstOrDefaultAsync(m => m.Id == command.MessageId, ct);
        if (message is null) return Result.Failure<HelpReplyItem>(Error.NotFound("Message"));

        var userId = currentUser.RequireId();
        var isDesk = currentUser.IsInRole(AppRoles.Security) || currentUser.IsInRole(AppRoles.Admin);

        // Either the person who wrote it, or the desk. Nobody else can read the thread, so
        // nobody else can add to it.
        if (message.UserId != userId && !isDesk)
            return Result.Failure<HelpReplyItem>(Error.Forbidden("This conversation is not yours."));

        var author = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

        var reply = new SupportReply
        {
            MessageId = message.Id,
            AuthorUserId = userId,
            AuthorName = author?.FullName ?? "Unknown",
            AuthorRole = isDesk && message.UserId != userId ? AppRoles.Security : "Customer",
            Body = command.Body.Trim()
        };

        db.SupportReplies.Add(reply);

        if (reply.AuthorRole == AppRoles.Security)
        {
            message.Status = SupportStatus.Answered;
            message.AnsweredAtUtc = DateTime.UtcNow;
        }
        else if (message.Status == SupportStatus.Answered)
        {
            // The customer came back, so it is open again rather than quietly finished.
            message.Status = SupportStatus.Open;
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(HelpMapper.ToItem(reply));
    }
}

public sealed record CloseHelpThreadCommand(Guid MessageId) : ICommand<Result>;

internal sealed class CloseHelpThreadHandler(IdentityDbContext db)
    : ICommandHandler<CloseHelpThreadCommand, Result>
{
    public async Task<Result> Handle(CloseHelpThreadCommand command, CancellationToken ct)
    {
        var message = await db.SupportMessages.FirstOrDefaultAsync(m => m.Id == command.MessageId, ct);
        if (message is null) return Result.Failure(Error.NotFound("Message"));

        message.Status = SupportStatus.Closed;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal static class HelpMapper
{
    public static HelpReplyItem ToItem(SupportReply reply) =>
        new(reply.Id, reply.AuthorName, reply.AuthorRole, reply.Body, reply.CreatedAtUtc);

    public static HelpThread ToThread(SupportMessage message) => new(
        message.Id, message.UserEmail, message.UserName, message.Subject, message.Body,
        message.Status.ToString(), message.CreatedAtUtc,
        [.. message.Replies.OrderBy(r => r.CreatedAtUtc).Select(ToItem)]);
}

public static class HelpDeskEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/api/help").WithTags("Help").RequireAuthorization();

        mine.MapPost("", async Task<Results<Ok<HelpThread>, BadRequest<Error>, NotFound<Error>>> (
            SendHelpMessageCommand body, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(body, ct);
            if (result.IsSuccess) return TypedResults.Ok(result.Value);
            return result.Error.Code == "not_found"
                ? TypedResults.NotFound(result.Error)
                : TypedResults.BadRequest(result.Error);
        }).WithName("SendHelpMessage");

        mine.MapGet("/mine", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetMyHelpThreadsQuery(), ct)))
            .WithName("GetMyHelpThreads");

        mine.MapPost("/{id:guid}/replies",
            async Task<Results<Ok<HelpReplyItem>, BadRequest<Error>, NotFound<Error>, ForbidHttpResult>> (
                Guid id, ReplyToHelpCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { MessageId = id }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "forbidden" => TypedResults.Forbid(),
                    _ => TypedResults.BadRequest(result.Error)
                };
            }).WithName("ReplyToHelpWithId");

        var desk = app.MapGroup("/api/help/inbox").WithTags("Help")
            .RequireAuthorization(AppPolicies.SecurityDesk);

        desk.MapGet("", async (bool? includeClosed, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetHelpInboxQuery(includeClosed ?? false), ct)))
            .WithName("GetHelpInbox");

        desk.MapPost("/{id:guid}/close",
            async Task<Results<NoContent, NotFound<Error>>> (
                Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new CloseHelpThreadCommand(id), ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound(result.Error);
            }).WithName("CloseHelpThreadWithId");
    }
}
