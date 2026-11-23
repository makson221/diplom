using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace WorksArchive.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<StudyGroup> Groups => Set<StudyGroup>();
    public DbSet<Work> Works => Set<Work>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    /// <summary>Ключі шифрування cookie — зберігаються в БД, щоб вхід не злітав після перезапуску сервера.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.Login).IsUnique();
            e.Property(x => x.Login).HasMaxLength(64).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.SecurityStamp).HasMaxLength(64);
        });

        b.Entity<StudyGroup>(e =>
        {
            e.ToTable("groups");
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
        });

        b.Entity<Work>(e =>
        {
            e.ToTable("works");
            e.Property(x => x.StudentName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.StoragePath).HasMaxLength(300).IsRequired();
            e.Property(x => x.StorageProvider).HasMaxLength(20).IsRequired();
            e.Property(x => x.SearchText).HasMaxLength(460);
            e.Property(x => x.ContentType).HasMaxLength(150);
            e.Property(x => x.UploadedBy).HasMaxLength(150);
            e.HasIndex(x => new { x.GroupId, x.Year });
            e.HasOne(x => x.Group).WithMany(g => g.Works)
                .HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AppSetting>(e =>
        {
            e.ToTable("settings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
        });
    }
}
