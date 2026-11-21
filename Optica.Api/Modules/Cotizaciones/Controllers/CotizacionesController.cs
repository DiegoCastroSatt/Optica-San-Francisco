using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Optica.Api.Modules.Cotizaciones.DTOs;
using Optica.Api.Modules.Cotizaciones.Interfaces;

namespace Optica.Api.Modules.Cotizaciones.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "pedidos")]
public class CotizacionesController : ControllerBase
{
    private readonly ICotizacionService _cotizacionService;

    public CotizacionesController(ICotizacionService cotizacionService)
    {
        _cotizacionService = cotizacionService;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var cotizacion = await _cotizacionService.ObtenerCotizacionPorIdAsync(id);
        if (cotizacion == null)
        {
            return NotFound(new { mensaje = "Cotización no encontrada." });
        }
        return Ok(cotizacion);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCotizacionDto dto)
    {
        try
        {
            var resultado = await _cotizacionService.CrearCotizacionAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = resultado.IdCotizacion }, resultado);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}
