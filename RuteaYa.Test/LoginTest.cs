
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RuteaYa.Web.Controllers;
using RuteaYa.Web.Models;
using RuteaYa.Web.Services;
using Xunit;

namespace RuteaYa.Test
{
    public class LoginTests
    {
        private static AccesoController CrearControlador(
            IUsuariosApiServicio servicio,
            out DefaultHttpContext httpContext,
            out Mock<IAuthenticationService> autenticacionMock)
        {
            autenticacionMock =
                new Mock<IAuthenticationService>();

            autenticacionMock
                .Setup(x => x.SignInAsync(
                    It.IsAny<HttpContext>(),
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties?>()))
                .Returns(Task.CompletedTask);

            autenticacionMock
                .Setup(x => x.SignOutAsync(
                    It.IsAny<HttpContext>(),
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    It.IsAny<AuthenticationProperties?>()))
                .Returns(Task.CompletedTask);

            var servicios = new ServiceCollection();

            servicios.AddSingleton<IAuthenticationService>(
                autenticacionMock.Object);

            servicios.AddSingleton<IUrlHelperFactory, UrlHelperFactory>();

            servicios.AddSingleton<ITempDataProvider,
                TempDataProviderFalso>();

            servicios.AddSingleton<ITempDataDictionaryFactory,
                TempDataDictionaryFactory>();

            var proveedor = servicios.BuildServiceProvider();

            httpContext = new DefaultHttpContext
            {
                RequestServices = proveedor
            };

            var actionDescriptor = new ControllerActionDescriptor
            {
                ControllerName = "Acceso",
                ActionName = "Login"
            };

            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                actionDescriptor);

            return new AccesoController(servicio)
            {
                ControllerContext =
                    new ControllerContext(actionContext)
            };
        }

        private static LoginUsuarioViewModel CrearModeloLogin()
        {
            return new LoginUsuarioViewModel
            {
                Email = "usuario@ejemplo.com",
                Password = "Clave123!"
            };
        }

        private static UsuarioApiRespuesta CrearUsuario()
        {
            return new UsuarioApiRespuesta
            {
                Id = 1,
                Nombre = "Sebastian",
                Apellido = "Prueba",
                Email = "usuario@ejemplo.com"
            };
        }

        private const string TokenPrueba = "token-falso-para-tests";

        [Fact]
        public async Task Login_CredencialesIncorrectas_RetornaVistaConError()
        {
            var servicioMock = new Mock<IUsuariosApiServicio>();

            servicioMock
                .Setup(x => x.LoginAsync(
                    It.IsAny<LoginUsuarioViewModel>()))
                .ReturnsAsync((
                    Exito: false,
                    Mensaje: "Credenciales incorrectas",
                    Usuario: (UsuarioApiRespuesta?)null,
                    Token: (string?)null));

            var controlador = CrearControlador(
                servicioMock.Object,
                out var httpContext,
                out var autenticacionMock);

            var modelo = CrearModeloLogin();

            var resultado = await controlador.Login(modelo);

            var vista = Assert.IsType<ViewResult>(resultado);

            Assert.Null(vista.ViewName);
            Assert.Same(modelo, vista.Model);

            Assert.Contains(
                controlador.ModelState[string.Empty]!.Errors,
                e => e.ErrorMessage == "Credenciales incorrectas");

            servicioMock.Verify(
                x => x.LoginAsync(It.IsAny<LoginUsuarioViewModel>()),
                Times.Once);

            autenticacionMock.Verify(
                x => x.SignInAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties?>()),
                Times.Never);

            Assert.False(
                httpContext.User.Identity?.IsAuthenticated ?? false);
        }

        [Fact]
        public async Task Login_CredencialesCorrectas_RedireccionaAInicio()
        {
            var servicioMock = new Mock<IUsuariosApiServicio>();

            var usuario = CrearUsuario();

            servicioMock
                .Setup(x => x.LoginAsync(
                    It.IsAny<LoginUsuarioViewModel>()))
                .ReturnsAsync((
                    Exito: true,
                    Mensaje: "Login correcto",
                    Usuario: (UsuarioApiRespuesta?)usuario,
                    Token: (string?)TokenPrueba));

            var controlador = CrearControlador(
                servicioMock.Object,
                out _,
                out var autenticacionMock);

            var resultado = await controlador.Login(CrearModeloLogin());

            var redireccion =
                Assert.IsType<RedirectToActionResult>(resultado);

            Assert.Equal("Inicio", redireccion.ActionName);

            autenticacionMock.Verify(
                x => x.SignInAsync(
                    It.IsAny<HttpContext>(),
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    It.Is<ClaimsPrincipal>(p =>
                        p.FindFirst(ClaimTypes.NameIdentifier)!.Value == "1" &&
                        p.FindFirst(ClaimTypes.Name)!.Value == "Sebastian" &&
                        p.FindFirst(ClaimTypes.Email)!.Value ==
                            "usuario@ejemplo.com"),
                    It.Is<AuthenticationProperties?>(propiedades =>
                        propiedades != null &&
                        propiedades.GetTokenValue("access_token") ==
                            TokenPrueba &&
                        propiedades.GetTokenValue("token_type") == "Bearer")),
                Times.Once);

            servicioMock.Verify(
                x => x.LoginAsync(It.IsAny<LoginUsuarioViewModel>()),
                Times.Once);
        }

        [Fact]
        public async Task Login_ModeloInvalido_NoConsultaLaApi()
        {
            var servicioMock = new Mock<IUsuariosApiServicio>();

            var controlador = CrearControlador(
                servicioMock.Object,
                out _,
                out var autenticacionMock);

            controlador.ModelState.AddModelError(
                "Email", "El email es obligatorio");

            var modelo = CrearModeloLogin();

            var resultado = await controlador.Login(modelo);

            var vista = Assert.IsType<ViewResult>(resultado);

            Assert.Same(modelo, vista.Model);

            servicioMock.Verify(
                x => x.LoginAsync(It.IsAny<LoginUsuarioViewModel>()),
                Times.Never);

            autenticacionMock.Verify(
                x => x.SignInAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties?>()),
                Times.Never);
        }

        [Fact]
        public async Task Login_RespuestaExitosaSinUsuario_RechazaAcceso()
        {
            var servicioMock = new Mock<IUsuariosApiServicio>();

            servicioMock
                .Setup(x => x.LoginAsync(
                    It.IsAny<LoginUsuarioViewModel>()))
                .ReturnsAsync((
                    Exito: true,
                    Mensaje: "No se recibió el usuario",
                    Usuario: (UsuarioApiRespuesta?)null,
                    Token: (string?)TokenPrueba));

            var controlador = CrearControlador(
                servicioMock.Object,
                out _,
                out var autenticacionMock);

            var resultado = await controlador.Login(CrearModeloLogin());

            Assert.IsType<ViewResult>(resultado);

            Assert.Contains(
                controlador.ModelState[string.Empty]!.Errors,
                e => e.ErrorMessage == "No se recibió el usuario");

            autenticacionMock.Verify(
                x => x.SignInAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties?>()),
                Times.Never);
        }

        [Fact]
        public async Task Login_RespuestaExitosaSinToken_RechazaAcceso()
        {
            var servicioMock = new Mock<IUsuariosApiServicio>();

            servicioMock
                .Setup(x => x.LoginAsync(
                    It.IsAny<LoginUsuarioViewModel>()))
                .ReturnsAsync((
                    Exito: true,
                    Mensaje: "Login correcto",
                    Usuario: (UsuarioApiRespuesta?)CrearUsuario(),
                    Token: (string?)null));

            var controlador = CrearControlador(
                servicioMock.Object,
                out _,
                out var autenticacionMock);

            var resultado = await controlador.Login(CrearModeloLogin());

            Assert.IsType<ViewResult>(resultado);

            autenticacionMock.Verify(
                x => x.SignInAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties?>()),
                Times.Never);
        }

        [Fact]
        public async Task Login_ErrorDeConexion_MuestraMensaje()
        {
            var servicioMock = new Mock<IUsuariosApiServicio>();

            servicioMock
                .Setup(x => x.LoginAsync(
                    It.IsAny<LoginUsuarioViewModel>()))
                .ThrowsAsync(new HttpRequestException());

            var controlador = CrearControlador(
                servicioMock.Object,
                out _,
                out var autenticacionMock);

            var resultado = await controlador.Login(CrearModeloLogin());

            Assert.IsType<ViewResult>(resultado);

            Assert.Contains(
                controlador.ModelState[string.Empty]!.Errors,
                e => e.ErrorMessage.Contains(
                    "No se pudo conectar con el servidor"));

            autenticacionMock.Verify(
                x => x.SignInAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties?>()),
                Times.Never);
        }

        [Fact]
        public async Task Login_TiempoDeEspera_MuestraMensaje()
        {
            var servicioMock = new Mock<IUsuariosApiServicio>();

            servicioMock
                .Setup(x => x.LoginAsync(
                    It.IsAny<LoginUsuarioViewModel>()))
                .ThrowsAsync(new TaskCanceledException());

            var controlador = CrearControlador(
                servicioMock.Object,
                out _,
                out var autenticacionMock);

            var resultado = await controlador.Login(CrearModeloLogin());

            Assert.IsType<ViewResult>(resultado);

            Assert.Contains(
                controlador.ModelState[string.Empty]!.Errors,
                e => e.ErrorMessage.Contains(
                    "El servidor tardó demasiado en responder"));

            autenticacionMock.Verify(
                x => x.SignInAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<ClaimsPrincipal>(),
                    It.IsAny<AuthenticationProperties?>()),
                Times.Never);
        }

        [Fact]
        public void LoginGet_UsuarioAutenticado_RedireccionaAInicio()
        {
            var servicioMock = new Mock<IUsuariosApiServicio>();

            var controlador = CrearControlador(
                servicioMock.Object,
                out var httpContext,
                out _);

            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.Name, "Sebastian")
                    },
                    CookieAuthenticationDefaults.AuthenticationScheme));

            var resultado = controlador.Login();

            var redireccion =
                Assert.IsType<RedirectToActionResult>(resultado);

            Assert.Equal("Inicio", redireccion.ActionName);
        }

        [Fact]
        public void LoginGet_UsuarioNoAutenticado_MuestraFormulario()
        {
            var servicioMock = new Mock<IUsuariosApiServicio>();

            var controlador = CrearControlador(
                servicioMock.Object,
                out _,
                out _);

            var resultado = controlador.Login();

            var vista = Assert.IsType<ViewResult>(resultado);

            Assert.Null(vista.ViewName);
            Assert.IsType<LoginUsuarioViewModel>(vista.Model);
        }

        [Fact]
        public async Task Logout_CierraSesionYRedireccionaALogin()
        {
            var servicioMock = new Mock<IUsuariosApiServicio>();

            var controlador = CrearControlador(
                servicioMock.Object,
                out _,
                out var autenticacionMock);

            var resultado = await controlador.Logout();

            var redireccion =
                Assert.IsType<RedirectToActionResult>(resultado);

            Assert.Equal("Login", redireccion.ActionName);

            autenticacionMock.Verify(
                x => x.SignOutAsync(
                    It.IsAny<HttpContext>(),
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    It.IsAny<AuthenticationProperties?>()),
                Times.Once);
        }
    }

    public class TempDataProviderFalso : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(
            HttpContext context)
        {
            return new Dictionary<string, object>();
        }

        public void SaveTempData(
            HttpContext context,
            IDictionary<string, object> values)
        {
        }
    }
}
