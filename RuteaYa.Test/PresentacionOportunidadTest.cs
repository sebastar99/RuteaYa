using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RuteaYa.Entidades;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;
using RuteaYa.Web.Models;

namespace RuteaYa.Test;

public class PresentacionOportunidadTest
{
    private static SalidaViajeDto Salida(int cupos = 5, decimal peso = 50, bool disponible = true) => new()
    { Id = 3, ViajeId = 10, FechaHora = new DateTimeOffset(2030, 1, 7, 8, 0, 0, TimeSpan.FromHours(-3)), Disponible = disponible,
        CuposDisponibles = cupos, PesoDisponibleKg = peso, LlegadaMaxima = RestriccionTemporalDto.SinRestriccion() };

    private static EvaluacionOportunidadDto Motor(EstadoCompatibilidad estado, params MotivoCompatibilidad[] motivos) => new()
    {
        ResultadoMotor = new ResultadoCompatibilidadDto { ViajeId = 10, EnvioId = 25, Estado = estado, Motivos = motivos,
            InsercionSeleccionada = motivos.Contains(MotivoCompatibilidad.DesvioAprobado) ? new() { Desvio = new() {
                DistanciaAdicionalMetros = 6599, TiempoAdicionalSegundos = 804 } } : null }
    };

    private static DetalleCandidatoDto Detalle(EvaluacionOportunidadDto evaluacion, SalidaViajeDto? salida, bool activo = true, decimal peso = 1) => new()
    {
        Viaje = new() { Id = 10, Origen = "A", Destino = "B", Activo = activo, DesvioMaxKm = 10, SalidaSeleccionada = salida },
        Envio = new() { Id = 25, PesoKg = peso, Descripcion = "Caja" }, Evaluacion = evaluacion
    };

    private static readonly MotivoCompatibilidad[] TodoAprobado = { MotivoCompatibilidad.DesvioAprobado, MotivoCompatibilidad.HorarioAprobado };

    private static ResultadoCondicion De(DetalleCandidatoDto d, string nombre) => PresentacionOportunidad.Condiciones(d).Single(c => c.Nombre == nombre).Resultado;

    [Fact]
    public void DesvioNegativo_SeMuestraConSignoYNoComoMasMenos()
    {
        Assert.Equal("+6,6".Replace(',', System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0]), PresentacionOportunidad.Firmado(6.6m, "0.#"));
        Assert.StartsWith("-", PresentacionOportunidad.Firmado(-5.4m, "0.#"));
        Assert.DoesNotContain("+-", PresentacionOportunidad.Firmado(-17m, "0"));
        Assert.Equal("0", PresentacionOportunidad.Firmado(0m, "0"));
    }

    [Fact]
    public void TresEstados_ConSuColor()
    {
        Assert.Equal(("Compatible", "green"), Par(Motor(EstadoCompatibilidad.Compatible, TodoAprobado)));
        Assert.Equal(("No compatible", "red"), Par(Motor(EstadoCompatibilidad.Incompatible, MotivoCompatibilidad.SinCupo)));
        Assert.Equal(("Pendiente de evaluación", "orange"), Par(Motor(EstadoCompatibilidad.Indeterminado, MotivoCompatibilidad.HorarioRetiroNoVerificable)));
        Assert.Equal(("Pendiente de evaluación", "orange"), Par(new EvaluacionOportunidadDto()));
        Assert.Equal(("Pendiente de evaluación", "orange"), Par(null));
        // Un "compatible" con datos pendientes nunca se muestra como compatible.
        var conPendientes = Motor(EstadoCompatibilidad.Compatible, TodoAprobado);
        conPendientes.DatosPendientes = new[] { DatoPendienteOportunidad.InicioRetiroDesconocido };
        Assert.Equal(("Pendiente de evaluación", "orange"), Par(conPendientes));
    }

    private static (string, string) Par(EvaluacionOportunidadDto? e) { var s = PresentacionOportunidad.Estado(e); return (PresentacionOportunidad.Etiqueta(s), PresentacionOportunidad.Color(s)); }

    [Fact]
    public void Compatible_CumpleLasCincoCondicionesYSePuedeAceptar()
    {
        var d = Detalle(Motor(EstadoCompatibilidad.Compatible, TodoAprobado), Salida());
        Assert.All(PresentacionOportunidad.Condiciones(d), c => Assert.Equal(ResultadoCondicion.Cumple, c.Resultado));
        Assert.Equal(5, PresentacionOportunidad.Condiciones(d).Count);
        Assert.Null(PresentacionOportunidad.NoSePuedeAceptar(d));
        Assert.Equal((6.6m, 13m), PresentacionOportunidad.Adicional(d.Evaluacion));
        Assert.Equal("Entra en tu recorrido.", PresentacionOportunidad.Resumen(d.Evaluacion));
    }

    [Theory]
    [InlineData("Peso permitido", MotivoCompatibilidad.PesoExcedido, "El paquete supera el peso que te queda.")]
    [InlineData("Cupos disponibles", MotivoCompatibilidad.SinCupo, "No te quedan cupos en esta salida.")]
    [InlineData("Desvío permitido", MotivoCompatibilidad.DesvioExcedido, "Se desvía más de lo que permite tu viaje.")]
    [InlineData("Horarios compatibles", MotivoCompatibilidad.SalidaPosteriorAlVencimiento, "Tu salida es posterior a la fecha límite del envío.")]
    [InlineData("Horarios compatibles", MotivoCompatibilidad.LlegadaMaximaExcedida, "Llegarías después de tu horario máximo.")]
    public void Incompatible_MarcaSoloLaCondicionQueFallaYExplicaCuandoNoSePuedeAceptar(string condicion, MotivoCompatibilidad motivo, string resumen)
    {
        var d = Detalle(Motor(EstadoCompatibilidad.Incompatible, motivo), Salida());
        Assert.Equal(ResultadoCondicion.NoCumple, De(d, condicion));
        Assert.Equal("Disponibilidad", PresentacionOportunidad.Condiciones(d).First().Nombre);
        Assert.Equal(ResultadoCondicion.Cumple, De(d, "Disponibilidad"));
        Assert.Equal(resumen, PresentacionOportunidad.Resumen(d.Evaluacion));
        var no = PresentacionOportunidad.NoSePuedeAceptar(d)!;
        Assert.Equal(resumen, no.Motivo); Assert.False(string.IsNullOrWhiteSpace(no.Solucion));
    }

    [Fact]
    public void SinSalida_TodoPendienteYExplicaComoResolverlo()
    {
        var d = Detalle(new EvaluacionOportunidadDto { DatosPendientes = new[] { DatoPendienteOportunidad.SalidaConcretaDesconocida } }, null);
        Assert.Equal(ResultadoCondicion.Pendiente, De(d, "Disponibilidad"));
        Assert.Equal(ResultadoCondicion.Pendiente, De(d, "Peso permitido"));
        Assert.Equal(ResultadoCondicion.Pendiente, De(d, "Cupos disponibles"));
        Assert.Equal(ResultadoCondicion.Pendiente, De(d, "Desvío permitido"));
        Assert.Equal(ResultadoCondicion.Pendiente, De(d, "Horarios compatibles"));
        var no = PresentacionOportunidad.NoSePuedeAceptar(d)!;
        Assert.Contains("salida", no.Motivo); Assert.Contains("Oportunidades", no.Solucion);
    }

    [Fact]
    public void DatosPendientesDelEnvio_ExplicanQueFaltaYQuienDebeCompletarlo()
    {
        var e = Motor(EstadoCompatibilidad.Indeterminado, MotivoCompatibilidad.DesvioAprobado);
        e.DatosPendientes = new[] { DatoPendienteOportunidad.InicioRetiroDesconocido };
        var d = Detalle(e, Salida());
        Assert.Equal(ResultadoCondicion.Cumple, De(d, "Desvío permitido"));
        Assert.Equal(ResultadoCondicion.Pendiente, De(d, "Horarios compatibles"));
        var no = PresentacionOportunidad.NoSePuedeAceptar(d)!;
        Assert.Equal("Falta saber desde cuándo se puede retirar el paquete.", no.Motivo);
        Assert.Contains("remitente", no.Solucion);
    }

    [Fact]
    public void ViajeInactivoOSalidaNoDisponible_NoSePuedeAceptar()
    {
        var compatible = Motor(EstadoCompatibilidad.Compatible, TodoAprobado);
        var inactivo = Detalle(compatible, Salida(), activo: false);
        Assert.Equal(ResultadoCondicion.NoCumple, De(inactivo, "Disponibilidad")); Assert.Equal("Tu viaje está inactivo.", PresentacionOportunidad.NoSePuedeAceptar(inactivo)!.Motivo);
        var sinDisponibilidad = Detalle(compatible, Salida(disponible: false));
        Assert.Equal(ResultadoCondicion.NoCumple, De(sinDisponibilidad, "Disponibilidad")); Assert.NotNull(PresentacionOportunidad.NoSePuedeAceptar(sinDisponibilidad));
    }

    [Fact]
    public void Capacidad_UsaLosDatosRealesDeLaSalidaAunqueElMotorNoHayaCorrido()
    {
        var sinMotor = new EvaluacionOportunidadDto { DatosPendientes = new[] { DatoPendienteOportunidad.RecorridoSinCoordenadasConfirmadas } };
        var d = Detalle(sinMotor, Salida(cupos: 0, peso: 0.5m), peso: 1);
        Assert.Equal(ResultadoCondicion.NoCumple, De(d, "Peso permitido"));
        Assert.Equal(ResultadoCondicion.NoCumple, De(d, "Cupos disponibles"));
        Assert.Equal(ResultadoCondicion.Pendiente, De(d, "Desvío permitido"));
        Assert.Equal(ResultadoCondicion.Cumple, De(Detalle(sinMotor, Salida(cupos: 2, peso: 5)), "Peso permitido"));
    }

    [Fact]
    public async Task Listado_MuestraTarjetasConLosTresEstadosYSinTablaNiTerminosTecnicos()
    {
        var viaje = new ViajeOportunidadesDto { Id = 10, Origen = "Morón", Destino = "Luján", Activo = true, SalidaSeleccionada = Salida() };
        EnvioDto Envio(int id, string desc) => new() { Id = id, UsuarioId = 8, Descripcion = desc, OrigenDir = "Retiro " + id, DestinoDir = "Entrega " + id,
            PesoKg = 1, Tamano = TamanoEnvio.Pequeno, Monto = 4000, FechaLimite = new DateTime(2030, 1, 7) };
        var listado = new ListadoCandidatosDto { Viajes = new[] { viaje }, ViajeSeleccionado = viaje, TotalCandidatos = 3,
            Candidatos = new[] { Envio(1, "Verde"), Envio(2, "Rojo"), Envio(3, "Naranja") },
            Evaluaciones = new Dictionary<int, EvaluacionOportunidadDto> {
                [1] = Motor(EstadoCompatibilidad.Compatible, TodoAprobado),
                [2] = Motor(EstadoCompatibilidad.Incompatible, MotivoCompatibilidad.DesvioExcedido),
                [3] = Motor(EstadoCompatibilidad.Indeterminado, MotivoCompatibilidad.DesvioAprobado) } };
        var logica = new Mock<IOportunidadesLogica>();
        logica.Setup(l => l.ConsultarAsync(7, 10, 1, 10, It.IsAny<CancellationToken>())).ReturnsAsync(listado);
        await using var app = OportunidadesVistaTest.CrearHost(logica.Object); await app.StartAsync();
        try {
            var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var cliente = new HttpClient { BaseAddress = new Uri(url) }; cliente.DefaultRequestHeaders.Add("X-Usuario-Prueba", "7");
            var html = WebUtility.HtmlDecode(await cliente.GetStringAsync("/Oportunidades?viajeId=10"));
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, "class=\"opp-card ").Count);
            Assert.Contains("opp-card green", html); Assert.Contains("opp-card red", html); Assert.Contains("opp-card orange", html);
            Assert.Contains("panel-pill green\">Compatible", html); Assert.Contains("panel-pill red\">No compatible", html);
            Assert.Contains("panel-pill orange\">Pendiente de evaluación", html);
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(html, ">Ver detalle<").Count);
            Assert.DoesNotContain("<table", html);
            foreach (var tecnico in new[] { "HERE", "motor", "offset", "Indeterminado", "segmento", "snapshot" })
                Assert.DoesNotContain(tecnico, html, StringComparison.OrdinalIgnoreCase);
        } finally { await app.StopAsync(); }
    }
}
