using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MovieRental.SharedKernel.Modules;

/// <summary>
/// A module owns its entities, its DbContext, its schema and its endpoints. The host
/// only knows this interface, so adding a module never means editing Program.cs.
/// </summary>
public interface IModule
{
    string Name { get; }
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
