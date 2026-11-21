using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Optica.Api.Modules.Accesos;
using Microsoft.AspNetCore.Mvc;
using Optica.Api.Modules.Autenticacion.Controllers;
using Optica.Api.Modules.Pedidos.DTOs;
using Optica.Api.Modules.Pedidos.Exceptions;
using Optica.Api.Modules.Pedidos.Interfaces;

namespace Optica.Api.Modules.Pedidos.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PedidosController : ControllerBase
{
    private readonly IPedidoService _pedidoService;

    public PedidosController(IPedidoService pedidoService)
    {
        _pedidoService = pedidoService;
    }

    [Authorize(Policy = AutenticacionClientesController.Politica)]
    [HttpGet("mis-pedidos")]
    public async Task<IActionResult> ObtenerMisPedidos()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idCliente))
            return Unauthorized();

        var pedidos = await _pedidoService.ObtenerPedidosPorClienteIdAsync(idCliente);
        return Ok(pedidos);
    }

    [Authorize(Policy = "pedidos")]
    [HttpGet]
    public async Task<IActionResult> GetPedidos()
    {
        var pedidos = await _pedidoService.ObtenerPedidosAsync();
        return Ok(pedidos);
    }

    [Authorize(Policy = "pedidos")]
    [HttpGet("pagina")]
    public async Task<IActionResult> GetPagina(
        [FromQuery] int pagina = 1, [FromQuery] int tamano = 20, [FromQuery] string? termino = null)
    {
        if (pagina < 1 || tamano is < 1 or > 100 || (pagina - 1L) * tamano > int.MaxValue)
            return BadRequest(new { mensaje = "La página debe ser positiva y el tamaño debe estar entre 1 y 100." });

        return Ok(await _pedidoService.ObtenerPaginaAsync(pagina, tamano, termino ?? string.Empty));
    }

    [Authorize(Policy = "pedidos")]
    [HttpGet("clientes/buscar")]
    public async Task<IActionResult> BuscarClientesPorRut([FromQuery] string rut)
    {
        var clientes = await _pedidoService.BuscarClientesPorRutAsync(rut);
        return Ok(clientes);
    }

    [Authorize(Policy = "pedidos")]
    [HttpGet("clientes")]
    public async Task<IActionResult> GetClientes()
    {
        var clientes = await _pedidoService.ObtenerClientesActivosAsync();
        return Ok(clientes);
    }

    [Authorize(Policy = "pedidos")]
    [HttpGet("cliente/{rut}")]
    public async Task<IActionResult> GetPedidosPorRut(string rut)
    {
        try
        {
            var pedidos = await _pedidoService.ObtenerPedidosPorRutAsync(rut);
            return Ok(pedidos);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
    }

    [Authorize(Policy = "pedidos")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPedidoById(int id)
    {
        var pedido = await _pedidoService.ObtenerPedidoPorIdAsync(id);
        if (pedido == null)
            return NotFound(new { mensaje = "Pedido no encontrado." });
        return Ok(pedido);
    }

    [Authorize(Policy = "pedidos")]
    [HttpPost]
    public async Task<IActionResult> CreatePedido([FromBody] CreatePedidoDto dto)
    {
        try
        {
            var result = await _pedidoService.CrearPedidoAsync(dto);
            return CreatedAtAction(nameof(GetPedidos), new { id = result.IdPedido }, result);
        }
        catch (CotizacionYaConvertidaException ex)
        {
            return StatusCode(StatusCodes.Status409Conflict, new { mensaje = ex.Message, idPedido = ex.IdPedidoExistente });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }

    [Authorize(Policy = "pedidos")]
    [HttpPatch("{id}/estado")]
    public async Task<IActionResult> UpdateEstado(int id, [FromBody] UpdateEstadoPedidoDto dto)
    {
        try
        {
            var result = await _pedidoService.ActualizarEstadoAsync(id, dto);
            var mensaje = result.EstadoNotificacion switch
            {
                "Enviada" => "El pedido se actualizó y el correo se envió correctamente.",
                "Pendiente" => "El pedido se actualizó; el correo quedó guardado para enviarse automáticamente.",
                "Fallida" => "El pedido se actualizó correctamente, pero no se pudo enviar el correo al cliente.",
                "YaEnviada" => "El pedido se actualizó; el correo de este pedido ya se había enviado.",
                _ => "Estado actualizado exitosamente."
            };
            return Ok(new { mensaje, estado = result.Estado, estadoNotificacion = result.EstadoNotificacion });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
    }
}
