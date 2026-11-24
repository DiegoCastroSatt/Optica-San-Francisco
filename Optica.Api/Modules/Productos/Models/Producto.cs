using System.ComponentModel.DataAnnotations.Schema;

namespace Optica.Api.Modules.Productos.Models;

public class Producto
{
    public int IdProducto { get; set; }
    public int? IdTipoMarco { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Marca { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public int Stock { get; set; }
    public int StockMinimo { get; set; }
    public string Estado { get; set; } = "Disponible";
    public string? RutaImagen { get; set; }

    public string? ImagenesJson { get; set; }

    [NotMapped]
    public string[] RutasImagenes => ImagenesJson is null
        ? (RutaImagen is null ? [] : [RutaImagen])
        : System.Text.Json.JsonSerializer.Deserialize<string[]>(ImagenesJson) ?? [];

    [NotMapped]
    public bool TieneVentas { get; set; }
}