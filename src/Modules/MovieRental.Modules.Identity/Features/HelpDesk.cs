using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// Live help: the customer types, the Security desk answers, both watch the same thread.
//
// What the desk receives is the sender's name, e-mail and what they wrote — taken from the
// signed-in session, never from a form field, so an address cannot be forged. Nothing else
// about the account crosses over: no roles, no phone, no bookings, and no password. Passwords
// are not stored in readable form anywhere and would not be shown here if they were.

public sealed record ChatLine(
    Guid Id, bool FromDesk, string AuthorName, string Body, DateTime CreatedAtUtc);

public sealed record ChatThread(
    Guid Id, string UserEmail, string UserName, string Status,
    DateTime LastMessageAtUtc, int UnreadForDesk, IReadOnlyList<ChatLine> Messages);

/// <summary>What the desk's list shows: who is waiting, and the last thing they said.</summary>
public sealed record InboxRow(
    Guid Id, string UserEmail, string UserName, string Status,
    DateTime LastMessageAtUtc, int Unread, string Preview);

// ---------------------------------------------------------------- customer side

public sealed record GetMyChatQuery : IQuery<ChatThread?>;

internal sealed class GetMyChatHandler(IdentityDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMyChatQuery, ChatThread?>
{
    public async Task<ChatThread?> Handle(GetMyChatQuery query, CancellationToken ct)
    {
        // A pure read. It used to mark the desk's replies as seen, which made every four-second
        // poll a database write — and a write racing the customer's own send, which is where
        // the "someone else changed this first" conflicts came from. The read receipt was only
        // ever used for the desk's unread count, so the customer's side does not need one at all.
        var conversation = await db.SupportConversations.AsNoTracking()
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.UserId == currentUser.RequireId(), ct);

        return conversation is null ? null : HelpMapper.ToThread(conversation);
    }
}

public sealed record SendChatMessageCommand(string Body) : ICommand<Result<ChatThread>>;

internal sealed class SendChatMessageValidator : AbstractValidator<SendChatMessageCommand>
{
    public SendChatMessageValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
}

internal sealed class SendChatMessageHandler(IdentityDbContext db, ICurrentUser currentUser, IRealtimeNotifier realtime)
    : ICommandHandler<SendChatMessageCommand, Result<ChatThread>>
{
    public async Task<Result<ChatThread>> Handle(SendChatMessageCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireId(), ct);
        if (user is null) return Result.Failure<ChatThread>(Error.NotFound("User"));

        // Only the conversation row is tracked. Loading every message just to append one gives
        // EF a graph it might try to update, and none of it needs writing.
        var conversation = await db.SupportConversations
            .FirstOrDefaultAsync(c => c.UserId == user.Id, ct);

        if (conversation is null)
        {
            conversation = new SupportConversation
            {
                UserId = user.Id,
                UserEmail = user.Email,
                UserName = user.FullName
            };
            db.SupportConversations.Add(conversation);
        }

        db.SupportChatMessages.Add(new SupportChatMessage
        {
            Conversation = conversation,
            FromDesk = false,
            AuthorUserId = user.Id,
            AuthorName = user.FullName,
            Body = command.Body.Trim()
        });

        // A closed thread reopens when the customer writes again. Anything else would make
        // them start over to ask a follow-up.
        conversation.Status = SupportStatus.Open;
        conversation.LastMessageAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await realtime.HelpForDeskAsync(conversation.Id, ct);
        return Result.Success(await HelpMapper.ReadAsync(db, conversation.Id, ct));
    }
}

// ---------------------------------------------------------------- desk side

public sealed record GetInboxQuery(bool IncludeClosed) : IQuery<IReadOnlyList<InboxRow>>;

internal sealed class GetInboxHandler(IdentityDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetInboxQuery, IReadOnlyList<InboxRow>>
{
    public async Task<IReadOnlyList<InboxRow>> Handle(GetInboxQuery query, CancellationToken ct)
    {
        var me = currentUser.RequireId();

        // Only other people's conversations. The desk is the Help Service; its own thread is
        // not a request waiting on it.
        var conversations = db.SupportConversations.AsNoTracking()
            .Include(c => c.Messages)
            .Where(c => c.UserId != me);

        var rows = await (query.IncludeClosed
                ? conversations
                : conversations.Where(c => c.Status != SupportStatus.Closed))
            .OrderByDescending(c => c.LastMessageAtUtc)
            .Take(200)
            .ToListAsync(ct);

        return [.. rows.Select(c => new InboxRow(
            c.Id, c.UserEmail, c.UserName, c.Status.ToString(), c.LastMessageAtUtc,
            c.Messages.Count(m => !m.FromDesk && m.SeenAtUtc is null),
            c.Messages.OrderByDescending(m => m.CreatedAtUtc).FirstOrDefault()?.Body is { } last
                ? last[..Math.Min(last.Length, 90)]
                : ""))];
    }
}

public sealed record GetChatQuery(Guid ConversationId) : IQuery<ChatThread?>;

internal sealed class GetChatHandler(IdentityDbContext db) : IQueryHandler<GetChatQuery, ChatThread?>
{
    public async Task<ChatThread?> Handle(GetChatQuery query, CancellationToken ct)
    {
        // Opening a conversation clears its unread mark. One set-based UPDATE rather than
        // loading rows, changing them and saving: nothing is tracked, so two agents opening
        // the same thread at the same moment cannot collide.
        await db.SupportChatMessages
            .Where(m => m.ConversationId == query.ConversationId && !m.FromDesk && m.SeenAtUtc == null)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.SeenAtUtc, DateTime.UtcNow), ct);

        var conversation = await db.SupportConversations.AsNoTracking()
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == query.ConversationId, ct);

        return conversation is null ? null : HelpMapper.ToThread(conversation);
    }
}

public sealed record DeskReplyCommand(Guid ConversationId, string Body) : ICommand<Result<ChatThread>>;

internal sealed class DeskReplyHandler(IdentityDbContext db, ICurrentUser currentUser, IRealtimeNotifier realtime)
    : ICommandHandler<DeskReplyCommand, Result<ChatThread>>
{
    public async Task<Result<ChatThread>> Handle(DeskReplyCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Body))
            return Result.Failure<ChatThread>(Error.Validation("Write something first."));

        var conversation = await db.SupportConversations
            .FirstOrDefaultAsync(c => c.Id == command.ConversationId, ct);

        if (conversation is null) return Result.Failure<ChatThread>(Error.NotFound("Conversation"));

        var deskUserId = currentUser.RequireId();
        var agent = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == deskUserId, ct);

        db.SupportChatMessages.Add(new SupportChatMessage
        {
            Conversation = conversation,
            FromDesk = true,
            AuthorUserId = deskUserId,
            // The agent's own name, so the customer knows they are talking to a person.
            AuthorName = agent?.FullName ?? "Security",
            Body = command.Body.Trim()
        });

        conversation.Status = SupportStatus.Answered;
        conversation.LastMessageAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await realtime.HelpForCustomerAsync(conversation.UserId, conversation.Id, ct);
        // Other agents watching the same inbox see the reply too, so two people do not answer
        // the same question.
        await realtime.HelpForDeskAsync(conversation.Id, ct);
        return Result.Success(await HelpMapper.ReadAsync(db, conversation.Id, ct));
    }
}

public sealed record CloseChatCommand(Guid ConversationId) : ICommand<Result>;

internal sealed class CloseChatHandler(IdentityDbContext db, IRealtimeNotifier realtime) : ICommandHandler<CloseChatCommand, Result>
{
    public async Task<Result> Handle(CloseChatCommand command, CancellationToken ct)
    {
        var conversation = await db.SupportConversations
            .FirstOrDefaultAsync(c => c.Id == command.ConversationId, ct);

        if (conversation is null) return Result.Failure(Error.NotFound("Conversation"));

        conversation.Status = SupportStatus.Closed;
        await db.SaveChangesAsync(ct);
        await realtime.HelpForCustomerAsync(conversation.UserId, conversation.Id, ct);
        await realtime.HelpForDeskAsync(conversation.Id, ct);
        return Result.Success();
    }
}

internal static class HelpMapper
{
    /// <summary>Re-reads the thread after a write, so the reply carries the full conversation
    /// without the writing handler having had to load it first.</summary>
    public static async Task<ChatThread> ReadAsync(IdentityDbContext db, Guid conversationId, CancellationToken ct)
    {
        var conversation = await db.SupportConversations.AsNoTracking()
            .Include(c => c.Messages)
            .FirstAsync(c => c.Id == conversationId, ct);

        return ToThread(conversation);
    }

    public static ChatThread ToThread(SupportConversation c) => new(
        c.Id, c.UserEmail, c.UserName, c.Status.ToString(), c.LastMessageAtUtc,
        c.Messages.Count(m => !m.FromDesk && m.SeenAtUtc is null),
        [.. c.Messages
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => new ChatLine(m.Id, m.FromDesk, m.AuthorName, m.Body, m.CreatedAtUtc))]);
}

public static class HelpDeskEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/api/help").WithTags("Help").RequireAuthorization();

        // Null when they have never written: the page opens with a greeting rather than an
        // error, and the thread is created by the first message.
        mine.MapGet("/chat", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetMyChatQuery(), ct)))
            .WithName("GetMyChat");

        mine.MapPost("/chat", async Task<Results<Ok<ChatThread>, BadRequest<Error>, NotFound<Error>>> (
            SendChatMessageCommand body, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(body, ct);
            if (result.IsSuccess) return TypedResults.Ok(result.Value);
            return result.Error.Code == "not_found"
                ? TypedResults.NotFound(result.Error)
                : TypedResults.BadRequest(result.Error);
        }).WithName("SendChatMessage");

        var desk = app.MapGroup("/api/help/inbox").WithTags("Help")
            .RequireAuthorization(AppPolicies.SecurityDesk);

        desk.MapGet("", async (bool? includeClosed, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetInboxQuery(includeClosed ?? false), ct)))
            .WithName("GetHelpInbox");

        desk.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var thread = await dispatcher.Ask(new GetChatQuery(id), ct);
                return thread is null ? Results.NotFound() : Results.Ok(thread);
            })
            .WithName("GetHelpChatWithId");

        desk.MapPost("/{id:guid}", async Task<Results<Ok<ChatThread>, BadRequest<Error>, NotFound<Error>>> (
            Guid id, DeskReplyCommand body, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(body with { ConversationId = id }, ct);
            if (result.IsSuccess) return TypedResults.Ok(result.Value);
            return result.Error.Code == "not_found"
                ? TypedResults.NotFound(result.Error)
                : TypedResults.BadRequest(result.Error);
        }).WithName("DeskReplyWithId");

        desk.MapPost("/{id:guid}/close", async Task<Results<NoContent, NotFound<Error>>> (
            Guid id, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(new CloseChatCommand(id), ct);
            return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound(result.Error);
        }).WithName("CloseHelpChatWithId");
    }
}
