using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieRental.SharedKernel.Cqrs;

namespace MovieRental.SharedKernel.Modules;

public static class ModuleRegistrationExtensions
{
    public static IServiceCollection AddModules(
        this IServiceCollection services, IConfiguration configuration, params IModule[] modules)
    {
        services.AddSingleton<IReadOnlyList<IModule>>(modules);
        foreach (var module in modules)
        {
            module.RegisterServices(services, configuration);
            services.AddHandlersFrom(module.GetType().Assembly);
            services.AddValidatorsFromAssembly(module.GetType().Assembly, includeInternalTypes: true);
        }
        return services;
    }

    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder endpoints)
    {
        foreach (var module in endpoints.ServiceProvider.GetRequiredService<IReadOnlyList<IModule>>())
            module.MapEndpoints(endpoints);
        return endpoints;
    }

    /// <summary>Scans a module assembly and registers every command/query handler it finds.
    /// Convention over one registration line per slice.</summary>
    public static IServiceCollection AddHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        var openHandlers = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>) };

        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType))
            {
                if (!openHandlers.Contains(contract.GetGenericTypeDefinition())) continue;
                services.AddScoped(contract, type);
            }
        }
        return services;
    }
}
