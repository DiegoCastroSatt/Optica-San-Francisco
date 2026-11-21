using System.Threading.Tasks;
using Optica.Api.Modules.Cotizaciones.Models;

namespace Optica.Api.Modules.Cotizaciones.Interfaces;

public interface ICotizacionRepository
{
    Task<Cotizacion> CrearCotizacionAsync(Cotizacion cotizacion);
    Task<Cotizacion?> ObtenerCotizacionPorIdAsync(int id);
}
