using Microsoft.AspNetCore.Http;
using Optica.Api.Modules.Clientes.DTOs;

namespace Optica.Api.Modules.AgendaReservas.Interfaces;

/// Resuelve, si existe, al cliente con sesión iniciada que está haciendo la petición actual.
public interface IIdentidadClienteAutenticado
{
    Task<PerfilClienteDto?> ObtenerAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
}
