using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PublicData.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationMessage> Messages => Set<ConversationMessage>();

    public DbSet<ToolCallAudit> ToolCalls => Set<ToolCallAudit>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Conversation>(e =>
        {
            e.ToTable("conversations");
            e.Property(c => c.Title).HasMaxLength(200);
            e.HasIndex(c => c.UpdatedAt);
            e.HasMany(c => c.Messages).WithOne(m => m.Conversation).HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<ConversationMessage>(e =>
        {
            e.ToTable("messages");
            e.Property(m => m.Role).HasMaxLength(16);
            e.Property(m => m.Model).HasMaxLength(100);
            e.HasIndex(m => new { m.ConversationId, m.CreatedAt });
            // Keep the audit trail when a conversation is deleted: only the link is cleared.
            e.HasMany(m => m.ToolCalls).WithOne(t => t.Message).HasForeignKey(t => t.MessageId).OnDelete(DeleteBehavior.SetNull);
        });

        model.Entity<ToolCallAudit>(e =>
        {
            e.ToTable("tool_calls");
            e.Property(t => t.Origin).HasMaxLength(16);
            e.Property(t => t.CallId).HasMaxLength(100);
            e.Property(t => t.ToolName).HasMaxLength(100);
            e.Property(t => t.ArgumentsJson).HasColumnType("jsonb");
            e.Property(t => t.ResultJson).HasColumnType("jsonb");
            e.Property(t => t.McpServer).HasMaxLength(100);
            e.Property(t => t.McpTransport).HasMaxLength(16);
            e.Property(t => t.Model).HasMaxLength(100);
            e.HasIndex(t => t.CreatedAt);
            e.HasIndex(t => new { t.ToolName, t.CreatedAt });
        });
    }
}

/// <summary>Used by 'dotnet ef migrations' only; the application reads ConnectionStrings:Default.</summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=dados_publicos;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options);
}
