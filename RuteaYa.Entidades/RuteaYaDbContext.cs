
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

        public DbSet<Envio> Envios => Set<Envio>();


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

            modelBuilder.Entity<Envio>(entity =>
            {
                entity.ToTable("Envios");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.OrigenDir).HasMaxLength(200).IsRequired();
                entity.Property(e => e.DestinoDir).HasMaxLength(200).IsRequired();
                entity.Property(e => e.Descripcion).HasMaxLength(500).IsRequired();

                entity.Property(e => e.PesoKg).HasPrecision(10, 2);
                entity.Property(e => e.Monto).HasPrecision(10, 2);
                entity.Property(e => e.OrigenLat).HasPrecision(18, 6);
                entity.Property(e => e.OrigenLng).HasPrecision(18, 6);
                entity.Property(e => e.DestinoLat).HasPrecision(18, 6);
                entity.Property(e => e.DestinoLng).HasPrecision(18, 6);

                entity.Property(e => e.RowVersion)
                    .IsRowVersion();

                entity.HasOne(e => e.Usuario)
                    .WithMany()
                    .HasForeignKey(e => e.UsuarioId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            
    }
    }
}
