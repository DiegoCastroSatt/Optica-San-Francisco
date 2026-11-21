using System;
using System.Threading.Tasks;
using Optica.Api.Modules.Clientes.Interfaces;
using Optica.Api.Modules.Clientes.Services;
using Optica.Api.Modules.Cotizaciones.DTOs;
using Optica.Api.Modules.Cotizaciones.Interfaces;
using Optica.Api.Modules.Cotizaciones.Models;

namespace Optica.Api.Modules.Cotizaciones.Services;

public class CotizacionService : ICotizacionService
{
    private readonly ICotizacionRepository _cotizacionRepository;
    private readonly IClienteRepository _clienteRepository;

    public CotizacionService(ICotizacionRepository cotizacionRepository, IClienteRepository clienteRepository)
    {
        _cotizacionRepository = cotizacionRepository;
        _clienteRepository = clienteRepository;
    }

    public async Task<CotizacionResponseDto> CrearCotizacionAsync(CreateCotizacionDto dto)
    {
        if (!RutChilenoValidator.EsValido(dto.Rut))
        {
            throw new ArgumentException("El RUT o su dígito verificador no es válido.");
        }

        var rutNormalizado = RutChilenoValidator.Normalizar(dto.Rut);
        var cliente = await _clienteRepository.ObtenerPorRutAsync(rutNormalizado);
        if (cliente == null || cliente.Estado != "Activo")
        {
            throw new ArgumentException("No se encontró un cliente activo con ese RUT.");
        }

        if (dto.Total <= 0)
        {
            throw new ArgumentException("El total de la cotización debe ser mayor a 0.");
        }

        var cotizacion = new Cotizacion
        {
            Rut = cliente.Rut,
            Fecha = dto.Fecha == default ? DateTime.UtcNow : dto.Fecha,
            FechaEntregaEstimada = dto.FechaEntregaEstimada,
            Total = dto.Total,
            Productos = dto.Productos?.Trim(),
            Servicios = dto.Servicios?.Trim(),
            Anotaciones = dto.Anotaciones?.Trim(),
            Estado = "Pendiente",
            FechaCreacion = DateTime.UtcNow
        };

        var nuevaCotizacion = await _cotizacionRepository.CrearCotizacionAsync(cotizacion);

        return new CotizacionResponseDto
        {
            IdCotizacion = nuevaCotizacion.IdCotizacion,
            Rut = nuevaCotizacion.Rut,
            NombreCliente = $"{cliente.Nombre} {cliente.Apellido}".Trim(),
            Fecha = nuevaCotizacion.Fecha,
            FechaEntregaEstimada = nuevaCotizacion.FechaEntregaEstimada,
            Total = nuevaCotizacion.Total,
            Productos = nuevaCotizacion.Productos,
            Servicios = nuevaCotizacion.Servicios,
            Anotaciones = nuevaCotizacion.Anotaciones,
            Estado = nuevaCotizacion.Estado,
            FechaCreacion = nuevaCotizacion.FechaCreacion
        };
    }

    public async Task<CotizacionResponseDto?> ObtenerCotizacionPorIdAsync(int id)
    {
        var cotizacion = await _cotizacionRepository.ObtenerCotizacionPorIdAsync(id);
        if (cotizacion == null) return null;

        var cliente = await _clienteRepository.ObtenerPorRutAsync(cotizacion.Rut);

        return new CotizacionResponseDto
        {
            IdCotizacion = cotizacion.IdCotizacion,
            Rut = cotizacion.Rut,
            NombreCliente = cliente != null ? $"{cliente.Nombre} {cliente.Apellido}".Trim() : string.Empty,
            Fecha = cotizacion.Fecha,
            FechaEntregaEstimada = cotizacion.FechaEntregaEstimada,
            Total = cotizacion.Total,
            Productos = cotizacion.Productos,
            Servicios = cotizacion.Servicios,
            Anotaciones = cotizacion.Anotaciones,
            Estado = cotizacion.Estado,
            FechaCreacion = cotizacion.FechaCreacion
        };
    }
}
