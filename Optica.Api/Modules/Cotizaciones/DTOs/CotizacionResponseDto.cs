using System;

namespace Optica.Api.Modules.Cotizaciones.DTOs;

public class CotizacionResponseDto
{
    public int IdCotizacion { get; set; }
    public string Rut { get; set; } = string.Empty;
    public string NombreCliente { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime? FechaEntregaEstimada { get; set; }
    public decimal Total { get; set; }
    public string? Productos { get; set; }
    public string? Servicios { get; set; }
    public string? Anotaciones { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
}
