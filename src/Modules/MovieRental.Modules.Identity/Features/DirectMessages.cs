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

public sealed record DirectLine(Guid Id, bool Mine, string SenderName, string Body, DateTime CreatedAtUtc);

/// <param name="HasOlder">More messages exist before the first one here; ask with ?before=.</param>
public sealed record DirectConversation(
    Guid OtherUserId, string OtherName, string? OtherAvatarUrl, string? OtherCity,
    bool BlockedByMe, bool Unreachable, IReadOnlyList<DirectLine> Messages, bool HasOlder = false);

public sealed record DirectThreadRow(
    Guid OtherUserId, string OtherName, string? OtherAvatarUrl,
    string Preview, DateTime LastMessageAtUtc, int Unread);

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
            .Select(t => new
            {
                t.LowUserId, t.HighUserId, t.LastMessageAtUtc,
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
                t.Unread);
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
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(pageSize + 1)
            .ToListAsync(ct);
        var hasOlder = page.Count > pageSize;
        if (hasOlder) page.RemoveAt(page.Count - 1);

        // Name and city only for someone on the globe or already in a conversation with me;
        // any user id used to return both.
        if (thread is null && !other.ShareOnGlobe) return null;

        // Marking messages read is a set-based update: the window polls, and a tracked write on
        // every poll is how a chat starts throwing concurrency errors.
        if (thread is not null)
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

        return new DirectConversation(
            other.Id, other.FullName, other.AvatarUrl, other.City,
            blockedByMe,
            blockedByMe || blockedByThem || !other.ShareOnGlobe || other.IsSuspended,
            [.. page
                .OrderBy(m => m.CreatedAtUtc)
                .Select(m => new DirectLine(m.Id, m.SenderId == me, m.SenderName, m.Body, m.CreatedAtUtc))],
            hasOlder);
    }
}

public sealed record SendDirectMessageCommand(Guid OtherUserId, string Body) : ICommand<Result<DirectConversation>>;

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
        // not a rule; a rule is something the server enforces.
        if (!other.ShareOnGlobe || other.IsSuspended)
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
            thread = new DirectThread { LowUserId = low, HighUserId = high };
            db.DirectThreads.Add(thread);
        }

        db.DirectMessages.Add(new DirectMessage
        {
            Thread = thread,
            SenderId = me,
            SenderName = sender.FullName,
            Body = command.Body.Trim()
        });

        thread.LastMessageAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // After the save, never before: the other side reloads on this nudge and must find
        // the message already there.
        await realtime.DirectMessageAsync(command.OtherUserId, me, sender.FullName, ct);

        return Result.Success((await dispatcher.Ask(new GetConversationQuery(command.OtherUserId), ct))!);
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
}
