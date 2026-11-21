using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Optica.Api.Data;
using Optica.Api.Modules.Cotizaciones.Interfaces;
using Optica.Api.Modules.Cotizaciones.Models;

namespace Optica.Api.Modules.Cotizaciones.Repositories;

public class CotizacionRepository : ICotizacionRepository
{
    private readonly OpticaDbContext _context;

    public CotizacionRepository(OpticaDbContext context)
    {
        _context = context;
    }

    public async Task<Cotizacion> CrearCotizacionAsync(Cotizacion cotizacion)
    {
        _context.Cotizaciones.Add(cotizacion);
        await _context.SaveChangesAsync();
        return cotizacion;
    }

    public async Task<Cotizacion?> ObtenerCotizacionPorIdAsync(int id)
    {
        return await _context.Cotizaciones.FindAsync(id);
    }
}
