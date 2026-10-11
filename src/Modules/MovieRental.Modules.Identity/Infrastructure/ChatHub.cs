using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Modules.Identity.Infrastructure;

/// <summary>
/// Real-time channel for direct messages and the Help desk.
///
/// The hub does not carry messages. Sending still goes through the HTTP endpoints, where the
/// validator, the block rules and the database transaction live; the hub only tells the other
/// side "something arrived" so its page reloads the thread at once instead of on the next poll.
/// That split means a dropped socket costs a few seconds of latency and never a message.
///
/// Users are addressed by their id (the NameIdentifier claim, which SignalR's default user id
/// provider reads), so every open tab of the same person hears the same event.
/// </summary>
[Authorize]
public sealed class ChatHub(IdentityDbContext db, Presence presence, ILogger<ChatHub> logger) : Hub
{
    public const string Path = "/hubs/chat";

    /// <summary>Everyone who answers Help chats — Security and Admin.</summary>
    public const string DeskGroup = "help-desk";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole(AppRoles.Security) == true || Context.User?.IsInRole(AppRoles.Admin) == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, DeskGroup);

        if (MyId() is { } me) presence.Connected(me);
        await base.OnConnectedAsync();
    }

    /// <summary>"I am typing to this person." Dropped silently when either side has blocked
    /// the other — a typing notice is still contact, and a block means no contact.</summary>
    public async Task Typing(Guid toUserId)
    {
        var me = MyId();
        if (me is null || me == toUserId) return;

        var blocked = await db.UserBlocks.AsNoTracking().AnyAsync(
            b => (b.BlockerId == toUserId && b.BlockedId == me) || (b.BlockerId == me && b.BlockedId == toUserId));
        if (blocked) return;

        await Clients.User(toUserId.ToString()).SendAsync("typing", new { fromUserId = me, fromName = MyName() });
    }

    /// <summary>Typing in a Help conversation. A customer's notice goes to the desk; an agent's
    /// goes to the customer who owns the conversation, and only if it is really theirs.</summary>
    public async Task HelpTyping(Guid? conversationId)
    {
        var me = MyId();
        if (me is null) return;

        var isDesk = Context.User?.IsInRole(AppRoles.Security) == true || Context.User?.IsInRole(AppRoles.Admin) == true;

        if (isDesk && conversationId is { } id)
        {
            var owner = await db.SupportConversations.AsNoTracking()
                .Where(c => c.Id == id).Select(c => (Guid?)c.UserId).FirstOrDefaultAsync();
            if (owner is { } customer)
                await Clients.User(customer.ToString()).SendAsync("helpTyping",
                    new { conversationId = id, fromDesk = true, name = MyName() });
            return;
        }

        var mine = await db.SupportConversations.AsNoTracking()
            .Where(c => c.UserId == me).Select(c => (Guid?)c.Id).FirstOrDefaultAsync();
        if (mine is { } conversation)
            await Clients.Group(DeskGroup).SendAsync("helpTyping",
                new { conversationId = conversation, fromDesk = false, name = MyName() });
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null) logger.LogDebug(exception, "Chat connection {Id} dropped.", Context.ConnectionId);
        if (MyId() is { } me) presence.Disconnected(me);
        return base.OnDisconnectedAsync(exception);
    }

    private Guid? MyId() =>
        Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private string MyName() =>
        Context.User?.FindFirstValue(ClaimTypes.Name)
        ?? Context.User?.FindFirstValue(ClaimTypes.Email)
        ?? "";
}

/// <summary>What the chat handlers call after saving. Kept behind an interface so the
/// handlers stay testable without a hub, and so a failure to notify never fails the send.</summary>
public interface IRealtimeNotifier
{
    Task DirectMessageAsync(Guid toUserId, Guid fromUserId, string fromName, CancellationToken ct = default);
    Task HelpForCustomerAsync(Guid customerUserId, Guid conversationId, CancellationToken ct = default);
    Task HelpForDeskAsync(Guid conversationId, CancellationToken ct = default);
}

internal sealed class SignalRNotifier(IHubContext<ChatHub> hub, ILogger<SignalRNotifier> logger) : IRealtimeNotifier
{
    public Task DirectMessageAsync(Guid toUserId, Guid fromUserId, string fromName, CancellationToken ct = default) =>
        Safely(() => hub.Clients.User(toUserId.ToString()).SendAsync("dm", new { fromUserId, fromName }, ct));

    public Task HelpForCustomerAsync(Guid customerUserId, Guid conversationId, CancellationToken ct = default) =>
        Safely(() => hub.Clients.User(customerUserId.ToString()).SendAsync("help", new { conversationId }, ct));

    public Task HelpForDeskAsync(Guid conversationId, CancellationToken ct = default) =>
        Safely(() => hub.Clients.Group(ChatHub.DeskGroup).SendAsync("helpDesk", new { conversationId }, ct));

    /// <summary>The message is already saved by the time this runs. If the push fails the
    /// other side's poll picks it up, so the error is logged and swallowed.</summary>
    private async Task Safely(Func<Task> push)
    {
        try { await push(); }
        catch (Exception ex) { logger.LogWarning(ex, "Real-time notification could not be delivered."); }
    }
}
