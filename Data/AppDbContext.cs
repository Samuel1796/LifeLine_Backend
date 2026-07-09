using Microsoft.EntityFrameworkCore;
using Nook.Api.Models;

namespace Nook.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Office> Offices => Set<Office>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Nook lives in its own schema so it can share a database (e.g. on
        // Render) without touching tables in "public".
        modelBuilder.HasDefaultSchema("nook");

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Workspace>()
            .HasOne(w => w.Office)
            .WithMany(o => o.Workspaces)
            .HasForeignKey(w => w.OfficeId);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Workspace)
            .WithMany()
            .HasForeignKey(b => b.WorkspaceId);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Speeds up overlap checks and per-day listings for a workspace.
        modelBuilder.Entity<Booking>()
            .HasIndex(b => new { b.WorkspaceId, b.StartsAt });
    }
}
