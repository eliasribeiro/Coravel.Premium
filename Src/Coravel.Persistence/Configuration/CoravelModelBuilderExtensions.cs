using Coravel.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Coravel.Persistence.Configuration;

/// <summary>
/// Extension methods for configuring Coravel persistence entities in EF Core.
/// </summary>
public static class CoravelModelBuilderExtensions
{
    /// <summary>
    /// Configures the Coravel persistence entities in the model builder.
    /// Call this method in your DbContext's OnModelCreating method.
    /// </summary>
    /// <param name="builder">The model builder.</param>
    /// <returns>The model builder for chaining.</returns>
    /// <example>
    /// <code>
    /// protected override void OnModelCreating(ModelBuilder builder)
    /// {
    ///     base.OnModelCreating(builder);
    ///     builder.ConfigureCoravelPersistence();
    /// }
    /// </code>
    /// </example>
    public static ModelBuilder ConfigureCoravelPersistence(this ModelBuilder builder)
    {
        ConfigureJobHistory(builder);
        ConfigureScheduledJob(builder);
        ConfigureScheduledJobHistory(builder);

        return builder;
    }

    private static void ConfigureJobHistory(ModelBuilder builder)
    {
        builder.Entity<CoravelJobHistory>(entity =>
        {
            entity.ToTable("Coravel_JobHistory");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.JobName)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.JobType)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.Status)
                .HasDefaultValue(JobStatus.Pending);

            entity.Property(e => e.Attempts)
                .HasDefaultValue(0);

            entity.Property(e => e.MaxAttempts)
                .HasDefaultValue(3);

            entity.Property(e => e.Priority)
                .HasDefaultValue(0);

            entity.Property(e => e.Version)
                .HasDefaultValue(0)
                .IsConcurrencyToken();

            entity.Property(e => e.InstanceId)
                .HasMaxLength(100);

            entity.Property(e => e.IdempotencyKey)
                .HasMaxLength(255);

            // Ignore the TimeSpan properties (use Ticks columns)
            entity.Ignore(e => e.Timeout);

            // Indexes for efficient querying
            entity.HasIndex(e => new { e.Status, e.NextRetryAtUtc, e.Priority })
                .HasDatabaseName("IX_JobHistory_Status_NextRetry_Priority");

            entity.HasIndex(e => e.IdempotencyKey)
                .HasDatabaseName("IX_JobHistory_IdempotencyKey");

            entity.HasIndex(e => new { e.Status, e.LastHeartbeatUtc })
                .HasDatabaseName("IX_JobHistory_Status_Heartbeat");
        });
    }

    private static void ConfigureScheduledJob(ModelBuilder builder)
    {
        builder.Entity<CoravelScheduledJob>(entity =>
        {
            entity.ToTable("Coravel_ScheduledJobs");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.JobName)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.JobType)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.CronExpression)
                .HasMaxLength(100);

            entity.Property(e => e.IsActive)
                .HasDefaultValue(true);

            entity.Property(e => e.MaxRetries)
                .HasDefaultValue(3);

            entity.Property(e => e.RetryDelayTicks)
                .HasDefaultValue(TimeSpan.FromSeconds(30).Ticks);

            entity.Property(e => e.PreventOverlapping)
                .HasDefaultValue(true);

            entity.Property(e => e.TimeZoneId)
                .HasMaxLength(100);

            entity.Property(e => e.InstanceLockId)
                .HasMaxLength(100);

            entity.Property(e => e.Version)
                .HasDefaultValue(0)
                .IsConcurrencyToken();

            // Ignore the TimeSpan properties (use Ticks columns)
            entity.Ignore(e => e.Timeout);
            entity.Ignore(e => e.RetryDelay);

            // Unique constraint on JobName
            entity.HasIndex(e => e.JobName)
                .IsUnique()
                .HasDatabaseName("IX_ScheduledJobs_JobName");

            // Index for finding due jobs
            entity.HasIndex(e => new { e.IsActive, e.NextExecutionUtc })
                .HasDatabaseName("IX_ScheduledJobs_Active_NextExecution");
        });
    }

    private static void ConfigureScheduledJobHistory(ModelBuilder builder)
    {
        builder.Entity<CoravelScheduledJobHistory>(entity =>
        {
            entity.ToTable("Coravel_ScheduledJobHistory");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.Status)
                .HasDefaultValue(JobStatus.Pending);

            entity.Property(e => e.Attempts)
                .HasDefaultValue(0);

            entity.Property(e => e.InstanceId)
                .HasMaxLength(100);

            entity.Property(e => e.Version)
                .HasDefaultValue(0)
                .IsConcurrencyToken();

            // Ignore the TimeSpan property (use Ticks column)
            entity.Ignore(e => e.Duration);

            // Foreign key relationship
            entity.HasOne(e => e.ScheduledJob)
                .WithMany()
                .HasForeignKey(e => e.ScheduledJobId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.ScheduledJobId)
                .HasDatabaseName("IX_ScheduledJobHistory_JobId");

            entity.HasIndex(e => new { e.Status, e.LastHeartbeatUtc })
                .HasDatabaseName("IX_ScheduledJobHistory_Status_Heartbeat");
        });
    }
}
