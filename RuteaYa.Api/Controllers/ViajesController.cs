using Microsoft.AspNetCore.Mvc;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Api.Controllers;

[ApiController]
[Route("api/viajes")]
public class ViajesController : ControllerBase
{
    private readonly IViajesLogica _viajesLogica;

    public ViajesController(IViajesLogica viajesLogica)
    {
        _viajesLogica = viajesLogica;
    }

    [HttpGet]
    public async Task<IActionResult> ListarViajes(
        [FromQuery] int usuarioId,
        [FromQuery] string estado = "todos"
    ) {
        if(usuarioId <= 0)
        {
            return BadRequest(new {mensaje = "El usuario no es valido."});
        }
        if(estado is not ("todos" or "activos" or "inactivos"))
        {
            return BadRequest(new {mensaje = "El filto no es valido."});
        }

        var viajes = await _viajesLogica.ListarViajesAsync(usuarioId, estado);
        return Ok(viajes);
    }

    [HttpGet]
    public async Task<IActionResult> ObtenerPorId(int id, [FromQuery] int usuarioId)
    {
        if(usuarioId <= 0)
        {
            return BadRequest(new {mensaje = "El usuario no es valido."});
        }

        var viaje = await _viajesLogica.ObtenerPorIdAsync(usuarioId, id);

        return viaje is null ? NotFound() : Ok(viaje);
    }

    [HttpPost]
    public async Task<IActionResult> CrearViaje(
        [FromQuery] int usuarioId,
        [FromBody] CrearViajeDto dto
    ) {
        if(usuarioId <= 0)
        {
            return BadRequest(new {mensaje = "El usuario no es valido."});
        }

        try {
            var resultado = await _viajesLogica.CrearViajeAsync(usuarioId, dto);
            
            if (!resultado.Exito) {
                return BadRequest(new { mensaje = resultado.mensaje });
            }
            
            var viaje = resultado.viaje;

            if(viaje is null)
            {
                return BadRequest(new { mensaje = "No se pudo crear el viaje." });
            }

            return CreatedAtAction(
                nameof(ObtenerPorId),
                new {id = viaje.Id, usuarioId},
                viaje
            );
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new {mensaje = exception.Message});
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> EditarViaje(
        int id, 
        [FromQuery] int usuarioId,
        [FromBody] EditarViajeDto dto
    ) {
        if(usuarioId <= 0)
        {
            return BadRequest(new {mensaje = "El usuario no es valido."});
        }

        try {
            var resultado = await _viajesLogica.EditarViajeAsync(usuarioId, id, dto);

            if (!resultado.exito)
            {
                return BadRequest(new { mensaje = resultado.mensaje });
            }
            if (resultado.viaje is null)
            {
                return NotFound();
            }

            return Ok(resultado.viaje);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new {mensaje = exception.Message});
        }
    }

    [HttpPatch("{id:int}/desactivar")]
    public async Task<IActionResult> DesactivarViaje(int id, [FromQuery] int usuarioId)
    {
        if(usuarioId <= 0)
        {
            return BadRequest(new {mensaje = "El usuario no es valido."});
        }

        var resultado = await _viajesLogica.DesactivarViajeAsync(usuarioId, id);

        if (!resultado.exito)
        {
            return NotFound(new { mensaje = resultado.mensaje });
        }

        return NoContent();
    }

    [HttpPost("{id:int}/paradas")]
    public async Task<IActionResult> AgregarParada(
        int id,
        [FromQuery] int usuarioId,
        [FromBody] AgregarParadaRequest dto
    ) {
        if(usuarioId <= 0)
        {
            return BadRequest(new {mensaje = "El usuario no es valido."});
        }

        try {
            var resultado = await _viajesLogica.AgregarParadaAsync(
                usuarioId, 
                id, 
                dto.Ubicacion, 
                dto.Orden
            );

            if (!resultado.exito)
            {
                return BadRequest(new { mensaje = resultado.mensaje });
            }

            return Ok(new
            {
                mensaje = resultado.mensaje,
                parada = resultado.parada
            });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new {mensaje = exception.Message});
        }
    }

    [HttpDelete("{id:int}/paradas/{paradaId:int}")]
    public async Task<IActionResult> EliminarParada(
        int id, 
        int paradaId,
        [FromQuery] int usuarioId
    ) {
        if(usuarioId <= 0)
        {
            return BadRequest(new {mensaje = "El usuario no es valido."});
        }

        try {
            var resultado = await _viajesLogica.EliminarParadaAsync(
                usuarioId, 
                id, 
                paradaId
            );

            if (!resultado.exito)
            {
                return NotFound(new { mensaje = resultado.mensaje });
            }

            return NoContent();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new {mensaje = exception.Message});
        }
    }

    public class AgregarParadaRequest
    {
        public string Ubicacion { get; set; } = string.Empty;
        public int Orden { get; set; }
    }
}