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
        if(estado is not ("todos" or "activo" or "inactivo"))
        {
            return BadRequest(new {mensaje = "El estado debe ser todos, actiovo o inactivo"});
        }

        var viajes = await _viajesLogica.ListarViajesAsync(usuarioId, estado);
        return Ok(viajes);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> ObtenerPorId(int id, [FromQuery] int usuarioId)
    {
        if(usuarioId <= 0)
        {
            return BadRequest(new {mensaje = "El usuario no es valido."});
        }

        var viaje = await _viajesLogica.ObtenerPorIdAsync(usuarioId, id);

        if(viaje is null)
        {
            return NotFound(new {mensaje = "No se encontro el viaje"});
        }

        return Ok(viaje);
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

    [HttpPut("{id:int}/paradas/orden")]
    public async Task<IActionResult> ReordenarParadas(
        int id,
        [FromQuery] int usuarioId,
        [FromBody] ReordenarParadasRequest dto)
    {
        if (usuarioId <= 0 || id <= 0)
        {
            return BadRequest(new {mensaje = "El usuario o el viaje no son validos."});
        }

        var resultado = await _viajesLogica.ReordenarParadasAsync(
            usuarioId,
            id,
            dto.ParadasIds
        );

        if (!resultado.exito)
        {
            return BadRequest(new {mensaje = resultado.mensaje});
        }

        return Ok(new {mensaje = resultado.mensaje});
    }



    public class AgregarParadaRequest
    {
        public string Ubicacion { get; set; } = string.Empty;
        public int Orden { get; set; }
    }

    public class ReordenarParadasRequest
    {
        public List<int> ParadasIds { get; set; } = new();
    }
}