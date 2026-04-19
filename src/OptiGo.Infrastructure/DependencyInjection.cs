using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptiGo.Application.Interfaces;
using OptiGo.Infrastructure.ExternalServices.Groq;
using OptiGo.Infrastructure.ExternalServices.Mapbox;
using OptiGo.Infrastructure.Persistence;
using OptiGo.Infrastructure.Persistence.Repositories;
using OptiGo.Infrastructure.Routing;

namespace OptiGo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {

        services.AddDbContext<OptiGoDbContext>(options =>
        {
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres"),
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(OptiGoDbContext).Assembly.FullName);
                });
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<OptiGoDbContext>());

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IVenueRepository, VenueRepository>();

        services.Configure<OptiGo.Infrastructure.ExternalServices.Google.GoogleOptions>(
            configuration.GetSection("Google"));

        services.AddHttpClient<IPlacesProvider, OptiGo.Infrastructure.ExternalServices.Google.GooglePlacesProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.Configure<OptiGo.Infrastructure.ExternalServices.Mapbox.MapboxOptions>(
            configuration.GetSection("Mapbox"));

        services.AddHttpClient<ITravelTimeService, OptiGo.Infrastructure.ExternalServices.Mapbox.MapboxTravelTimeService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<ITrafficSnapshotProvider, DefaultTrafficSnapshotProvider>();
        services.AddSingleton<IRouteCostProvider, CachedRouteCostProvider>();
        services.AddScoped<IVenuePrefilter, RouteAwareVenuePrefilter>();
        services.AddScoped<IStopCandidateGenerator, StopCandidateGenerator>();
        services.AddScoped<IDriverRouteOptimizer, SharedDestinationRouteOptimizer>();
        services.AddScoped<IVenueEvaluator, DefaultVenueEvaluator>();
        services.AddScoped<IOutingRoutePlanner, HybridOutingRoutePlanner>();

        services.Configure<GroqOptions>(configuration.GetSection("Groq"));
        services.AddHttpClient<IAIService, GroqAIService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }
}
