using Microsoft.EntityFrameworkCore;

namespace TrippinEdi;

internal class DiscoveryContext : DbContext
{
    public DbSet<Interest> Interests { get; set; }
    public DbSet<Dislike> Dislikes { get; set; }
    public DbSet<PastDiscovery> PastDiscoveries { get; set; }
    public DbSet<PendingDiscovery> PendingDiscoveries { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Filename=discoveries.db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Interest>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
        });

        modelBuilder.Entity<Dislike>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired();
        });

        modelBuilder.Entity<PastDiscovery>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).IsRequired();
        });

        modelBuilder.Entity<PendingDiscovery>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).IsRequired();
        });
    }
}
