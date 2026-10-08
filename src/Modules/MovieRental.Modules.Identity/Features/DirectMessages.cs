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

public sealed record DirectConversation(
    Guid OtherUserId, string OtherName, string? OtherAvatarUrl, string? OtherCity,
    bool BlockedByMe, bool Unreachable, IReadOnlyList<DirectLine> Messages);

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

        var threads = await db.DirectThreads.AsNoTracking()
            .Include(t => t.Messages)
            .Where(t => t.LowUserId == me || t.HighUserId == me)
            .OrderByDescending(t => t.LastMessageAtUtc)
            .Take(100)
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
            var last = t.Messages.OrderByDescending(m => m.CreatedAtUtc).FirstOrDefault();

            return new DirectThreadRow(
                otherId, other?.FullName ?? "—", other?.AvatarUrl,
                last?.Body is { } body ? body[..Math.Min(body.Length, 90)] : "",
                t.LastMessageAtUtc,
                t.Messages.Count(m => m.SenderId != me && m.SeenAtUtc is null));
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
            .Include(t => t.Messages)
            .Where(t => t.LowUserId == me || t.HighUserId == me)
            .OrderByDescending(t => t.LastMessageAtUtc)
            .Take(200)
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
            var mine = thread.Messages.Where(m => m.SenderId == me).OrderByDescending(m => m.CreatedAtUtc).ToList();
            var theirs = thread.Messages.Where(m => m.SenderId != me).OrderByDescending(m => m.CreatedAtUtc).ToList();
            var last = thread.Messages.OrderByDescending(m => m.CreatedAtUtc).FirstOrDefault();
            static string Cut(string body) => body[..Math.Min(body.Length, 90)];

            if (mine.Count > 0)
                sent.Add(Person(other, Cut(mine[0].Body), last?.SenderId == me, mine.Count, mine[0].CreatedAtUtc));
            if (theirs.Count > 0)
                received.Add(Person(other, Cut(theirs[0].Body), last?.SenderId == me, theirs.Count, theirs[0].CreatedAtUtc));
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

public sealed record GetConversationQuery(Guid OtherUserId) : IQuery<DirectConversation?>;

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
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.LowUserId == low && t.HighUserId == high, ct);

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
            [.. (thread?.Messages ?? [])
                .OrderBy(m => m.CreatedAtUtc)
                .Select(m => new DirectLine(m.Id, m.SenderId == me, m.SenderName, m.Body, m.CreatedAtUtc))]);
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
        if (string.IsNullOrWhiteSpace(command.Quote))
            return Result.Failure(Error.Validation("Nothing to report."));

        db.MessageReports.Add(new MessageReport
        {
            ReporterId = me,
            ReporterEmail = reporter.Email,
            AboutUserId = about.Id,
            AboutEmail = about.Email,
            // Copied, not referenced: the sender can delete the message and the desk still
            // needs to read what was said.
            Quote = command.Quote.Trim()[..Math.Min(command.Quote.Trim().Length, 2000)],
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

        mine.MapGet("/{userId:guid}", async (Guid userId, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var conversation = await dispatcher.Ask(new GetConversationQuery(userId), ct);
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
            }).WithName("SendDirectMessageWithId");

        mine.MapPut("/{userId:guid}/block",
            async Task<Results<NoContent, BadRequest<Error>>> (
                Guid userId, BlockUserCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { OtherUserId = userId }, ct);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.BadRequest(result.Error);
            }).WithName("BlockUserWithId");

        mine.MapPost("/{userId:guid}/report",
            async Task<Results<NoContent, BadRequest<Error>, NotFound<Error>>> (
                Guid userId, ReportMessageCommand body, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var result = await dispatcher.Send(body with { AboutUserId = userId }, ct);
                if (result.IsSuccess) return TypedResults.NoContent();
                return result.Error.Code == "not_found"
                    ? TypedResults.NotFound(result.Error)
                    : TypedResults.BadRequest(result.Error);
            }).WithName("ReportMessageWithId");

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
