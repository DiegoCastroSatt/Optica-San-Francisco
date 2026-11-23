using Optica.Api.Modules.Marcos;
using Microsoft.EntityFrameworkCore;
using Optica.Api.Modules.Productos.DTOs;
using Optica.Api.Modules.Productos.Models;

public class ProductoService : IProductoService
{
    public async Task<(List<ProductoResponseDto> Productos, int Total)> BuscarPagina(string termino, string estado, string categoria,
        string color, string marca, string ordenPrecio, int desplazamiento, int limite, int? marco = null)
    {
        var (productos, total) = await _productoRepository.BuscarPagina(
            termino, estado, categoria, color, marca, ordenPrecio, desplazamiento, limite, marco);
        return (productos.Select(Mapear).ToList(), total);
    }

    public Task<(string[] Categorias, string[] Colores, string[] Marcas)> OpcionesBusqueda(string termino) =>
        _productoRepository.OpcionesBusqueda(termino);

    private static readonly string[] EstadosValidos = ["Disponible", "Agotado"];
    private static readonly string[] ExtensionesImagenValidas = [".jpg", ".jpeg", ".png", ".webp"];
    private readonly IProductoRepository _productoRepository;
    private readonly IWebHostEnvironment _environment;

    public ProductoService(IProductoRepository productoRepository, IWebHostEnvironment environment)
    {
        _productoRepository = productoRepository;
        _environment = environment;
    }

    public async Task<ProductoResponseDto> CrearProducto(CrearProductoDto dto, int idResponsable)
    {
        if (idResponsable <= 0)
        {
            throw new ArgumentException("No se pudo identificar al responsable del registro.");
        }

        var codigo = dto.Codigo.Trim();
        var nombre = dto.Nombre.Trim();
        var estado = dto.Estado.Trim();

        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(dto.Categoria))
        {
            throw new ArgumentException("El código, nombre y categoría son obligatorios.");
        }

        if (codigo.Length > 30 || nombre.Length > 100 || dto.Marca.Trim().Length > 60 || dto.Modelo.Trim().Length > 60 || dto.Color.Trim().Length > 40 || dto.Categoria.Trim().Length > 50)
        {
            throw new ArgumentException("Uno o más campos superan la longitud permitida.");
        }

        if (dto.Precio < 0 || dto.Stock < 0 || dto.StockMinimo < 0)
        {
            throw new ArgumentException("El precio y las cantidades no pueden ser negativos.");
        }

        if (dto.Precio > 99999999.99m)
        {
            throw new ArgumentException("El precio supera el máximo permitido.");
        }

        if (!EstadosValidos.Contains(estado, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("El estado debe ser Disponible o Agotado.");
        }

        if (await _productoRepository.ExisteCodigo(codigo))
        {
            throw new InvalidOperationException("Ya existe un producto con ese código.");
        }

        var nuevas = await GuardarImagenes(dto.Imagen, dto.Imagenes);
        try
        {
            var rutas = OrdenarImagenes(nuevas, dto.IndicePrincipal);

            var producto = await _productoRepository.CrearConHistorialStock(new Producto
            {
                Codigo = codigo,
                Nombre = nombre,
                Marca = dto.Marca.Trim(),
                Modelo = dto.Modelo.Trim(),
                Color = dto.Color.Trim(),
                Categoria = dto.Categoria.Trim(),
                Precio = dto.Precio,
                Stock = dto.Stock,
                StockMinimo = dto.StockMinimo,
                Estado = EstadosValidos.First(estadoValido => string.Equals(estadoValido, estado, StringComparison.OrdinalIgnoreCase)),
                RutaImagen = rutas.FirstOrDefault(),
                ImagenesJson = System.Text.Json.JsonSerializer.Serialize(rutas)
            }, idResponsable);

            return Mapear(producto);
        }
        catch
        {
            foreach (var ruta in nuevas) EliminarImagen(ruta);
            throw;
        }
    }

    public async Task<List<ProductoResponseDto>> BuscarProductos(string termino) =>
        (await _productoRepository.Buscar(termino)).Select(Mapear).ToList();

    public async Task<ReporteInventarioResponseDto> ConsultarReporteInventario(CancellationToken cancellationToken)
    {
        var productos = await _productoRepository.ObtenerParaReporteInventario(cancellationToken);
        var agotados = productos
            .Where(producto => producto.Stock <= 0)
            .Select(MapearProductoReporte)
            .ToList();
        var stockBajo = productos
            .Where(producto => producto.Stock > 0 && producto.Stock <= producto.StockMinimo)
            .Select(MapearProductoReporte)
            .ToList();
        var disponibles = productos
            .Where(producto => producto.Stock > producto.StockMinimo)
            .Select(MapearProductoReporte)
            .ToList();

        return new ReporteInventarioResponseDto(
            DateTimeOffset.UtcNow,
            productos.Count,
            disponibles.Count,
            agotados.Count,
            stockBajo.Count,
            disponibles,
            agotados,
            stockBajo);
    }

    public async Task<ProductoResponseDto?> ObtenerProducto(int id)
    {
        var producto = await _productoRepository.ObtenerPorId(id);
        return producto is null ? null : Mapear(producto);
    }

    public async Task<ProductoResponseDto> ActualizarProducto(int id, ActualizarProductoDto dto, int idResponsable)
    {
        ValidarResponsable(idResponsable);
        var producto = await _productoRepository.ObtenerPorId(id) ?? throw new KeyNotFoundException("No se encuentra el producto.");
        var codigo = dto.Codigo.Trim();
        var nombre = dto.Nombre.Trim();
        var categoria = dto.Categoria.Trim();
        if (string.IsNullOrWhiteSpace(codigo) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(categoria))
        {
            throw new ArgumentException("El código, nombre y categoría son obligatorios.");
        }

        if (dto.Precio < 0 || dto.Stock < 0 || dto.StockMinimo < 0 || !EstadosValidos.Contains(dto.Estado.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Los datos del producto no son válidos.");
        }

        var cambiaStock = producto.Stock != dto.Stock || producto.StockMinimo != dto.StockMinimo;
        if (cambiaStock)
        {
            ValidarMotivo(dto.MotivoCambio);
        }

        producto.Codigo = codigo;
        producto.Nombre = nombre;
        producto.Marca = dto.Marca.Trim();
        producto.Modelo = dto.Modelo.Trim();
        producto.Color = dto.Color.Trim();
        producto.Categoria = categoria;
        producto.Precio = dto.Precio;
        producto.Stock = dto.Stock;
        producto.StockMinimo = dto.StockMinimo;
        producto.Estado = EstadosValidos.First(estado => string.Equals(estado, dto.Estado.Trim(), StringComparison.OrdinalIgnoreCase));
        var anteriores = producto.RutasImagenes;
        // La ruta heredada se reemplaza solo si el consumidor antiguo envía Imagen.
        List<string>? seleccion = dto.RutasConservadas;
        if (dto.RutasConservadasJson is not null)
        {
            try { seleccion = System.Text.Json.JsonSerializer.Deserialize<List<string>>(dto.RutasConservadasJson); }
            catch (System.Text.Json.JsonException) { throw new ArgumentException("La selección de imágenes no es válida."); }
            if (seleccion is null) throw new ArgumentException("La selección de imágenes no es válida.");
        }
        var conservadas = seleccion ?? (dto.Imagen is { Length: > 0 } ? [] : anteriores.ToList());
        if (conservadas.Distinct().Count() != conservadas.Count || conservadas.Any(ruta => !anteriores.Contains(ruta)))
            throw new ArgumentException("Las imágenes conservadas deben pertenecer al producto.");
        if (conservadas.Count + dto.Imagenes.Count + (dto.Imagen is { Length: > 0 } ? 1 : 0) > 5)
            throw new ArgumentException("Puedes guardar hasta 5 imágenes por producto.");

        var nuevas = await GuardarImagenes(dto.Imagen, dto.Imagenes);
        Producto actualizado;
        try
        {
            var rutas = OrdenarImagenes(conservadas.Concat(nuevas).ToList(), dto.IndicePrincipal);
            producto.RutaImagen = rutas.FirstOrDefault();
            producto.ImagenesJson = System.Text.Json.JsonSerializer.Serialize(rutas);
            actualizado = await _productoRepository.ActualizarConHistorialStock(
                producto, idResponsable, dto.MotivoCambio?.Trim() ?? string.Empty);
        }
        catch
        {
            foreach (var ruta in nuevas) EliminarImagen(ruta);
            throw;
        }
        foreach (var ruta in anteriores.Except(actualizado.RutasImagenes)) EliminarImagen(ruta);
        return Mapear(actualizado);
    }

    public async Task<ProductoResponseDto> ActualizarStock(int id, ActualizarStockDto dto, int idResponsable)
    {
        ValidarResponsableYMotivo(idResponsable, dto.MotivoCambio);
        if (dto.Stock < 0 || dto.StockMinimo < 0)
        {
            throw new ArgumentException("Las cantidades de stock no pueden ser negativas.");
        }

        var producto = await _productoRepository.ObtenerPorId(id)
            ?? throw new KeyNotFoundException("No se encuentra el producto.");

        producto.Stock = dto.Stock;
        producto.StockMinimo = dto.StockMinimo;
        producto.Estado = dto.Stock > 0 ? "Disponible" : "Agotado";
        return Mapear(await _productoRepository.ActualizarConHistorialStock(
            producto,
            idResponsable,
            dto.MotivoCambio.Trim()));
    }

    public async Task<HistorialStockPaginadoResponseDto?> ConsultarHistorialStock(
        int idProducto,
        int pagina,
        int tamanoPagina)
    {
        ValidarPaginacionHistorial(pagina, tamanoPagina);
        if (idProducto <= 0)
            throw new ArgumentException("El identificador del producto no es válido.");

        return await _productoRepository.ConsultarHistorialStock(idProducto, pagina, tamanoPagina);
    }

    public async Task<HistorialStockPaginadoResponseDto> ConsultarHistorialStockReciente(
        int pagina,
        int tamanoPagina)
    {
        ValidarPaginacionHistorial(pagina, tamanoPagina);
        return await _productoRepository.ConsultarHistorialStockReciente(pagina, tamanoPagina);
    }

    private static void ValidarPaginacionHistorial(int pagina, int tamanoPagina)
    {
        if (pagina <= 0 || tamanoPagina <= 0 || tamanoPagina > 100 ||
            (long)(pagina - 1) * tamanoPagina > int.MaxValue)
        {
            throw new ArgumentException("La paginación solicitada no es válida.");
        }
    }

    public async Task EliminarProducto(int id)
    {
        var producto = await _productoRepository.ObtenerPorId(id)
            ?? throw new KeyNotFoundException("No se encuentra el producto.");

        try
        {
            if (!await _productoRepository.Eliminar(id))
            {
                throw new KeyNotFoundException("No se encuentra el producto.");
            }
        }
        catch (DbUpdateException ex) when (
            ex.Message.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("foreign key", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("ventas", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("detalle_venta", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("asociado", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("No se puede eliminar el producto porque está asociado a ventas o detalles de venta.", ex);
        }

        foreach (var ruta in producto.RutasImagenes) EliminarImagen(ruta);
    }

    private static ProductoResponseDto Mapear(Producto producto) => new()
    {
        Id = producto.IdProducto,
        IdTipoMarco = producto.IdTipoMarco,
        Codigo = producto.Codigo,
        Nombre = producto.Nombre,
        Marca = producto.Marca,
        Modelo = producto.Modelo,
        Color = producto.Color,
        Categoria = producto.Categoria,
        Precio = producto.Precio,
        Stock = producto.Stock,
        StockMinimo = producto.StockMinimo,
        Estado = producto.Estado,
        RutaImagen = producto.RutaImagen,
        RutasImagenes = producto.RutasImagenes,
        TieneVentas = producto.TieneVentas
    };

    private static ProductoReporteInventarioDto MapearProductoReporte(Producto producto) =>
        new(
            producto.IdProducto,
            producto.Codigo,
            producto.Nombre,
            producto.Categoria,
            producto.Stock,
            producto.StockMinimo);

    private static void ValidarResponsableYMotivo(int idResponsable, string? motivo)
    {
        ValidarResponsable(idResponsable);
        ValidarMotivo(motivo);
    }

    private static void ValidarResponsable(int idResponsable)
    {
        if (idResponsable <= 0)
            throw new ArgumentException("No se pudo identificar al responsable del cambio.");
    }

    private static void ValidarMotivo(string? motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > 255)
            throw new ArgumentException("El motivo es obligatorio y no puede superar los 255 caracteres.");
    }

    private static List<string> OrdenarImagenes(List<string> rutas, int principal)
    {
        if (principal < 0 || (rutas.Count > 0 && principal >= rutas.Count) || (rutas.Count == 0 && principal != 0))
            throw new ArgumentException("Selecciona una imagen principal válida.");
        if (rutas.Count > 0)
        {
            var ruta = rutas[principal];
            rutas.RemoveAt(principal);
            rutas.Insert(0, ruta);
        }
        return rutas;
    }

    private async Task<List<string>> GuardarImagenes(IFormFile? heredada, List<IFormFile> imagenes)
    {
        var archivos = imagenes.ToList();
        if (heredada is { Length: > 0 }) archivos.Insert(0, heredada);
        if (archivos.Count > 5) throw new ArgumentException("Puedes guardar hasta 5 imágenes por producto.");
        if (archivos.Any(archivo => archivo.Length == 0))
            throw new ArgumentException("La imagen está vacía.");
        var rutas = new List<string>();
        try
        {
            foreach (var archivo in archivos) rutas.Add((await GuardarImagen(archivo))!);
            return rutas;
        }
        catch
        {
            foreach (var ruta in rutas) EliminarImagen(ruta);
            throw;
        }
    }

    private async Task<string?> GuardarImagen(IFormFile? imagen)
    {
        if (imagen is null || imagen.Length == 0)
        {
            return null;
        }

        if (imagen.Length > 5 * 1024 * 1024)
        {
            throw new ArgumentException("La imagen no puede superar los 5 MB.");
        }

        var extension = Path.GetExtension(imagen.FileName).ToLowerInvariant();
        if (!ExtensionesImagenValidas.Contains(extension) || !imagen.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("La imagen debe ser JPG, PNG o WebP.");
        }

        var directorio = Path.Combine(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), "uploads", "productos");
        Directory.CreateDirectory(directorio);

        var nombreArchivo = $"{Guid.NewGuid():N}{extension}";
        var rutaFisica = Path.Combine(directorio, nombreArchivo);

        try
        {
            await using var stream = File.Create(rutaFisica);
            await imagen.CopyToAsync(stream);
        }
        catch
        {
            EliminarImagen($"/uploads/productos/{nombreArchivo}");
            throw;
        }
        return $"/uploads/productos/{nombreArchivo}";
    }

    private void EliminarImagen(string? rutaImagen)
    {
        if (string.IsNullOrWhiteSpace(rutaImagen))
        {
            return;
        }

        var nombreArchivo = Path.GetFileName(rutaImagen);
        var directorio = Path.Combine(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), "uploads", "productos");
        var rutaFisica = Path.Combine(directorio, nombreArchivo);
        if (File.Exists(rutaFisica))
        {
            try { File.Delete(rutaFisica); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}