using System.Security.Claims;
using MovieRental.SharedKernel.Security;

namespace MovieRental.Host.Infrastructure;

/// <summary>Reads the signed JWT claims off the request. Handlers depend on this
/// abstraction rather than HttpContext, which keeps them testable and framework-free.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? Id =>
        Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;

    public Guid RequireId() => Id ?? throw new UnauthorizedAccessException("This action needs a signed-in user.");
}
