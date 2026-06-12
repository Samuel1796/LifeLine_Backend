using BloodDonorFinder.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BloodDonorFinder.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<DonorProfile> DonorProfiles => Set<DonorProfile>();
    public DbSet<BloodRequest> BloodRequests => Set<BloodRequest>();
    public DbSet<DonorResponse> DonorResponses => Set<DonorResponse>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasOne(u => u.DonorProfile)
            .WithOne(p => p.User)
            .HasForeignKey<DonorProfile>(p => p.UserId);

        modelBuilder.Entity<BloodRequest>()
            .HasOne(r => r.Requester)
            .WithMany()
            .HasForeignKey(r => r.RequesterId);

        modelBuilder.Entity<DonorResponse>()
            .HasOne(r => r.BloodRequest)
            .WithMany(b => b.Responses)
            .HasForeignKey(r => r.BloodRequestId);

        modelBuilder.Entity<DonorResponse>()
            .HasOne(r => r.Donor)
            .WithMany()
            .HasForeignKey(r => r.DonorId);

        // A donor can only respond once per request
        modelBuilder.Entity<DonorResponse>()
            .HasIndex(r => new { r.BloodRequestId, r.DonorId })
            .IsUnique();

        modelBuilder.Entity<Message>()
            .HasOne(m => m.BloodRequest)
            .WithMany()
            .HasForeignKey(m => m.BloodRequestId);

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Message>()
            .HasIndex(m => new { m.BloodRequestId, m.DonorId });
    }
}
