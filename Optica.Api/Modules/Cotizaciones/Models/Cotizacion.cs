using System;

namespace Optica.Api.Modules.Cotizaciones.Models;

public class Cotizacion
{
    public int IdCotizacion { get; set; }
    public string Rut { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public DateTime? FechaEntregaEstimada { get; set; }
    public decimal Total { get; set; }
    public string? Productos { get; set; }
    public string? Servicios { get; set; }
    public string? Anotaciones { get; set; }
    public string Estado { get; set; } = "Pendiente";
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
}
