using Coravel.Persistence.Configuration;
using Coravel.Persistence.HostedServices;
using Coravel.Persistence.Interfaces;
using Coravel.Persistence.Mutex;
using Coravel.Persistence.Services;
using Coravel.Scheduling.Schedule.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coravel.Persistence;

/// <summary>
/// Extension methods for registering Coravel persistence services.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Adds Coravel persistence services using the specified DbContext.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that implements IPersistentCoravelDbContext.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddPersistentCoravel&lt;ApplicationDbContext&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddPersistentCoravel<TContext>(this IServiceCollection services)
        where TContext : DbContext, IPersistentCoravelDbContext
    {
        return services.AddPersistentCoravel<TContext>(_ => { });
    }

    /// <summary>
    /// Adds Coravel persistence services using the specified DbContext with configuration.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type that implements IPersistentCoravelDbContext.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Action to configure persistence options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddPersistentCoravel&lt;ApplicationDbContext&gt;(options =>
    /// {
    ///     options.JobPollingInterval = TimeSpan.FromSeconds(10);
    ///     options.MaxConcurrentJobs = 3;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddPersistentCoravel<TContext>(
        this IServiceCollection services,
        Action<PersistentCoravelOptions> configure)
        where TContext : DbContext, IPersistentCoravelDbContext
    {
        // Configure options
        services.Configure(configure);

        // Register the context as IPersistentCoravelDbContext
        services.AddScoped<IPersistentCoravelDbContext>(sp => sp.GetRequiredService<TContext>());

        // Register core services
        services.AddSingleton<DatabaseMutex>();
        services.AddSingleton<IMutex>(sp => sp.GetRequiredService<DatabaseMutex>());
        services.AddSingleton<JobClaimService>();
        services.AddSingleton<ScheduleSyncService>();

        // Register persistent queue
        services.AddSingleton<PersistentQueue>();
        services.AddSingleton<IPersistentQueue>(sp => sp.GetRequiredService<PersistentQueue>());
        services.AddSingleton<IPersistentQueueConfiguration>(sp => sp.GetRequiredService<PersistentQueue>());

        // Register persistent scheduler
        services.AddSingleton<PersistentScheduler>();
        services.AddSingleton<IPersistentScheduler>(sp => sp.GetRequiredService<PersistentScheduler>());
        services.AddSingleton<IPersistentSchedulerConfiguration>(sp => sp.GetRequiredService<PersistentScheduler>());

        // Register hosted services
        services.AddHostedService<PersistentJobHost>();
        services.AddHostedService<PersistentSchedulerHost>();
        services.AddHostedService<StaleJobRecoveryHost>();

        return services;
    }

    /// <summary>
    /// Configures the persistent queue with an action.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <returns>The queue configuration interface for chaining.</returns>
    public static IPersistentQueueConfiguration ConfigurePersistentQueue(this IServiceProvider provider)
    {
        return provider.GetRequiredService<IPersistentQueueConfiguration>();
    }

    /// <summary>
    /// Configures the persistent scheduler with schedules.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <param name="configureSchedules">Action to configure schedules.</param>
    /// <returns>The scheduler configuration interface for chaining.</returns>
    /// <example>
    /// <code>
    /// app.Services.UsePersistentScheduler(scheduler =>
    /// {
    ///     scheduler.Schedule&lt;MyJob&gt;().EveryMinute();
    ///     scheduler.Schedule&lt;AnotherJob&gt;().DailyAt(3, 0);
    /// });
    /// </code>
    /// </example>
    public static IPersistentSchedulerConfiguration UsePersistentScheduler(
        this IServiceProvider provider,
        Action<IPersistentScheduler> configureSchedules)
    {
        var scheduler = provider.GetRequiredService<IPersistentScheduler>();
        configureSchedules(scheduler);
        return provider.GetRequiredService<IPersistentSchedulerConfiguration>();
    }
}
