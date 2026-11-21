using System.Threading.Tasks;
using Optica.Api.Modules.Cotizaciones.DTOs;

namespace Optica.Api.Modules.Cotizaciones.Interfaces;

public interface ICotizacionService
{
    Task<CotizacionResponseDto> CrearCotizacionAsync(CreateCotizacionDto dto);
    Task<CotizacionResponseDto?> ObtenerCotizacionPorIdAsync(int id);
}
