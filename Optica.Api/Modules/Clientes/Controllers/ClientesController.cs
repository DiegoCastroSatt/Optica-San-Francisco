using Microsoft.AspNetCore.Authorization;
using Optica.Api.Modules.Accesos;
using Microsoft.AspNetCore.Mvc;
using Optica.Api.Modules.Autenticacion.Interfaces;
using Optica.Api.Modules.Clientes.DTOs;
using Optica.Api.Modules.Clientes.Interfaces;
using Optica.Api.Modules.Clientes.Models;

namespace Optica.Api.Modules.Clientes.Controllers;

[Authorize(Policy = "clientes")]
[ApiController]
[Route("api/[controller]")]
public class ClientesController : ControllerBase
{
    private readonly IClienteService _clienteService;
    private readonly IInvitacionClienteService _invitacion;
    private readonly ILogger<ClientesController> _logger;

    public ClientesController(
        IClienteService clienteService,
        IInvitacionClienteService invitacion,
        ILogger<ClientesController> logger)
    {
        _clienteService = clienteService;
        _invitacion = invitacion;
        _logger = logger;
    }

    [HttpGet("buscar")]
    public async Task<ActionResult<IEnumerable<Cliente>>> Buscar([FromQuery] string? termino)
    {
        var lista = await _clienteService.BuscarAsync(termino);
        return Ok(lista);
    }

    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] CrearClienteDto dto, CancellationToken cancellationToken = default)
    {
        Cliente cliente;
        try
        {
            cliente = await _clienteService.RegistrarAsync(dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }

        // El cliente ya quedó registrado; un fallo en la invitación no debe deshacer el alta.
        var invitacion = ResultadoInvitacionCliente.SinCorreo;
        if (!string.IsNullOrWhiteSpace(cliente.Correo))
        {
            try
            {
                invitacion = await _invitacion.InvitarAsync(cliente.IdCliente, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError("Falló la invitación del cliente {ClienteId} ({TipoError}).", cliente.IdCliente, ex.GetType().Name);
                invitacion = ResultadoInvitacionCliente.ErrorEnvio;
            }
        }

        // Se conservan los campos del cliente para no romper a los consumidores actuales.
        return CreatedAtAction(nameof(Buscar), new { termino = cliente.Rut }, new
        {
            cliente.IdCliente,
            cliente.Rut,
            cliente.Nombre,
            cliente.Apellido,
            cliente.Telefono,
            cliente.Correo,
            cliente.Estado,
            cliente.FechaRegistro,
            invitacionEnviada = invitacion == ResultadoInvitacionCliente.Enviada,
            mensajeInvitacion = MensajeInvitacion(invitacion)
        });
    }

    // Envía (o reenvía) el correo para que el cliente cree su contraseña.
    [HttpPost("{id}/invitacion")]
    public async Task<IActionResult> Invitar(int id, CancellationToken cancellationToken = default)
    {
        ResultadoInvitacionCliente resultado;
        try
        {
            resultado = await _invitacion.InvitarAsync(id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError("Falló la invitación del cliente {ClienteId} ({TipoError}).", id, ex.GetType().Name);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { mensaje = "No se pudo enviar la invitación. Inténtalo nuevamente más tarde." });
        }

        var cuerpo = new { mensaje = MensajeInvitacion(resultado) };
        return resultado switch
        {
            ResultadoInvitacionCliente.Enviada => Ok(cuerpo),
            ResultadoInvitacionCliente.ClienteNoEncontrado => NotFound(cuerpo),
            ResultadoInvitacionCliente.CuentaExistente or ResultadoInvitacionCliente.CorreoEnUso => Conflict(cuerpo),
            ResultadoInvitacionCliente.Espera => StatusCode(StatusCodes.Status429TooManyRequests, cuerpo),
            ResultadoInvitacionCliente.ErrorEnvio => StatusCode(StatusCodes.Status503ServiceUnavailable, cuerpo),
            _ => BadRequest(cuerpo)
        };
    }

    private static string MensajeInvitacion(ResultadoInvitacionCliente resultado) => resultado switch
    {
        ResultadoInvitacionCliente.Enviada => "Se envió al cliente un correo para crear su contraseña. El enlace vence en 7 días.",
        ResultadoInvitacionCliente.ClienteNoEncontrado => "El cliente no fue encontrado en la base de datos.",
        ResultadoInvitacionCliente.ClienteInactivo => "El cliente está inactivo. Actívalo antes de enviar la invitación.",
        ResultadoInvitacionCliente.SinCorreo => "El cliente no tiene un correo válido, por lo que no se envió invitación para crear su cuenta.",
        ResultadoInvitacionCliente.CuentaExistente => "El cliente ya tiene una cuenta. Si olvidó su contraseña, puede recuperarla desde el inicio de sesión.",
        ResultadoInvitacionCliente.CorreoEnUso => "Ese correo ya está asociado a la cuenta de otro cliente.",
        ResultadoInvitacionCliente.Espera => "Espera un minuto antes de reenviar la invitación.",
        _ => "No se pudo enviar el correo de invitación. Puedes reenviarlo desde la lista de clientes."
    };

    [HttpPut("{id}")]
    public async Task<IActionResult> Actualizar(int id, [FromBody] ActualizarClienteDto dto)
    {
        try
        {
            var cliente = await _clienteService.ActualizarAsync(id, dto);
            return Ok(cliente);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensaje = "Error al actualizar los datos en la base de datos.", detalle = ex.Message });
        }
    }

    [HttpPatch("{id}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoDto dto)
    {
        try
        {
            var cliente = await _clienteService.CambiarEstadoAsync(id, dto.NuevoEstado);
            return Ok(new 
            { 
                mensaje = $"El cliente fue marcado como {dto.NuevoEstado.ToLower()} exitosamente.",
                idCliente = cliente.IdCliente,
                nuevoEstado = cliente.Estado
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { mensaje = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { mensaje = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { mensaje = "Error al actualizar el estado en la base de datos.", detalle = ex.Message });
        }
    }

    [HttpGet("{id}/historial")]
public async Task<ActionResult<HistorialClienteDto>> ObtenerHistorial(int id)
{
    try
    {
        var historial = await _clienteService.ObtenerHistorialAsync(id);
        return Ok(historial);
    }
    catch (KeyNotFoundException ex)
    {
        return NotFound(new { mensaje = ex.Message });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { mensaje = "Error al consultar el historial del cliente.", detalle = ex.Message });
    }
}
}