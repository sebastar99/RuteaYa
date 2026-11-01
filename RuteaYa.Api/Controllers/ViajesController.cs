
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RuteaYa.Logica;
using RuteaYa.Logica.DTOs;

namespace RuteaYa.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/viajes")]
public class ViajesController : ControllerBase
{
    private readonly IViajesLogica _viajesLogica;

    public ViajesController(IViajesLogica viajesLogica)
    {
        _viajesLogica = viajesLogica;
    }

    private int ObtenerUsuarioId()
    {
        var identificador =
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return int.TryParse(identificador, out var id) && id > 0 ? id : 0;
    }

    [HttpGet]
    public async Task<IActionResult> ListarViajes ([FromQuery] string estado = "todos")
    {
        var usuarioId = ObtenerUsuarioId();

        if (usuarioId <= 0)
        {
            return Unauthorized(new { mensaje = "El usuario no es valido." });
        }

        estado = estado.Trim().ToLowerInvariant();

        if (estado is not ("todos" or "activo" or "inactivo"))
        {
            return BadRequest(new {mensaje = "El estado debe ser todos, activo o inactivo."});
        }

        var viajes = await _viajesLogica.ListarViajesAsync(usuarioId, estado);

        return Ok(viajes);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> ObtenerPorId(int id)
    {
        var usuarioId = ObtenerUsuarioId();

        if (usuarioId <= 0)
        {
            return Unauthorized();
        }
        if (id <= 0)
        {
            return BadRequest(new { mensaje = "ID de viaje invalido." });
        }

        var viaje = await _viajesLogica.ObtenerPorIdAsync(usuarioId, id);

        if (viaje is null)
        {
            return NotFound(new { mensaje = "No se encontrencontro el viaje." });
        }

        return Ok(viaje);
    }

    [HttpPost]
    public async Task<IActionResult> CrearViaje(
        [FromBody] CrearViajeDto dto)
    {
        var usuarioId = ObtenerUsuarioId();

        if (usuarioId <= 0)
        {
            return Unauthorized();
        }
           
        try {
            var resultado = await _viajesLogica.CrearViajeAsync(usuarioId, dto);

            if (!resultado.Exito)
            {
                return BadRequest(new { mensaje = resultado.mensaje });
            }

            if (resultado.viaje is null)
            {
                return BadRequest(new {mensaje = "No se pudo crear el viaje."});
            }

            return CreatedAtAction(
                nameof(ObtenerPorId), 
                new { id = resultado.viaje.Id }, resultado.viaje
            );
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> EditarViaje(int id, [FromBody] EditarViajeDto dto)
    {
        var usuarioId = ObtenerUsuarioId();

        if (usuarioId <= 0)
        {
            return Unauthorized();
        }
            
        try {
            var resultado = await _viajesLogica.EditarViajeAsync(usuarioId, id, dto);

            if (!resultado.exito)
            {
                return BadRequest(new { mensaje = resultado.mensaje });
            }
            if (resultado.viaje is null)
            {
                return NotFound(new { mensaje = "No se encontro el viaje." });
            }
                
            return Ok(resultado.viaje);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [HttpPatch("{id:int}/desactivar")]
    public async Task<IActionResult> DesactivarViaje(int id)
    {
        var usuarioId = ObtenerUsuarioId();

        if (usuarioId <= 0)
        {
            return Unauthorized();
        }
            
        var resultado = await _viajesLogica.DesactivarViajeAsync(usuarioId, id);

        if (!resultado.exito)
        {
            return NotFound(new { mensaje = resultado.mensaje });
        }
            
        return NoContent();
    }

    [HttpPost("{id:int}/paradas")]
    public async Task<IActionResult> AgregarParada(int id, [FromBody] AgregarParadaRequest dto)
    {
        var usuarioId = ObtenerUsuarioId();

        if (usuarioId <= 0)
        {
            return Unauthorized();

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
                
            return Ok(new {mensaje = resultado.mensaje, parada = resultado.parada});
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new {mensaje = ex.Message });
        }
    }

    [HttpDelete("{id:int}/paradas/{paradaId:int}")]
    public async Task<IActionResult> EliminarParada(int id, int paradaId)
    {
        var usuarioId = ObtenerUsuarioId();

        if (usuarioId <= 0)
        {
            return Unauthorized();
        }
           
        var resultado = await _viajesLogica.EliminarParadaAsync(usuarioId, id, paradaId);

        if (!resultado.exito)
        {
            return NotFound(new { mensaje = resultado.mensaje });
        }
            
        return NoContent();
    }

    [HttpPut("{id:int}/paradas/orden")]
    public async Task<IActionResult> ReordenarParadas(int id, [FromBody] ReordenarParadasRequest dto)
    {
        var usuarioId = ObtenerUsuarioId();

        if (usuarioId <= 0)
        {
            return Unauthorized();
        }
            
        if (dto.ParadasIds is null)
        {
            return BadRequest(new {mensaje = "La lista de paradas es obligatoria."});
        }
            
        var resultado = await _viajesLogica.ReordenarParadasAsync(
            usuarioId,
            id,
            dto.ParadasIds
        );

        if (!resultado.exito)
        {
            return BadRequest(new { mensaje = resultado.mensaje });
        }
            
        return Ok(new {mensaje = resultado.mensaje });
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
