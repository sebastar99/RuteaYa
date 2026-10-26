
using Microsoft.EntityFrameworkCore;

namespace RuteaYa.Entidades
{
    public class RuteaYaDbContext : DbContext
    {
        public RuteaYaDbContext(
            DbContextOptions<RuteaYaDbContext> options)
            : base(options)
        {
        }

        public DbSet<Usuario> Usuarios => Set<Usuario>();

        public DbSet<Viaje> Viajes => Set<Viaje>();
        public DbSet<Parada> Paradas => Set<Parada>();
        public DbSet<DiaViaje> DiasViaje => Set<DiaViaje>();


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Usuario>(entity =>
            {
                entity.ToTable("Usuarios");

                entity.HasKey(u => u.Id);

                entity.HasIndex(u => u.Email)
                    .IsUnique();

                entity.Property(u => u.Nombre)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(u => u.Apellido)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(u => u.Email)
                    .HasMaxLength(256)
                    .IsRequired();

                entity.Property(u => u.PasswordHash)
                    .IsRequired();

                entity.Property(u => u.CreadoEn)
                    .HasColumnType("datetime2")
                    .IsRequired();
            });

            modelBuilder.Entity<Viaje>(entity => 
            {
                entity.ToTable("Viajes");

                entity.HasKey(v => v.Id);

                entity.Property(v => v.Origen).HasMaxLength(150).IsRequired();
                entity.Property(v => v.Destino).HasMaxLength(150).IsRequired();

                entity.Property(v => v.PesoMax).HasPrecision(10, 2);
                entity.Property(v => v.DesvioMaxKm).HasPrecision(10, 2);

                entity.HasMany(v => v.Paradas)
                    .WithOne(p => p.Viaje)
                    .HasForeignKey(p => p.ViajeId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(v => v.Dias)
                    .WithOne(d => d.Viaje)
                    .HasForeignKey(d => d.ViajeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Parada>(entity => 
            {
                entity.ToTable("Paradas");

                entity.HasKey(p => p.Id);

                entity.Property(p => p.Ubicacion).HasMaxLength(150).IsRequired();
                entity.HasIndex(p => new { p.ViajeId, p.Orden }).IsUnique();
            });

            modelBuilder.Entity<DiaViaje>(entity => 
            {
                entity.ToTable("DiasViaje");

                entity.HasKey(d => d.Id);

                entity.HasIndex(d => new { d.ViajeId, d.DiaSemana }).IsUnique();
            });
        }
    }
}
