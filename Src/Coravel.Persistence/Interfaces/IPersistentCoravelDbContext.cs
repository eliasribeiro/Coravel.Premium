using Coravel.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Coravel.Persistence.Interfaces;

/// <summary>
/// Interface that must be implemented by the application's DbContext
/// to enable Coravel persistence features.
/// </summary>
/// <example>
/// <code>
/// public class ApplicationDbContext : DbContext, IPersistentCoravelDbContext
/// {
///     public DbSet&lt;CoravelJobHistory&gt; Coravel_JobHistory { get; set; }
///     public DbSet&lt;CoravelScheduledJob&gt; Coravel_ScheduledJobs { get; set; }
///     public DbSet&lt;CoravelScheduledJobHistory&gt; Coravel_ScheduledJobHistory { get; set; }
/// }
/// </code>
/// </example>
public interface IPersistentCoravelDbContext
{
    /// <summary>
    /// DbSet for queued job history.
    /// </summary>
    DbSet<CoravelJobHistory> Coravel_JobHistory { get; set; }

    /// <summary>
    /// DbSet for scheduled job definitions.
    /// </summary>
    DbSet<CoravelScheduledJob> Coravel_ScheduledJobs { get; set; }

    /// <summary>
    /// DbSet for scheduled job execution history.
    /// </summary>
    DbSet<CoravelScheduledJobHistory> Coravel_ScheduledJobHistory { get; set; }

    /// <summary>
    /// Saves all changes made in this context to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Provides access to database-related information and operations.
    /// </summary>
    DatabaseFacade Database { get; }
}
