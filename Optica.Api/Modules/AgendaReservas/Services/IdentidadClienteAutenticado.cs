using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Optica.Api.Modules.AgendaReservas.Interfaces;
using Optica.Api.Modules.Autenticacion.Controllers;
using Optica.Api.Modules.Clientes.DTOs;
using Optica.Api.Modules.Clientes.Interfaces;

namespace Optica.Api.Modules.AgendaReservas.Services;

public sealed class IdentidadClienteAutenticado(IClienteService clienteService) : IIdentidadClienteAutenticado
{
    public async Task<PerfilClienteDto?> ObtenerAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var resultado = await httpContext.AuthenticateAsync(AutenticacionClientesController.Esquema);
        if (!resultado.Succeeded)
        {
            return null;
        }

        if (!int.TryParse(resultado.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var idCliente))
        {
            return null;
        }

        return await clienteService.ObtenerPerfilAsync(idCliente);
    }
}
