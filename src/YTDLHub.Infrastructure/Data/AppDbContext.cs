using Microsoft.EntityFrameworkCore;
using YTDLHub.Core.Models;

namespace YTDLHub.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users { get; set; } = null!;
    public DbSet<UserFolder> Folders { get; set; } = null!;
    public DbSet<LoginOtp> LoginOtps { get; set; } = null!;
    public DbSet<DownloadJob> DownloadJobs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // AppUser
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);

            // Username is now the primary login key — must be unique
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(100).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();

            // TelegramId is now optional
            e.HasIndex(x => x.TelegramId).IsUnique().HasFilter("\"TelegramId\" IS NOT NULL");

            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.PhoneNumber).HasMaxLength(30);
            e.Property(x => x.TelegramUsername).HasMaxLength(100);

            // Computed DisplayName is ignored in DB
            e.Ignore(x => x.DisplayName);
        });

        // UserFolder
        modelBuilder.Entity<UserFolder>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.User)
             .WithMany(x => x.Folders)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        // LoginOtp (kept for potential bot use)
        modelBuilder.Entity<LoginOtp>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TelegramId);
            e.HasIndex(x => x.Code);
        });
    }
}
