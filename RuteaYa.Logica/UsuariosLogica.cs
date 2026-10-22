
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RuteaYa.Entidades;

namespace RuteaYa.Logica
{
    public class UsuariosLogica : IUsuariosLogica
    {
        private readonly RuteaYaDbContext _context;
        private readonly IPasswordHasher<Usuario> _passwordHasher;

        public UsuariosLogica(
            RuteaYaDbContext context,
            IPasswordHasher<Usuario> passwordHasher)
        {
            _context = context;
            _passwordHasher = passwordHasher;
        }

        public async Task<(bool Exito, string Mensaje, Usuario? Usuario)>
            RegistrarAsync(Usuario usuario, string password)
        {
            if (string.IsNullOrWhiteSpace(usuario.Email))
                return (false, "El correo es obligatorio.", null);

            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                return (false, "La contraseña debe tener al menos 8 caracteres.", null);

            usuario.Email = usuario.Email.Trim().ToLowerInvariant();

            bool correoExiste = await _context.Usuarios
                .AnyAsync(u => u.Email == usuario.Email);

            if (correoExiste)
                return (false, "Ya existe una cuenta con ese correo.", null);

            usuario.PasswordHash =
                _passwordHasher.HashPassword(usuario, password);

            usuario.CreadoEn = DateTime.UtcNow;

            _context.Usuarios.Add(usuario);
            await _context.SaveChangesAsync();

            return (true, "Usuario registrado correctamente.", usuario);
        }

        public async Task<Usuario?> ValidarCredencialesAsync(
            string email,
            string password)
        {
            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
                return null;

            email = email.Trim().ToLowerInvariant();

            var usuario = await _context.Usuarios
                .SingleOrDefaultAsync(u => u.Email == email);

            if (usuario == null)
                return null;

            var resultado = _passwordHasher.VerifyHashedPassword(
                usuario,
                usuario.PasswordHash,
                password);

            return resultado == PasswordVerificationResult.Failed
                ? null
                : usuario;
        }
    }
}
