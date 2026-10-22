
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
        }
    }
}
