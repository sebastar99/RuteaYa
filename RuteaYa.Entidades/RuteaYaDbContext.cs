
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
        public DbSet<SalidaViaje> SalidasViaje => Set<SalidaViaje>();
        public DbSet<UbicacionConfirmada> UbicacionesConfirmadas => Set<UbicacionConfirmada>();

        private void ValidarSnapshots()
        {
            ChangeTracker.DetectChanges();
            foreach (var entrada in ChangeTracker.Entries<UbicacionConfirmada>())
            {
                if (entrada.State == EntityState.Deleted || entrada.State == EntityState.Modified &&
                    entrada.Properties.Any(p => p.IsModified && p.Metadata.Name is not (nameof(UbicacionConfirmada.ConsumidaEnUtc) or nameof(UbicacionConfirmada.Version))))
                    throw new InvalidOperationException("Las ubicaciones confirmadas son snapshots inmutables.");
                if (entrada.State == EntityState.Modified)
                {
                    var u = entrada.Entity;
                    var consumoAnterior = entrada.OriginalValues.GetValue<DateTimeOffset?>(nameof(UbicacionConfirmada.ConsumidaEnUtc));
                    if (consumoAnterior.HasValue || !u.ConsumidaEnUtc.HasValue || u.Version == entrada.OriginalValues.GetValue<Guid>(nameof(UbicacionConfirmada.Version)) ||
                        Database.CurrentTransaction == null || !TieneVinculoPendiente(u))
                        throw new InvalidOperationException("Un comprobante se consume una sola vez, junto al guardado de su vínculo y dentro de una transacción.");
                }
            }
        }

        private bool TieneVinculoPendiente(UbicacionConfirmada u)
        {
            bool Pendiente(EntityState estado) => estado is EntityState.Added or EntityState.Modified;
            return u.Campo switch
            {
                CampoUbicacion.OrigenViaje => ChangeTracker.Entries<Viaje>().Any(e => Pendiente(e.State) && e.Entity.UsuarioId == u.UsuarioId && e.Entity.OrigenUbicacionId == u.Id),
                CampoUbicacion.DestinoViaje => ChangeTracker.Entries<Viaje>().Any(e => Pendiente(e.State) && e.Entity.UsuarioId == u.UsuarioId && e.Entity.DestinoUbicacionId == u.Id),
                CampoUbicacion.Parada => ChangeTracker.Entries<Parada>().Any(e => Pendiente(e.State) && e.Entity.UbicacionConfirmadaId == u.Id && e.Entity.Viaje?.UsuarioId == u.UsuarioId),
                CampoUbicacion.RetiroEnvio => ChangeTracker.Entries<Envio>().Any(e => Pendiente(e.State) && e.Entity.UsuarioId == u.UsuarioId && e.Entity.RetiroUbicacionId == u.Id),
                CampoUbicacion.EntregaEnvio => ChangeTracker.Entries<Envio>().Any(e => Pendiente(e.State) && e.Entity.UsuarioId == u.UsuarioId && e.Entity.EntregaUbicacionId == u.Id),
                _ => false
            };
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        { ValidarSnapshots(); return base.SaveChanges(acceptAllChangesOnSuccess); }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        { ValidarSnapshots(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }


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
                entity.HasOne(v => v.OrigenUbicacion).WithMany().HasForeignKey(v => v.OrigenUbicacionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(v => v.DestinoUbicacion).WithMany().HasForeignKey(v => v.DestinoUbicacionId).OnDelete(DeleteBehavior.Restrict);

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
                entity.HasOne(p => p.UbicacionConfirmada).WithMany().HasForeignKey(p => p.UbicacionConfirmadaId).OnDelete(DeleteBehavior.Restrict);
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
                entity.HasOne(e => e.SalidaViaje).WithMany().HasForeignKey(e => e.SalidaViajeId).OnDelete(DeleteBehavior.Restrict);

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

                entity.HasOne(e => e.Conductor)
                    .WithMany()
                    .HasForeignKey(e => e.ConductorId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => e.ConductorId);
                entity.HasOne(e => e.RetiroUbicacion).WithMany().HasForeignKey(e => e.RetiroUbicacionId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.EntregaUbicacion).WithMany().HasForeignKey(e => e.EntregaUbicacionId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<SalidaViaje>(entity =>
            {
                entity.ToTable("SalidasViaje");
                entity.HasKey(s => s.Id);
                entity.HasIndex(s => new { s.ViajeId, s.Fecha }).IsUnique();
                entity.HasOne(s => s.Viaje).WithMany().HasForeignKey(s => s.ViajeId).OnDelete(DeleteBehavior.Restrict);
                entity.Property(s => s.Version).IsConcurrencyToken();
            });

            modelBuilder.Entity<UbicacionConfirmada>(entity =>
            {
                entity.ToTable("UbicacionesConfirmadas", tabla =>
                {
                    tabla.HasCheckConstraint("CK_Ubicacion_Posicion", "[PosicionLatitud] BETWEEN -90 AND 90 AND [PosicionLongitud] BETWEEN -180 AND 180");
                    tabla.HasCheckConstraint("CK_Ubicacion_Navegacion", "[NavegacionLatitud] BETWEEN -90 AND 90 AND [NavegacionLongitud] BETWEEN -180 AND 180");
                    tabla.HasCheckConstraint("CK_Ubicacion_Acceso", "[AccesoElegido] >= -1");
                    tabla.HasCheckConstraint("CK_Ubicacion_Ambito", "([TipoRecurso] = 1 AND [Campo] IN (1,2,3)) OR ([TipoRecurso] = 2 AND [Campo] IN (4,5))");
                });
                entity.HasKey(u => u.Id);
                entity.Property(u => u.Id).ValueGeneratedNever();
                entity.Property(u => u.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
                entity.Property(u => u.SeleccionHash).HasMaxLength(64).IsUnicode(false).IsRequired();
                entity.HasIndex(u => u.TokenHash).IsUnique();
                entity.HasIndex(u => u.SeleccionHash).IsUnique();
                entity.HasIndex(u => u.VenceEnUtc);
                entity.Property(u => u.ClaveCampo).HasMaxLength(64).IsRequired();
                entity.Property(u => u.RevisionRecurso).HasMaxLength(64).IsUnicode(false);
                entity.Property(u => u.IdHere).HasMaxLength(512).IsRequired();
                entity.Property(u => u.DireccionNormalizada).HasMaxLength(512).IsRequired();
                entity.Property(u => u.TipoResultado).HasMaxLength(80).IsRequired();
                entity.Property(u => u.TipoNumeroPuerta).HasMaxLength(80);
                entity.Property(u => u.Proveedor).HasMaxLength(20).IsRequired();
                entity.Property(u => u.PosicionLatitud).HasPrecision(18, 8);
                entity.Property(u => u.PosicionLongitud).HasPrecision(18, 8);
                entity.Property(u => u.NavegacionLatitud).HasPrecision(18, 8);
                entity.Property(u => u.NavegacionLongitud).HasPrecision(18, 8);
                entity.Property(u => u.Version).IsConcurrencyToken();
                entity.HasOne<Usuario>().WithMany().HasForeignKey(u => u.UsuarioId).OnDelete(DeleteBehavior.Restrict);
            });

            
    }
    }
}
