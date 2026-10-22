using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieRental.Modules.Identity.Features;
using MovieRental.Modules.Identity.Infrastructure;
using MovieRental.Modules.Identity.Persistence;
using MovieRental.SharedKernel.Contracts;
using MovieRental.SharedKernel.Modules;

namespace MovieRental.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Default"),
                sql => sql.MigrationsHistoryTable("__EFMigrations", IdentityDbContext.SchemaName)));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IVerificationService, VerificationService>();

        RegisterTransports(services, configuration);
    }

    /// <summary>
    /// Which transport is live is a configuration decision, not a code one. An unrecognised
    /// provider name throws at startup rather than falling back to the console, because a
    /// silent fallback in production means verification codes nobody ever receives.
    /// </summary>
    private static void RegisterTransports(IServiceCollection services, IConfiguration configuration)
    {
        var emailProvider = configuration[$"{EmailOptions.SectionName}:Provider"] ?? "Console";
        switch (emailProvider.ToLowerInvariant())
        {
            case "smtp": services.AddSingleton<IEmailSender, SmtpEmailSender>(); break;
            case "console": services.AddSingleton<IEmailSender, ConsoleEmailSender>(); break;
            default: throw new InvalidOperationException(
                $"Notifications:Email:Provider is '{emailProvider}'. Use 'Smtp' or 'Console'.");
        }

        var smsProvider = configuration[$"{SmsOptions.SectionName}:Provider"] ?? "Console";
        switch (smsProvider.ToLowerInvariant())
        {
            case "twilio":
                services.AddHttpClient(nameof(TwilioSmsSender));
                services.AddSingleton<ISmsSender, TwilioSmsSender>();
                break;
            case "console": services.AddSingleton<ISmsSender, ConsoleSmsSender>(); break;
            default: throw new InvalidOperationException(
                $"Notifications:Sms:Provider is '{smsProvider}'. Use 'Twilio' or 'Console'.");
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RegisterEndpoint.Map(endpoints);
        LoginEndpoint.Map(endpoints);
        RefreshTokenEndpoints.Map(endpoints);
        VerificationEndpoints.Map(endpoints);
        CurrentUserProfileEndpoint.Map(endpoints);
    }
}
