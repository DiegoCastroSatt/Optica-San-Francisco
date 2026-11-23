using System;
using System.ComponentModel.DataAnnotations;

namespace Optica.Api.Modules.Pedidos.DTOs;

public class CreatePedidoDto
{
    public string Rut { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public string Estado { get; set; } = "Pendiente";
    public decimal Total { get; set; }
    public string? Anotaciones { get; set; }
    public DateTime? FechaEntregaEstimada { get; set; }
    [MaxLength(36, ErrorMessage = "El token de cotización no puede tener más de 36 caracteres.")]
    public string? TokenCotizacion { get; set; }
}
