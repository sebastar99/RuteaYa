
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using RuteaYa.Web.Models;
using Xunit;

namespace RuteaYa.Test
{
    public class RegistroTests
    {
        private static IList<ValidationResult> ValidarModelo(
            RegistroUsuarioViewModel modelo)
        {
            var resultados = new List<ValidationResult>();
            var contexto = new ValidationContext(modelo);

            Validator.TryValidateObject(
                modelo,
                contexto,
                resultados,
                validateAllProperties: true);

            return resultados;
        }

        private static RegistroUsuarioViewModel ModeloValido()
        {
            return new RegistroUsuarioViewModel
            {
                Nombre = "Sebastian",
                Apellido = "Staropoli",
                Email = "datahola22@gmail.com",
                Password = "12345678",
                ConfirmarPassword = "12345678"
            };
        }

        [Fact]
        public void Registro_ModeloValido_NoTieneErrores()
        {
            var modelo = ModeloValido();

            var errores = ValidarModelo(modelo);

            Assert.Empty(errores);
        }

        [Fact]
        public void Registro_NombreVacio_DevuelveError()
        {
            var modelo = ModeloValido();
            modelo.Nombre = "";

            var errores = ValidarModelo(modelo);

            Assert.Contains(
                errores,
                e => e.MemberNames.Contains(nameof(modelo.Nombre)));
        }

        [Fact]
        public void Registro_ApellidoVacio_DevuelveError()
        {
            var modelo = ModeloValido();
            modelo.Apellido = "";

            var errores = ValidarModelo(modelo);

            Assert.Contains(
                errores,
                e => e.MemberNames.Contains(nameof(modelo.Apellido)));
        }

        [Fact]
        public void Registro_EmailInvalido_DevuelveError()
        {
            var modelo = ModeloValido();
            modelo.Email = "correo-invalido";

            var errores = ValidarModelo(modelo);

            Assert.Contains(
                errores,
                e => e.MemberNames.Contains(nameof(modelo.Email)));
        }

        [Fact]
        public void Registro_PasswordVacio_DevuelveError()
        {
            var modelo = ModeloValido();
            modelo.Password = "";
            modelo.ConfirmarPassword = "";

            var errores = ValidarModelo(modelo);

            Assert.Contains(
                errores,
                e => e.MemberNames.Contains(nameof(modelo.Password)));
        }

        [Fact]
        public void Registro_PasswordCorta_DevuelveError()
        {
            var modelo = ModeloValido();
            modelo.Password = "Ab1!";
            modelo.ConfirmarPassword = "Ab1!";

            var errores = ValidarModelo(modelo);

            Assert.Contains(
                errores,
                e => e.MemberNames.Contains(nameof(modelo.Password)));
        }

        [Fact]
        public void Registro_ConfirmacionDiferente_DevuelveError()
        {
            var modelo = ModeloValido();
            modelo.ConfirmarPassword = "OtraClave123!";

            var errores = ValidarModelo(modelo);

            Assert.Contains(
                errores,
                e => e.MemberNames.Contains(
                    nameof(modelo.ConfirmarPassword)));
        }

        [Fact]
        public void Registro_EmailVacio_DevuelveError()
        {
            var modelo = ModeloValido();
            modelo.Email = "";

            var errores = ValidarModelo(modelo);

            Assert.Contains(
                errores,
                e => e.MemberNames.Contains(nameof(modelo.Email)));
        }
    }
}
