using System;
using System.ComponentModel.DataAnnotations;

namespace Optica.Api.Modules.Cotizaciones.DTOs;

public class CreateCotizacionDto
{
    [Required(ErrorMessage = "El RUT es obligatorio.")]
    public string Rut { get; set; } = string.Empty;

    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    public DateTime? FechaEntregaEstimada { get; set; }

    [Range(1, double.MaxValue, ErrorMessage = "El total debe ser mayor a 0.")]
    public decimal Total { get; set; }

    public string? Productos { get; set; }

    public string? Servicios { get; set; }

    public string? Anotaciones { get; set; }
}
