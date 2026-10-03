using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using QASentinel.Domain.Entities;
using QASentinel.Domain.Testing;

namespace QASentinel.Infrastructure.Persistence;

public sealed class QASentinelDbContext(DbContextOptions<QASentinelDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ApplicationUnderTest> Applications => Set<ApplicationUnderTest>();

    public DbSet<ApplicationSetting> Settings => Set<ApplicationSetting>();

    public DbSet<TestCase> TestCases => Set<TestCase>();

    public DbSet<TestStep> TestSteps => Set<TestStep>();

    public DbSet<TestExecution> TestExecutions => Set<TestExecution>();

    public DbSet<StepExecution> StepExecutions => Set<StepExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("Projects");
            entity.HasKey(project => project.Id);
            entity.Property(project => project.Name).HasMaxLength(200).IsRequired();
            entity.Property(project => project.Description).HasMaxLength(2000);
            entity.HasMany(project => project.Applications)
                .WithOne(application => application.Project)
                .HasForeignKey(application => application.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(project => project.TestCases)
                .WithOne(testCase => testCase.Project)
                .HasForeignKey(testCase => testCase.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationUnderTest>(entity =>
        {
            entity.ToTable("Applications");
            entity.HasKey(application => application.Id);
            entity.Property(application => application.Name).HasMaxLength(200).IsRequired();
            entity.Property(application => application.ExecutablePath).HasMaxLength(2000).IsRequired();
            entity.Property(application => application.Arguments).HasMaxLength(2000);
            entity.Property(application => application.WorkingDirectory).HasMaxLength(2000);
            entity.Property(application => application.PreferredBackend).HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<ApplicationSetting>(entity =>
        {
            entity.ToTable("Settings");
            entity.HasKey(setting => setting.Key);
            entity.Property(setting => setting.Key).HasMaxLength(200);
            entity.Property(setting => setting.Value).HasMaxLength(4000).IsRequired();
        });

        var targetConverter = new ValueConverter<TestTarget, string>(
            target => TestTargetJson.Serialize(target),
            json => TestTargetJson.Deserialize(json));
        var targetComparer = new ValueComparer<TestTarget>(
            (left, right) => TestTargetJson.Serialize(left!) == TestTargetJson.Serialize(right!),
            target => TestTargetJson.Serialize(target).GetHashCode(StringComparison.Ordinal),
            target => TestTargetJson.Deserialize(TestTargetJson.Serialize(target)));

        modelBuilder.Entity<TestCase>(entity =>
        {
            entity.ToTable("TestCases");
            entity.HasKey(testCase => testCase.Id);
            entity.Property(testCase => testCase.Name).HasMaxLength(200).IsRequired();
            entity.Property(testCase => testCase.Description).HasMaxLength(2000);
            entity.Property(testCase => testCase.Preconditions).HasMaxLength(4000);
            entity.Property(testCase => testCase.Priority).HasConversion<string>().HasMaxLength(20);
            entity.Property(testCase => testCase.Tags).HasMaxLength(1000);
            entity.HasMany(testCase => testCase.Steps)
                .WithOne(step => step.TestCase)
                .HasForeignKey(step => step.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(testCase => testCase.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasMany<TestExecution>()
                .WithOne(execution => execution.TestCase)
                .HasForeignKey(execution => execution.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TestStep>(entity =>
        {
            entity.ToTable("TestSteps");
            entity.HasKey(step => step.Id);
            entity.HasIndex(step => new { step.TestCaseId, step.Sequence }).IsUnique();
            entity.Property(step => step.Action).HasConversion<string>().HasMaxLength(40);
            entity.Property(step => step.Target)
                .HasConversion(targetConverter)
                .Metadata.SetValueComparer(targetComparer);
            entity.Property(step => step.Input).HasMaxLength(4000);
        });

        modelBuilder.Entity<TestExecution>(entity =>
        {
            entity.ToTable("TestExecutions");
            entity.HasKey(execution => execution.Id);
            entity.HasIndex(execution => execution.StartedAt);
            entity.Property(execution => execution.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(execution => execution.MachineName).HasMaxLength(200).IsRequired();
            entity.Property(execution => execution.ApplicationVersion).HasMaxLength(100);
            entity.Property(execution => execution.ErrorMessage).HasMaxLength(4000);
            entity.HasMany(execution => execution.Steps)
                .WithOne(step => step.Execution)
                .HasForeignKey(step => step.ExecutionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(execution => execution.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<StepExecution>(entity =>
        {
            entity.ToTable("StepExecutions");
            entity.HasKey(step => step.Id);
            entity.HasIndex(step => new { step.ExecutionId, step.Sequence }).IsUnique();
            entity.Property(step => step.Action).HasConversion<string>().HasMaxLength(40);
            entity.Property(step => step.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(step => step.ErrorCode).HasMaxLength(100);
            entity.Property(step => step.Message).HasMaxLength(4000);
        });
    }
}
