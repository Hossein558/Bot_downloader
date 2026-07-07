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
            e.HasIndex(x => x.TelegramId).IsUnique();
            e.Property(x => x.TelegramId).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200);
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

        // LoginOtp
        modelBuilder.Entity<LoginOtp>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TelegramId);
            e.HasIndex(x => x.Code);
        });
    }
}
