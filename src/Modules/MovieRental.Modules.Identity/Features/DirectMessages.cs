using System.Globalization;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieRental.Modules.Identity.Domain;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Cqrs;
using MovieRental.SharedKernel.Results;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Features;

// Members writing to each other from the globe.
//
// Three rules shape this, and none of them are optional in a feature that lets strangers reach
// strangers:
//
//  1. Only people who put themselves on the globe can be written to. Appearing there is a
//     choice; being reachable follows from it rather than from having registered.
//  2. Anyone can block anyone, one-sidedly and instantly. Blocking is a decision about who may
//     reach you, not a negotiation.
//  3. Anyone can report a message to the Security desk, and the text is copied into the report
//     so it survives the sender deleting it.
//  4. A private account is written to by request: the first message goes, and nothing more until
//     they accept. Declined, the thread simply stays closed - the writer is not told.

public sealed record DirectReplyPreview(Guid Id, string SenderName, string Snippet, bool Deleted);
public sealed record DirectReaction(string Emoji, int Count, bool Mine);

public sealed record DirectLine(
    Guid Id, bool Mine, string SenderName, string Body, DateTime CreatedAtUtc,
    DateTime? SeenAtUtc = null, DateTime? EditedAtUtc = null, bool Deleted = false, bool Forwarded = false,
    DirectReplyPreview? ReplyTo = null, IReadOnlyList<DirectReaction>? Reactions = null, bool Starred = false);

/// <param name="HasOlder">More messages exist before the first one here; ask with ?before=.</param>
/// <param name="Request">"none" (open), "sent" (my request, waiting), "received" (their request
/// to me) or "declined" (theirs, which I turned down).</param>
public sealed record DirectConversation(
    Guid OtherUserId, string OtherName, string? OtherAvatarUrl, string? OtherCity,
    bool BlockedByMe, bool Unreachable, IReadOnlyList<DirectLine> Messages, bool HasOlder = false,
    string Request = "none", DirectLine? Pinned = null);

public sealed record DirectThreadRow(
    Guid OtherUserId, string OtherName, string? OtherAvatarUrl,
    string Preview, DateTime LastMessageAtUtc, int Unread, string Request = "none");

internal static class DirectReach
{
    /// <summary>The other person has written in this pair's thread: they can be answered even
    /// after leaving the globe, or without ever having been on it.</summary>
    public static Task<bool> TheyWroteAsync(IdentityDbContext db, Guid low, Guid high, Guid other, CancellationToken ct) =>
        db.DirectMessages.AnyAsync(m => m.Thread!.LowUserId == low && m.Thread.HighUserId == high && m.SenderId == other, ct);
}

/// <summary>The request state of a thread as one participant sees it.</summary>
internal static class DirectRequest
{
    public static string For(DirectThread? thread, Guid me) => For(thread?.RequestedById, thread?.RequestDeclined ?? false, me);

    public static string For(Guid? requestedById, bool declined, Guid me) =>
        requestedById is not { } by ? "none"
        : by == me ? "sent"                     // declined or not: the writer only ever waits
        : declined ? "declined" : "received";
}

public sealed record GetMyThreadsQuery : IQuery<IReadOnlyList<DirectThreadRow>>;

internal sealed class GetMyThreadsHandler(IdentityDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMyThreadsQuery, IReadOnlyList<DirectThreadRow>>
{
    public async Task<IReadOnlyList<DirectThreadRow>> Handle(GetMyThreadsQuery query, CancellationToken ct)
    {
        var me = currentUser.RequireId();

        // The last message and the unread count are worked out in SQL. Loading every message of
        // a hundred threads to read one line from each grew with every conversation.
        var threads = await db.DirectThreads.AsNoTracking()
            .Where(t => t.LowUserId == me || t.HighUserId == me)
            .OrderByDescending(t => t.LastMessageAtUtc)
            .Take(100)
            // A request I turned down is gone from my list; the writer still sees theirs.
            .Where(t => !(t.RequestDeclined && t.RequestedById != me))
            .Select(t => new
            {
                t.LowUserId, t.HighUserId, t.LastMessageAtUtc, t.RequestedById, t.RequestDeclined,
                Last = t.Messages.OrderByDescending(m => m.CreatedAtUtc).Select(m => m.Body).FirstOrDefault(),
                Unread = t.Messages.Count(m => m.SenderId != me && m.SeenAtUtc == null)
            })
            .ToListAsync(ct);

        var otherIds = threads.Select(t => t.LowUserId == me ? t.HighUserId : t.LowUserId).ToArray();
        var people = await db.Users.AsNoTracking()
            .Where(u => otherIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.AvatarUrl })
            .ToDictionaryAsync(u => u.Id, ct);

        return [.. threads.Select(t =>
        {
            var otherId = t.LowUserId == me ? t.HighUserId : t.LowUserId;
            var other = people.GetValueOrDefault(otherId);

            return new DirectThreadRow(
                otherId, other?.FullName ?? "—", other?.AvatarUrl,
                t.Last is { } body ? body[..Math.Min(body.Length, 90)] : "",
                t.LastMessageAtUtc,
                t.Unread,
                DirectRequest.For(t.RequestedById, t.RequestDeclined, me));
        })];
    }
}

// Everything under the globe in one call: whom I wrote to, who wrote to me, whom I blocked
// and who blocked me.
public sealed record MessagePerson(
    Guid UserId, string Name, string? AvatarUrl, string? City, string? CountryCode,
    string? Preview, bool LastWasMine, int Count, DateTime AtUtc);

public sealed record MessageOverview(
    IReadOnlyList<MessagePerson> Sent,
    IReadOnlyList<MessagePerson> Received,
    IReadOnlyList<MessagePerson> BlockedByMe,
    IReadOnlyList<MessagePerson> BlockedMe);

public sealed record GetMessageOverviewQuery : IQuery<MessageOverview>;

internal sealed class GetMessageOverviewHandler(IdentityDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetMessageOverviewQuery, MessageOverview>
{
    public async Task<MessageOverview> Handle(GetMessageOverviewQuery query, CancellationToken ct)
    {
        var me = currentUser.RequireId();

        var threads = await db.DirectThreads.AsNoTracking()
            .Where(t => t.LowUserId == me || t.HighUserId == me)
            .OrderByDescending(t => t.LastMessageAtUtc)
            .Take(200)
            .Select(t => new
            {
                t.LowUserId, t.HighUserId,
                MineCount = t.Messages.Count(m => m.SenderId == me),
                Mine = t.Messages.Where(m => m.SenderId == me).OrderByDescending(m => m.CreatedAtUtc)
                    .Select(m => new { m.Body, m.CreatedAtUtc }).FirstOrDefault(),
                TheirsCount = t.Messages.Count(m => m.SenderId != me),
                Theirs = t.Messages.Where(m => m.SenderId != me).OrderByDescending(m => m.CreatedAtUtc)
                    .Select(m => new { m.Body, m.CreatedAtUtc }).FirstOrDefault(),
                LastSender = t.Messages.OrderByDescending(m => m.CreatedAtUtc).Select(m => (Guid?)m.SenderId).FirstOrDefault()
            })
            .ToListAsync(ct);

        var blocks = await db.UserBlocks.AsNoTracking()
            .Where(b => b.BlockerId == me || b.BlockedId == me)
            .ToListAsync(ct);

        var ids = threads.Select(t => t.LowUserId == me ? t.HighUserId : t.LowUserId)
            .Concat(blocks.Select(b => b.BlockerId == me ? b.BlockedId : b.BlockerId))
            .Distinct().ToArray();

        var people = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName, u.AvatarUrl, u.City, u.CountryCode })
            .ToDictionaryAsync(u => u.Id, ct);

        MessagePerson Person(Guid id, string? preview, bool lastMine, int count, DateTime at)
        {
            var p = people.GetValueOrDefault(id);
            return new MessagePerson(id, p?.FullName ?? "—", p?.AvatarUrl, p?.City, p?.CountryCode,
                preview, lastMine, count, at);
        }

        List<MessagePerson> sent = [], received = [];
        foreach (var thread in threads)
        {
            var other = thread.LowUserId == me ? thread.HighUserId : thread.LowUserId;
            var lastMine = thread.LastSender == me;
            static string Cut(string body) => body[..Math.Min(body.Length, 90)];

            if (thread.Mine is { } mine)
                sent.Add(Person(other, Cut(mine.Body), lastMine, thread.MineCount, mine.CreatedAtUtc));
            if (thread.Theirs is { } theirs)
                received.Add(Person(other, Cut(theirs.Body), lastMine, thread.TheirsCount, theirs.CreatedAtUtc));
        }

        return new MessageOverview(
            sent,
            received,
            [.. blocks.Where(b => b.BlockerId == me).OrderByDescending(b => b.CreatedAtUtc)
                .Select(b => Person(b.BlockedId, b.Reason, false, 0, b.CreatedAtUtc))],
            [.. blocks.Where(b => b.BlockedId == me).OrderByDescending(b => b.CreatedAtUtc)
                .Select(b => Person(b.BlockerId, null, false, 0, b.CreatedAtUtc))]);
    }
}

/// <param name="Before">Only messages older than this: the page above the one already shown.</param>
public sealed record GetConversationQuery(Guid OtherUserId, DateTime? Before = null) : IQuery<DirectConversation?>;

internal sealed class GetConversationHandler(IdentityDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetConversationQuery, DirectConversation?>
{
    public async Task<DirectConversation?> Handle(GetConversationQuery query, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        if (me == query.OtherUserId) return null;

        var other = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == query.OtherUserId, ct);
        if (other is null) return null;

        var (low, high) = DirectThread.Pair(me, query.OtherUserId);

        var thread = await db.DirectThreads.AsNoTracking()
            .FirstOrDefaultAsync(t => t.LowUserId == low && t.HighUserId == high, ct);

        // A page at a time, newest first. The window polls, and every poll used to carry the
        // whole history of the conversation.
        const int pageSize = 50;
        var page = thread is null ? [] : await db.DirectMessages.AsNoTracking()
            .Where(m => m.ThreadId == thread.Id && (query.Before == null || m.CreatedAtUtc < query.Before))
            .Where(m => !db.DirectMessageMarks.Any(k => k.MessageId == m.Id && k.UserId == me && k.Hidden))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(pageSize + 1)
            .ToListAsync(ct);
        var hasOlder = page.Count > pageSize;
        if (hasOlder) page.RemoveAt(page.Count - 1);

        // Name and city only for someone on the globe or already in a conversation with me;
        // any user id used to return both.
        if (thread is null && !other.ShareOnGlobe) return null;
        var theyWrote = thread is not null && await db.DirectMessages.AnyAsync(m => m.ThreadId == thread.Id && m.SenderId == other.Id, ct);

        // Marking messages read is a set-based update: the window polls, and a tracked write on
        // every poll is how a chat starts throwing concurrency errors. A request is not "read"
        // until it is accepted: the writer's ticks stay single while it waits.
        if (thread is not null && thread.RequestedById is null)
        {
            await db.DirectMessages
                .Where(m => m.ThreadId == thread.Id && m.SenderId != me && m.SeenAtUtc == null)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.SeenAtUtc, DateTime.UtcNow), ct);
        }

        var blockedByMe = await db.UserBlocks.AsNoTracking()
            .AnyAsync(b => b.BlockerId == me && b.BlockedId == query.OtherUserId, ct);

        // Either side's block, or the other person having left the globe, closes the door. The
        // reason is deliberately not distinguished: telling somebody they have been blocked
        // invites them to work around it.
        var blockedByThem = await db.UserBlocks.AsNoTracking()
            .AnyAsync(b => b.BlockerId == query.OtherUserId && b.BlockedId == me, ct);

        var pinned = thread?.PinnedMessageId is { } pinId
            ? await db.DirectMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == pinId && m.DeletedAtUtc == null, ct)
            : null;
        var lines = await DirectLines.BuildAsync(db, me, pinned is null ? page : [.. page, pinned], ct);

        return new DirectConversation(
            other.Id, other.FullName, other.AvatarUrl, other.City,
            blockedByMe,
            blockedByMe || blockedByThem || (!other.ShareOnGlobe && !theyWrote) || other.IsSuspended,
            [.. page.OrderBy(m => m.CreatedAtUtc).Select(m => lines[m.Id])],
            hasOlder,
            DirectRequest.For(thread, me),
            pinned is null ? null : lines[pinned.Id]);
    }
}

/// <summary>Messages as one reader sees them: their reactions and stars, the line each one
/// answers, and the text gone from anything deleted for everyone. Three queries for a page.</summary>
internal static class DirectLines
{
    public static async Task<Dictionary<Guid, DirectLine>> BuildAsync(
        IdentityDbContext db, Guid me, IReadOnlyCollection<DirectMessage> messages, CancellationToken ct)
    {
        var ids = messages.Select(m => m.Id).Distinct().ToArray();
        var marks = await db.DirectMessageMarks.AsNoTracking()
            .Where(k => ids.Contains(k.MessageId))
            .ToListAsync(ct);

        var replyIds = messages.Where(m => m.ReplyToId != null).Select(m => m.ReplyToId!.Value).Distinct().ToArray();
        var replies = replyIds.Length == 0 ? [] : await db.DirectMessages.AsNoTracking()
            .Where(m => replyIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        var result = new Dictionary<Guid, DirectLine>();
        foreach (var m in messages)
        {
            if (result.ContainsKey(m.Id)) continue;
            var onThis = marks.Where(k => k.MessageId == m.Id).ToList();
            var reactions = onThis.Where(k => k.Reaction != null)
                .GroupBy(k => k.Reaction!)
                .Select(g => new DirectReaction(g.Key, g.Count(), g.Any(k => k.UserId == me)))
                .ToList();
            var reply = m.ReplyToId is { } rid && replies.GetValueOrDefault(rid) is { } r
                ? new DirectReplyPreview(r.Id, r.SenderName,
                    r.DeletedAtUtc != null ? "" : r.Body[..Math.Min(r.Body.Length, 120)], r.DeletedAtUtc != null)
                : null;
            var deleted = m.DeletedAtUtc != null;
            result[m.Id] = new DirectLine(
                m.Id, m.SenderId == me, m.SenderName, deleted ? "" : m.Body, m.CreatedAtUtc,
                m.SenderId == me ? m.SeenAtUtc : null, m.EditedAtUtc, deleted, m.Forwarded,
                deleted ? null : reply, deleted ? [] : reactions,
                onThis.Any(k => k.UserId == me && k.Starred));
        }
        return result;
    }
}

public sealed record SendDirectMessageCommand(Guid OtherUserId, string Body, Guid? ReplyToId = null, bool Forwarded = false)
    : ICommand<Result<DirectConversation>>;

internal sealed class SendDirectMessageValidator : AbstractValidator<SendDirectMessageCommand>
{
    public SendDirectMessageValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
}

internal sealed class SendDirectMessageHandler(
    IdentityDbContext db, ICurrentUser currentUser, IDispatcher dispatcher, IRealtimeNotifier realtime)
    : ICommandHandler<SendDirectMessageCommand, Result<DirectConversation>>
{
    public async Task<Result<DirectConversation>> Handle(SendDirectMessageCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        if (me == command.OtherUserId)
            return Result.Failure<DirectConversation>(Error.Validation("You cannot message yourself."));

        var sender = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == me, ct);
        var other = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == command.OtherUserId, ct);
        if (sender is null || other is null) return Result.Failure<DirectConversation>(Error.NotFound("User"));

        // Reachability is checked here, not only in the interface. A button that is hidden is
        // not a rule; a rule is something the server enforces. Someone who wrote to me can be
        // answered whether or not they are still on the globe: they started the conversation.
        var (pairLow, pairHigh) = DirectThread.Pair(me, command.OtherUserId);
        if (other.IsSuspended || (!other.ShareOnGlobe && !await DirectReach.TheyWroteAsync(db, pairLow, pairHigh, other.Id, ct)))
            return Result.Failure<DirectConversation>(Error.Conflict("This person is not accepting messages."));

        var blocked = await db.UserBlocks.AsNoTracking().AnyAsync(
            b => (b.BlockerId == command.OtherUserId && b.BlockedId == me)
              || (b.BlockerId == me && b.BlockedId == command.OtherUserId), ct);

        if (blocked)
            return Result.Failure<DirectConversation>(Error.Conflict("This person is not accepting messages."));

        var (low, high) = DirectThread.Pair(me, command.OtherUserId);

        var thread = await db.DirectThreads.FirstOrDefaultAsync(t => t.LowUserId == low && t.HighUserId == high, ct);
        if (thread is null)
        {
            // A private account is written to by request: this first message, and no more.
            thread = new DirectThread { LowUserId = low, HighUserId = high, RequestedById = other.IsPrivate ? me : null };
            db.DirectThreads.Add(thread);
        }
        else if (thread.RequestedById == me)
        {
            return Result.Failure<DirectConversation>(Error.Conflict(
                "Your message request has not been accepted yet; you can write again once it is."));
        }
        else if (thread.RequestedById is not null)
        {
            // The private side answering is the same as accepting.
            thread.RequestedById = null;
            thread.RequestDeclined = false;
        }

        if (command.ReplyToId is { } replyTo && !await db.DirectMessages.AnyAsync(m => m.Id == replyTo && m.ThreadId == thread.Id, ct))
            return Result.Failure<DirectConversation>(Error.Validation("That message is not in this conversation."));

        db.DirectMessages.Add(new DirectMessage
        {
            Thread = thread,
            SenderId = me,
            SenderName = sender.FullName,
            Body = command.Body.Trim(),
            ReplyToId = command.ReplyToId,
            Forwarded = command.Forwarded,
        });

        thread.LastMessageAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // After the save, never before: the other side reloads on this nudge and must find
        // the message already there.
        await realtime.DirectMessageAsync(command.OtherUserId, me, sender.FullName, ct);

        return Result.Success((await dispatcher.Ask(new GetConversationQuery(command.OtherUserId), ct))!);
    }
}

// ---- A private account's answer to a message request ----

public sealed record AnswerMessageRequestCommand(Guid OtherUserId, bool Accept) : ICommand<Result<DirectConversation>>;

internal sealed class AnswerMessageRequestHandler(
    IdentityDbContext db, ICurrentUser currentUser, IDispatcher dispatcher, IRealtimeNotifier realtime)
    : ICommandHandler<AnswerMessageRequestCommand, Result<DirectConversation>>
{
    public async Task<Result<DirectConversation>> Handle(AnswerMessageRequestCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        var (low, high) = DirectThread.Pair(me, command.OtherUserId);
        // Only the person who was asked can answer: the request has to be from the other side.
        var thread = await db.DirectThreads.FirstOrDefaultAsync(
            t => t.LowUserId == low && t.HighUserId == high && t.RequestedById == command.OtherUserId, ct);
        if (thread is null) return Result.Failure<DirectConversation>(Error.NotFound("Message request"));

        if (command.Accept) { thread.RequestedById = null; thread.RequestDeclined = false; }
        else thread.RequestDeclined = true;
        await db.SaveChangesAsync(ct);

        // Accepting changes the writer's window (they may write now); declining tells them nothing.
        if (command.Accept) await realtime.DirectChangedAsync(command.OtherUserId, me, ct);
        return Result.Success((await dispatcher.Ask(new GetConversationQuery(command.OtherUserId), ct))!);
    }
}

// ---- What can be done to one message: edit, delete, react, star, pin ----

public enum DeleteScope { Me = 1, Everyone = 2 }

public sealed record EditDirectMessageCommand(Guid MessageId, string Body) : ICommand<Result>;
public sealed record DeleteDirectMessageCommand(Guid MessageId, DeleteScope Scope) : ICommand<Result>;
public sealed record ReactToDirectMessageCommand(Guid MessageId, string? Emoji) : ICommand<Result>;
public sealed record StarDirectMessageCommand(Guid MessageId, bool Starred) : ICommand<Result>;
public sealed record PinDirectMessageCommand(Guid MessageId, bool Pinned) : ICommand<Result>;

internal sealed class EditDirectMessageValidator : AbstractValidator<EditDirectMessageCommand>
{
    public EditDirectMessageValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
}

internal sealed class DirectMessageActions(IdentityDbContext db, ICurrentUser currentUser, IRealtimeNotifier realtime) :
    ICommandHandler<EditDirectMessageCommand, Result>,
    ICommandHandler<DeleteDirectMessageCommand, Result>,
    ICommandHandler<ReactToDirectMessageCommand, Result>,
    ICommandHandler<StarDirectMessageCommand, Result>,
    ICommandHandler<PinDirectMessageCommand, Result>
{
    /// <summary>How long a sent message can still be corrected, as in WhatsApp.</summary>
    public static readonly TimeSpan EditWindow = TimeSpan.FromMinutes(15);

    /// <summary>The message, if it is in a thread the current user belongs to. Anyone else gets
    /// "not found" - never "not yours", which would confirm the id exists.</summary>
    private async Task<(DirectMessage Message, DirectThread Thread, Guid Me, Guid Other)?> FindAsync(Guid messageId, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        var message = await db.DirectMessages.Include(m => m.Thread).FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message?.Thread is not { } thread || (thread.LowUserId != me && thread.HighUserId != me)) return null;
        return (message, thread, me, thread.LowUserId == me ? thread.HighUserId : thread.LowUserId);
    }

    private async Task<DirectMessageMark> MarkAsync(Guid messageId, Guid me, CancellationToken ct)
    {
        var mark = await db.DirectMessageMarks.FirstOrDefaultAsync(k => k.MessageId == messageId && k.UserId == me, ct);
        if (mark is null) { mark = new DirectMessageMark { MessageId = messageId, UserId = me }; db.DirectMessageMarks.Add(mark); }
        return mark;
    }

    private static readonly Error Missing = Error.NotFound("Message");
    private static readonly Error Closed = Error.Conflict("This person is not accepting messages.");

    /// <summary>A block either way closes the conversation to anything the other side would see.</summary>
    private Task<bool> BlockedAsync(Guid me, Guid other, CancellationToken ct) =>
        db.UserBlocks.AnyAsync(b => (b.BlockerId == me && b.BlockedId == other) || (b.BlockerId == other && b.BlockedId == me), ct);

    public async Task<Result> Handle(EditDirectMessageCommand command, CancellationToken ct)
    {
        if (await FindAsync(command.MessageId, ct) is not var (message, _, me, other)) return Result.Failure(Missing);
        if (message.SenderId != me) return Result.Failure(Error.Forbidden("Only your own messages can be edited."));
        if (message.DeletedAtUtc != null) return Result.Failure(Error.Conflict("This message was deleted."));
        if (DateTime.UtcNow - message.CreatedAtUtc > EditWindow)
            return Result.Failure(Error.Conflict("A message can be edited for 15 minutes after it is sent."));
        if (await BlockedAsync(me, other, ct)) return Result.Failure(Closed);

        message.Body = command.Body.Trim();
        message.EditedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await realtime.DirectChangedAsync(other, me, ct);
        return Result.Success();
    }

    public async Task<Result> Handle(DeleteDirectMessageCommand command, CancellationToken ct)
    {
        if (await FindAsync(command.MessageId, ct) is not var (message, thread, me, other)) return Result.Failure(Missing);

        if (command.Scope == DeleteScope.Me)
        {
            (await MarkAsync(message.Id, me, ct)).Hidden = true;
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }

        if (message.SenderId != me) return Result.Failure(Error.Forbidden("Only your own messages can be deleted for everyone."));
        // The text goes; the place in the conversation stays, as "This message was deleted".
        message.Body = "";
        message.DeletedAtUtc = DateTime.UtcNow;
        if (thread.PinnedMessageId == message.Id) thread.PinnedMessageId = null;
        await db.DirectMessageMarks.Where(k => k.MessageId == message.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(k => k.Reaction, (string?)null), ct);
        await db.SaveChangesAsync(ct);
        await realtime.DirectChangedAsync(other, me, ct);
        return Result.Success();
    }

    public async Task<Result> Handle(ReactToDirectMessageCommand command, CancellationToken ct)
    {
        if (command.Emoji is { } emoji && !IsEmoji(emoji)) return Result.Failure(Error.Validation("A reaction is one emoji."));
        if (await FindAsync(command.MessageId, ct) is not var (message, thread, me, other)) return Result.Failure(Missing);
        if (message.DeletedAtUtc != null) return Result.Failure(Error.Conflict("This message was deleted."));
        if (thread.RequestedById != null) return Result.Failure(Error.Conflict("Accept the message request first."));
        if (await BlockedAsync(me, other, ct)) return Result.Failure(Closed);

        (await MarkAsync(message.Id, me, ct)).Reaction = command.Emoji;
        await db.SaveChangesAsync(ct);
        await realtime.DirectChangedAsync(other, me, ct);
        return Result.Success();
    }

    public async Task<Result> Handle(StarDirectMessageCommand command, CancellationToken ct)
    {
        if (await FindAsync(command.MessageId, ct) is not var (message, _, me, _)) return Result.Failure(Missing);
        (await MarkAsync(message.Id, me, ct)).Starred = command.Starred;
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> Handle(PinDirectMessageCommand command, CancellationToken ct)
    {
        if (await FindAsync(command.MessageId, ct) is not var (message, thread, me, other)) return Result.Failure(Missing);
        if (message.DeletedAtUtc != null) return Result.Failure(Error.Conflict("This message was deleted."));
        if (thread.RequestedById != null) return Result.Failure(Error.Conflict("Accept the message request first."));
        if (await BlockedAsync(me, other, ct)) return Result.Failure(Closed);

        if (command.Pinned) thread.PinnedMessageId = message.Id;
        else if (thread.PinnedMessageId == message.Id) thread.PinnedMessageId = null;
        await db.SaveChangesAsync(ct);
        await realtime.DirectChangedAsync(other, me, ct);
        return Result.Success();
    }

    /// <summary>One emoji, with its joiners and modifiers (a flag, a skin tone, a family) - and
    /// nothing a person could type as text.</summary>
    internal static bool IsEmoji(string value)
    {
        // One grapheme: a flag or a family is several code points but one emoji; two hearts are two.
        if (value.Length is 0 or > 16 || new StringInfo(value).LengthInTextElements != 1) return false;
        var symbol = false;
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.OtherSymbol) { symbol = true; continue; }
            if (rune.Value is 0x200D or 0xFE0F or 0x20E3 || category is UnicodeCategory.ModifierSymbol
                or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) continue;
            return false;
        }
        return symbol;
    }
}

public sealed record BlockUserCommand(Guid OtherUserId, bool Blocked) : ICommand<Result>;

internal sealed class BlockUserHandler(IdentityDbContext db, ICurrentUser currentUser)
    : ICommandHandler<BlockUserCommand, Result>
{
    public async Task<Result> Handle(BlockUserCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        if (me == command.OtherUserId) return Result.Failure(Error.Validation("You cannot block yourself."));
        // Only real people: a block row for an id nobody has was pure clutter.
        if (command.Blocked && !await db.Users.AnyAsync(u => u.Id == command.OtherUserId, ct))
            return Result.Failure(Error.NotFound("User"));

        var existing = await db.UserBlocks
            .FirstOrDefaultAsync(b => b.BlockerId == me && b.BlockedId == command.OtherUserId, ct);

        if (command.Blocked && existing is null)
            db.UserBlocks.Add(new UserBlock { BlockerId = me, BlockedId = command.OtherUserId });
        else if (!command.Blocked && existing is not null)
            db.UserBlocks.Remove(existing);
        else
            return Result.Success();

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ReportMessageCommand(Guid AboutUserId, string Quote, string? Reason) : ICommand<Result>;

internal sealed class ReportMessageHandler(IdentityDbContext db, ICurrentUser currentUser, IAuditLog audit)
    : ICommandHandler<ReportMessageCommand, Result>
{
    public async Task<Result> Handle(ReportMessageCommand command, CancellationToken ct)
    {
        var me = currentUser.RequireId();
        var reporter = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == me, ct);
        var about = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == command.AboutUserId, ct);

        if (reporter is null || about is null) return Result.Failure(Error.NotFound("User"));

        // The quote is read from the thread, not taken from the request. Taking it from the
        // request let anyone file a report "quoting" abuse the other person never wrote.
        var (low, high) = DirectThread.Pair(me, about.Id);
        var quote = await db.DirectMessages.AsNoTracking()
            .Where(m => m.SenderId == about.Id && m.Thread!.LowUserId == low && m.Thread.HighUserId == high)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => m.Body)
            .FirstOrDefaultAsync(ct) ?? "(no message)";

        db.MessageReports.Add(new MessageReport
        {
            ReporterId = me,
            ReporterEmail = reporter.Email,
            AboutUserId = about.Id,
            AboutEmail = about.Email,
            // Copied, not referenced: the sender can delete the message and the desk still
            // needs to read what was said.
            Quote = quote[..Math.Min(quote.Length, 2000)],
            Reason = command.Reason?.Trim()
        });

        // Reporting also blocks. Nobody should have to keep receiving from somebody they have
        // just reported while the desk gets round to it.
        if (!await db.UserBlocks.AnyAsync(b => b.BlockerId == me && b.BlockedId == about.Id, ct))
            db.UserBlocks.Add(new UserBlock { BlockerId = me, BlockedId = about.Id, Reason = "Reported" });

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new AuditEntry("message.reported", about.Email, command.Reason, about.Id), ct);
        return Result.Success();
    }
}

public sealed record ReportRow(
    Guid Id, string ReporterEmail, string AboutEmail, string Quote, string? Reason,
    bool Handled, DateTime CreatedAtUtc);

public sealed record GetReportsQuery(bool IncludeHandled) : IQuery<IReadOnlyList<ReportRow>>;

internal sealed class GetReportsHandler(IdentityDbContext db) : IQueryHandler<GetReportsQuery, IReadOnlyList<ReportRow>>
{
    public async Task<IReadOnlyList<ReportRow>> Handle(GetReportsQuery query, CancellationToken ct) =>
        await db.MessageReports.AsNoTracking()
            .Where(r => query.IncludeHandled || !r.Handled)
            .OrderBy(r => r.Handled).ThenByDescending(r => r.CreatedAtUtc)
            .Take(200)
            .Select(r => new ReportRow(r.Id, r.ReporterEmail, r.AboutEmail, r.Quote, r.Reason, r.Handled, r.CreatedAtUtc))
            .ToListAsync(ct);
}

public sealed record ResolveReportCommand(Guid ReportId) : ICommand<Result>;

internal sealed class ResolveReportHandler(IdentityDbContext db, IAuditLog audit)
    : ICommandHandler<ResolveReportCommand, Result>
{
    public async Task<Result> Handle(ResolveReportCommand command, CancellationToken ct)
    {
        var report = await db.MessageReports.FirstOrDefaultAsync(r => r.Id == command.ReportId, ct);
        if (report is null) return Result.Failure(Error.NotFound("Report"));

        report.Handled = true;
        report.HandledAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry("report.resolved", report.AboutEmail, report.Reason, report.Id), ct);
        return Result.Success();
    }
}

public static class DirectMessageEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var mine = app.MapGroup("/api/messages").WithTags("Messages").RequireAuthorization();

        mine.MapGet("", async (IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetMyThreadsQuery(), ct)))
            .WithName("GetMyDirectThreads");

        mine.MapGet("/overview", async (IDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.Ask(new GetMessageOverviewQuery(), ct)))
            .WithName("GetMessageOverview");

        mine.MapGet("/{userId:guid}", async (Guid userId, DateTime? before, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var conversation = await dispatcher.Ask(new GetConversationQuery(userId, before?.ToUniversalTime()), ct);
                return conversation is null ? Results.NotFound() : Results.Ok(conversation);
            })
            .WithName("GetDirectConversationWithId");

        mine.MapPost("/{userId:guid}",
            async Task<Results<Ok<DirectConversation>, BadRequest<Error>, Conflict<Error>, NotFound<Error>>> (
                Guid userId, SendDirectMessageCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { OtherUserId = userId }, ct);
                if (result.IsSuccess) return TypedResults.Ok(result.Value);
                return result.Error.Code switch
                {
                    "not_found" => TypedResults.NotFound(result.Error),
                    "conflict" => TypedResults.Conflict(result.Error),
                    _ => TypedResults.BadRequest(result.Error)
                };
            }).WithName("SendDirectMessageWithId").RequireRateLimiting(AppPolicies.WriteRateLimit);

        mine.MapPut("/{userId:guid}/request",
            async Task<Results<Ok<DirectConversation>, NotFound<Error>>> (
                Guid userId, AnswerMessageRequestCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { OtherUserId = userId }, ct);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.NotFound(result.Error);
            }).WithName("AnswerMessageRequest").RequireRateLimiting(AppPolicies.WriteRateLimit);

        var item = mine.MapGroup("/item/{messageId:guid}").RequireRateLimiting(AppPolicies.WriteRateLimit);
        item.MapPut("", (Guid messageId, EditDirectMessageCommand body, IDispatcher d, CancellationToken ct) =>
            Answer(d.Send(body with { MessageId = messageId }, ct))).WithName("EditDirectMessage");
        item.MapDelete("", (Guid messageId, DeleteScope? scope, IDispatcher d, CancellationToken ct) =>
            Answer(d.Send(new DeleteDirectMessageCommand(messageId, scope ?? DeleteScope.Me), ct))).WithName("DeleteDirectMessage");
        item.MapPut("/reaction", (Guid messageId, ReactToDirectMessageCommand body, IDispatcher d, CancellationToken ct) =>
            Answer(d.Send(body with { MessageId = messageId }, ct))).WithName("ReactToDirectMessage");
        item.MapPut("/star", (Guid messageId, StarDirectMessageCommand body, IDispatcher d, CancellationToken ct) =>
            Answer(d.Send(body with { MessageId = messageId }, ct))).WithName("StarDirectMessage");
        item.MapPut("/pin", (Guid messageId, PinDirectMessageCommand body, IDispatcher d, CancellationToken ct) =>
            Answer(d.Send(body with { MessageId = messageId }, ct))).WithName("PinDirectMessage");

        mine.MapPut("/{userId:guid}/block",
            async Task<Results<NoContent, BadRequest<Error>>> (
                Guid userId, BlockUserCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { OtherUserId = userId }, ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.BadRequest(result.Error);
            }).WithName("BlockUserWithId").RequireRateLimiting(AppPolicies.WriteRateLimit);

        mine.MapPost("/{userId:guid}/report",
            async Task<Results<NoContent, BadRequest<Error>, NotFound<Error>>> (
                Guid userId, ReportMessageCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { AboutUserId = userId }, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.BadRequest(result.Error);
            }).WithName("ReportMessageWithId").RequireRateLimiting(AppPolicies.WriteRateLimit);

        var desk = app.MapGroup("/api/reports").WithTags("Messages")
            .RequireAuthorization(AppPolicies.SecurityDesk);

        desk.MapGet("", async (bool? includeHandled, IDispatcher dispatcher, CancellationToken ct) =>
                Results.Ok(await dispatcher.Ask(new GetReportsQuery(includeHandled ?? false), ct)))
            .WithName("GetMessageReports");

        desk.MapPost("/{id:guid}/resolve",
            async Task<Results<NoContent, NotFound<Error>>> (
                Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new ResolveReportCommand(id), ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.NotFound(result.Error);
            }).WithName("ResolveReportWithId");
    }

    private static async Task<IResult> Answer(Task<Result> pending)
    {
        var result = await pending;
        if (result.IsSuccess) return Results.NoContent();
        return result.Error.Code switch
        {
            "not_found" => Results.NotFound(result.Error),
            "forbidden" => Results.Json(result.Error, statusCode: StatusCodes.Status403Forbidden),
            "conflict" => Results.Conflict(result.Error),
            _ => Results.BadRequest(result.Error)
        };
    }
}
