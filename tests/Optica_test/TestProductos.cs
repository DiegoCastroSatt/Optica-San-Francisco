using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Optica.Api.Modules.Productos.DTOs;
using Optica.Api.Modules.Productos.Models;
using Xunit;

namespace Optica.Ventas.Tests;

public class TestProductos
{
    [Fact]
    public async Task CrearProducto_RechazaCamposObligatoriosAusentes()
    {
        var repositorio = new ProductoRepositorioPrueba();
        var servicio = CrearServicio(repositorio);

        await Assert.ThrowsAsync<ArgumentException>(() => servicio.CrearProducto(new CrearProductoDto
        {
            Codigo = "",
            Nombre = "Armazón",
            Categoria = "Armazones"
        }, 42));

        Assert.Null(repositorio.UltimoProducto);
    }

    [Fact]
    public async Task CrearProducto_RechazaCantidadesNegativas()
    {
        var servicio = CrearServicio(new ProductoRepositorioPrueba());

        await Assert.ThrowsAsync<ArgumentException>(() => servicio.CrearProducto(new CrearProductoDto
        {
            Codigo = "OPT-001",
            Nombre = "Armazón",
            Categoria = "Armazones",
            Precio = 100,
            Stock = -1,
            StockMinimo = 0
        }, 42));
    }

    [Fact]
    public async Task CrearProducto_RechazaCodigoDuplicado()
    {
        var repositorio = new ProductoRepositorioPrueba { CodigoExiste = true };
        var servicio = CrearServicio(repositorio);

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.CrearProducto(ProductoValido(), 42));
        Assert.Null(repositorio.UltimoProducto);
    }

    [Fact]
    public async Task CrearProducto_NormalizaDatosYDevuelveRespuesta()
    {
        var repositorio = new ProductoRepositorioPrueba();
        var servicio = CrearServicio(repositorio);

        var resultado = await servicio.CrearProducto(new CrearProductoDto
        {
            Codigo = "  OPT-001  ",
            Nombre = "  Armazón clásico  ",
            Marca = " Ray-Ban ",
            Modelo = " RB2140 ",
            Color = " Negro ",
            Categoria = " Armazones ",
            Precio = 45000,
            Stock = 5,
            StockMinimo = 1,
            Estado = "agotado"
        }, 42);

        Assert.Equal(1, resultado.Id);
        Assert.Equal("OPT-001", resultado.Codigo);
        Assert.Equal("Armazón clásico", resultado.Nombre);
        Assert.Equal("Agotado", resultado.Estado);
        Assert.Equal("OPT-001", repositorio.UltimoProducto!.Codigo);
        Assert.Equal("Ray-Ban", repositorio.UltimoProducto.Marca);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(0, 0)]
    public async Task CrearProducto_RegistraStockInicialNormalYMinimo(int stock, int stockMinimo)
    {
        var repositorio = new ProductoRepositorioPrueba();
        var dto = ProductoValido();
        dto.Stock = stock;
        dto.StockMinimo = stockMinimo;

        await CrearServicio(repositorio).CrearProducto(dto, 42);

        Assert.Collection(repositorio.HistorialStock,
            cambio =>
            {
                Assert.Equal(42, cambio.IdResponsable);
                Assert.Equal("Registro de nuevo producto", cambio.MotivoMovimiento);
                Assert.Equal("Normal", cambio.TipoStock);
                Assert.Equal(0, cambio.ValorAnterior);
                Assert.Equal(stock, cambio.ValorActual);
            },
            cambio =>
            {
                Assert.Equal(42, cambio.IdResponsable);
                Assert.Equal("Registro de nuevo producto", cambio.MotivoMovimiento);
                Assert.Equal("Minimo", cambio.TipoStock);
                Assert.Equal(0, cambio.ValorAnterior);
                Assert.Equal(stockMinimo, cambio.ValorActual);
            });
    }

    [Fact]
    public async Task ConsultarProductosStockBajo_DevuelveProductosEnElMinimoOAgotados()
    {
        var repositorio = new ProductoRepositorioPrueba();
        repositorio.Productos.AddRange(
            CrearProductoInventario(1, "OPT-001", "En el mínimo", 3, 3),
            CrearProductoInventario(2, "OPT-002", "Agotado", 0, 5),
            CrearProductoInventario(3, "OPT-003", "Stock suficiente", 4, 3));
        using var cancellationSource = new CancellationTokenSource();

        var alertas = await CrearServicio(repositorio).ConsultarProductosStockBajo(cancellationSource.Token);

        Assert.Equal([1, 2], alertas.Select(alerta => alerta.IdProducto));
        Assert.Equal(cancellationSource.Token, repositorio.TokenReporteInventario);
    }

    [Fact]
    public async Task EliminarProducto_PermiteBorrarAunqueTengaVentas()
    {
        var repositorio = new ProductoRepositorioPrueba
        {
            ProductoActual = new Producto
            {
                IdProducto = 7,
                Nombre = "Armazón",
                Codigo = "OPT-007",
                Categoria = "Armazones",
                TieneVentas = true
            }
        };
        var servicio = CrearServicio(repositorio);

        await servicio.EliminarProducto(7);

        Assert.True(repositorio.ProductoEliminado);
    }

    [Fact]
    public async Task ActualizarStock_CambiaSoloLasCantidadesDeInventario()
    {
        var producto = new Producto
        {
            IdProducto = 7,
            Codigo = "OPT-007",
            Nombre = "Armazón",
            Categoria = "Armazones",
            Precio = 12000,
            Stock = 3,
            StockMinimo = 1
        };
        var repositorio = new ProductoRepositorioPrueba { ProductoActual = producto };
        var servicio = CrearServicio(repositorio);

        var resultado = await servicio.ActualizarStock(7,
            new ActualizarStockDto { Stock = 8, StockMinimo = 2, MotivoCambio = "Ajuste manual" }, 42);

        Assert.Equal(8, resultado.Stock);
        Assert.Equal(2, resultado.StockMinimo);
        Assert.Equal("OPT-007", resultado.Codigo);
        Assert.Equal(12000, resultado.Precio);
    }

    [Fact]
    public async Task ActualizarStock_RechazaCantidadesNegativas()
    {
        var servicio = CrearServicio(new ProductoRepositorioPrueba());

        await Assert.ThrowsAsync<ArgumentException>(() => servicio.ActualizarStock(7, new ActualizarStockDto
        {
            Stock = -1,
            StockMinimo = 0,
            MotivoCambio = "Ajuste manual"
        }, 42));
    }

    [Theory]
    [InlineData(0, 0, "Agotado")]
    [InlineData(0, 5, "Agotado")]
    [InlineData(1, 5, "Disponible")]
    [InlineData(20, 5, "Disponible")]
    public async Task ActualizarStock_PersisteCantidadesYEstado(int stock, int minimo, string estado)
    {
        var repositorio = new ProductoRepositorioPrueba { ProductoActual = new Producto
        {
            IdProducto = 7, Codigo = "OPT-007", Nombre = "Armazón", Categoria = "Armazones",
            Precio = 12000, Stock = 4, StockMinimo = 1, Estado = "Agotado", RutaImagen = "/imagen.png"
        }};
        var resultado = await CrearServicio(repositorio).ActualizarStock(7,
            new ActualizarStockDto { Stock = stock, StockMinimo = minimo, MotivoCambio = "Ajuste manual" }, 42);
        Assert.NotNull(repositorio.UltimoProducto);
        Assert.Equal(stock, repositorio.UltimoProducto.Stock);
        Assert.Equal(minimo, repositorio.UltimoProducto.StockMinimo);
        Assert.Equal(estado, resultado.Estado);
        Assert.Equal(estado, repositorio.UltimoProducto.Estado);
        Assert.Equal(12000m, repositorio.UltimoProducto.Precio);
        Assert.Equal("/imagen.png", repositorio.UltimoProducto.RutaImagen);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(-1, -1)]
    public async Task ActualizarStock_InvalidoNoModificaNiGuarda(int stock, int minimo)
    {
        var producto = new Producto { Stock = 5, StockMinimo = 2, Estado = "Disponible" };
        var repositorio = new ProductoRepositorioPrueba { ProductoActual = producto };
        await Assert.ThrowsAsync<ArgumentException>(() => CrearServicio(repositorio).ActualizarStock(7,
            new ActualizarStockDto { Stock = stock, StockMinimo = minimo, MotivoCambio = "Ajuste manual" }, 42));
        Assert.Null(repositorio.UltimoProducto);
        Assert.Equal(5, producto.Stock);
        Assert.Equal(2, producto.StockMinimo);
        Assert.Equal("Disponible", producto.Estado);
    }

    [Fact]
    public async Task ActualizarStock_ProductoInexistenteNoGuarda()
    {
        var repositorio = new ProductoRepositorioPrueba();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CrearServicio(repositorio).ActualizarStock(999,
            new ActualizarStockDto { Stock = 3, StockMinimo = 1, MotivoCambio = "Ajuste manual" }, 42));
        Assert.Null(repositorio.UltimoProducto);
    }

    [Fact]
    public async Task ActualizarStock_GuardaCambiosYMovimientosEnLaMismaOperacion()
    {
        var repositorio = new ProductoRepositorioPrueba
        {
            ProductoActual = new Producto
            {
                IdProducto = 7,
                Codigo = "OPT-007",
                Nombre = "Armazón",
                Categoria = "Armazones",
                Stock = 3,
                StockMinimo = 1
            }
        };

        await CrearServicio(repositorio).ActualizarStock(7,
            new ActualizarStockDto { Stock = 8, StockMinimo = 2, MotivoCambio = "Ajuste manual" }, 42);

        Assert.Equal(8, repositorio.StockPersistido);
        Assert.Equal(2, repositorio.StockMinimoPersistido);
        Assert.Collection(repositorio.HistorialStock,
            cambio =>
            {
                Assert.Equal(7, cambio.IdProducto);
                Assert.Equal(42, cambio.IdResponsable);
                Assert.Equal("Ajuste manual", cambio.MotivoMovimiento);
                Assert.Equal("Normal", cambio.TipoStock);
                Assert.Equal(3, cambio.ValorAnterior);
                Assert.Equal(8, cambio.ValorActual);
            },
            cambio =>
            {
                Assert.Equal(7, cambio.IdProducto);
                Assert.Equal(42, cambio.IdResponsable);
                Assert.Equal("Ajuste manual", cambio.MotivoMovimiento);
                Assert.Equal("Minimo", cambio.TipoStock);
                Assert.Equal(1, cambio.ValorAnterior);
                Assert.Equal(2, cambio.ValorActual);
            });
    }

    [Fact]
    public async Task ActualizarStock_SiFallaLaOperacionNoPersisteCambiosNiMovimientos()
    {
        var repositorio = new ProductoRepositorioPrueba
        {
            ProductoActual = new Producto
            {
                IdProducto = 7,
                Codigo = "OPT-007",
                Nombre = "Armazón",
                Categoria = "Armazones",
                Stock = 3,
                StockMinimo = 1
            },
            FallarAlActualizarStock = true
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => CrearServicio(repositorio).ActualizarStock(7,
            new ActualizarStockDto { Stock = 8, StockMinimo = 2, MotivoCambio = "Ajuste manual" }, 42));

        Assert.Equal(3, repositorio.StockPersistido);
        Assert.Equal(1, repositorio.StockMinimoPersistido);
        Assert.Empty(repositorio.HistorialStock);
        Assert.Null(repositorio.UltimoProducto);
    }

    [Fact]
    public async Task ConsultarReporteInventario_ClasificaProductosYCalculaTotales()
    {
        var repositorio = new ProductoRepositorioPrueba();
        repositorio.Productos.AddRange(
            CrearProductoInventario(1, "DIS-001", "Disponible", 5, 2),
            CrearProductoInventario(2, "BAJ-002", "Stock igual al mínimo", 2, 2),
            CrearProductoInventario(3, "BAJ-003", "Stock bajo", 1, 2),
            CrearProductoInventario(4, "AGO-004", "Agotado con mínimo cero", 0, 0),
            CrearProductoInventario(5, "AGO-005", "Stock negativo", -1, 2));
        using var origenCancelacion = new CancellationTokenSource();
        var inicio = DateTimeOffset.UtcNow;

        var resultado = await CrearServicio(repositorio)
            .ConsultarReporteInventario(origenCancelacion.Token);
        var fin = DateTimeOffset.UtcNow;

        Assert.Equal(origenCancelacion.Token, repositorio.TokenReporteInventario);
        Assert.InRange(resultado.GeneradoEn, inicio, fin);
        Assert.Equal(TimeSpan.Zero, resultado.GeneradoEn.Offset);
        Assert.Equal(5, resultado.TotalProductos);
        Assert.Equal(1, resultado.TotalDisponibles);
        Assert.Equal(2, resultado.TotalStockBajo);
        Assert.Equal(2, resultado.TotalAgotados);
        Assert.Equal(new ProductoReporteInventarioDto(1, "DIS-001", "Disponible", "Armazones", 5, 2),
            Assert.Single(resultado.ProductosDisponibles));
        Assert.Collection(resultado.ProductosStockBajo,
            producto => Assert.Equal(
                new ProductoReporteInventarioDto(2, "BAJ-002", "Stock igual al mínimo", "Armazones", 2, 2),
                producto),
            producto => Assert.Equal(
                new ProductoReporteInventarioDto(3, "BAJ-003", "Stock bajo", "Armazones", 1, 2),
                producto));
        Assert.Collection(resultado.ProductosAgotados,
            producto => Assert.Equal(
                new ProductoReporteInventarioDto(4, "AGO-004", "Agotado con mínimo cero", "Armazones", 0, 0),
                producto),
            producto => Assert.Equal(
                new ProductoReporteInventarioDto(5, "AGO-005", "Stock negativo", "Armazones", -1, 2),
                producto));
    }

    [Fact]
    public async Task ConsultarReporteInventario_SinProductosDevuelveListasYTotalesVacios()
    {
        var resultado = await CrearServicio(new ProductoRepositorioPrueba())
            .ConsultarReporteInventario(CancellationToken.None);

        Assert.Equal(0, resultado.TotalProductos);
        Assert.Equal(0, resultado.TotalDisponibles);
        Assert.Equal(0, resultado.TotalStockBajo);
        Assert.Equal(0, resultado.TotalAgotados);
        Assert.Empty(resultado.ProductosDisponibles);
        Assert.Empty(resultado.ProductosStockBajo);
        Assert.Empty(resultado.ProductosAgotados);
    }

    [Fact]
    public async Task ConsultarHistorialStock_FiltraPorProductoYRespetaPaginacion()
    {
        var esperado = new Optica.Api.Modules.Productos.DTOs.HistorialStockPaginadoResponseDto(
            2, 10, 15, 2, Array.Empty<Optica.Api.Modules.Productos.DTOs.HistorialStockMovimientoResponseDto>());
        var repositorio = new ProductoRepositorioPrueba { RespuestaHistorialProducto = esperado };

        var resultado = await CrearServicio(repositorio).ConsultarHistorialStock(7, 2, 10);

        Assert.Same(esperado, resultado);
        Assert.Equal(7, repositorio.UltimoIdProductoConsultado);
        Assert.Equal(2, repositorio.UltimaPaginaConsultada);
        Assert.Equal(10, repositorio.UltimoTamanoPaginaConsultado);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task ConsultarHistorialStock_RechazaPaginacionInvalida(int pagina, int tamanoPagina)
    {
        var repositorio = new ProductoRepositorioPrueba();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CrearServicio(repositorio).ConsultarHistorialStock(7, pagina, tamanoPagina));

        Assert.Null(repositorio.UltimoIdProductoConsultado);
    }

    private static Producto CrearProductoInventario(int id, string codigo, string nombre, int stock, int stockMinimo) => new()
    {
        IdProducto = id,
        Codigo = codigo,
        Nombre = nombre,
        Categoria = "Armazones",
        Stock = stock,
        StockMinimo = stockMinimo
    };

    [Fact]
    public async Task Galeria_CreaVariasImagenesYSeleccionaLaPrincipal()
    {
        var directorio = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new ProductoRepositorioPrueba();
            var servicio = new ProductoService(repo, new EntornoPrueba { WebRootPath = directorio });
            var dto = ProductoValido();
            dto.Imagenes = [Foto("uno.jpg"), Foto("dos.jpg")];
            dto.IndicePrincipal = 1;
            var resultado = await servicio.CrearProducto(dto, 42);

            Assert.Equal(2, resultado.RutasImagenes.Length);
            Assert.Equal(resultado.RutasImagenes[0], resultado.RutaImagen);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(directorio, "uploads", "productos")).Length);
        }
        finally { if (Directory.Exists(directorio)) Directory.Delete(directorio, true); }
    }

    [Fact]
    public async Task Galeria_LimpiaArchivosSiUnaImagenEsInvalida()
    {
        var directorio = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new ProductoRepositorioPrueba();
            var servicio = new ProductoService(repo, new EntornoPrueba { WebRootPath = directorio });
            var dto = ProductoValido();
            dto.Imagenes = [Foto("uno.jpg"), Foto("invalida.exe")];

            await Assert.ThrowsAsync<ArgumentException>(() => servicio.CrearProducto(dto, 42));
            Assert.Null(repo.UltimoProducto);
            Assert.Empty(Directory.GetFiles(Path.Combine(directorio, "uploads", "productos")));
        }
        finally { if (Directory.Exists(directorio)) Directory.Delete(directorio, true); }
    }

    [Fact]
    public async Task Galeria_ConservaLaImagenAnteriorYPermiteQuitarTodas()
    {
        var repo = new ProductoRepositorioPrueba
        {
            ProductoActual = new Producto { IdProducto = 1, Codigo = "OPT-001", Nombre = "Armazón",
                Categoria = "Armazones", RutaImagen = "/uploads/productos/anterior.jpg" }
        };
        var servicio = CrearServicio(repo);
        var dto = new ActualizarProductoDto
        {
            Codigo = "OPT-001", Nombre = "Armazón", Categoria = "Armazones", Estado = "Disponible"
        };
        var resultado = await servicio.ActualizarProducto(1, dto, 42);
        Assert.Equal(new[] { "/uploads/productos/anterior.jpg" }, resultado.RutasImagenes);

        dto.RutasConservadasJson = "[]";
        resultado = await servicio.ActualizarProducto(1, dto, 42);
        Assert.Empty(resultado.RutasImagenes);
        Assert.Null(resultado.RutaImagen);
    }

    [Fact]
    public async Task Galeria_RechazaRutasDeOtroProducto()
    {
        var repo = new ProductoRepositorioPrueba
        {
            ProductoActual = new Producto { IdProducto = 1, Codigo = "OPT-001", Nombre = "Armazón", Categoria = "Armazones" }
        };
        await Assert.ThrowsAsync<ArgumentException>(() => CrearServicio(repo).ActualizarProducto(1,
            new ActualizarProductoDto { Codigo = "OPT-001", Nombre = "Armazón", Categoria = "Armazones",
                Estado = "Disponible", RutasConservadas = ["/uploads/productos/ajena.jpg"] }, 42));
    }

    private static Microsoft.AspNetCore.Http.IFormFile Foto(string nombre) =>
        new Microsoft.AspNetCore.Http.FormFile(new MemoryStream([255, 216, 255, 217]), 0, 4, "imagenes", nombre)
        { Headers = new Microsoft.AspNetCore.Http.HeaderDictionary(), ContentType = "image/jpeg" };

    private static ProductoService CrearServicio(ProductoRepositorioPrueba repositorio) =>
        new(repositorio, new EntornoPrueba());

    private static CrearProductoDto ProductoValido() => new()
    {
        Codigo = "OPT-001",
        Nombre = "Armazón",
        Categoria = "Armazones",
        Precio = 100,
        Stock = 1,
        StockMinimo = 1,
        Estado = "Disponible"
    };
}

internal sealed class ProductoRepositorioPrueba : IProductoRepository
{
    private Producto? _productoActual;

    public bool CodigoExiste { get; set; }
    public bool ProductoEliminado { get; private set; }
    public bool FallarAlActualizarStock { get; set; }
    public Producto? ProductoActual
    {
        get => _productoActual;
        set
        {
            _productoActual = value;
            StockPersistido = value?.Stock;
            StockMinimoPersistido = value?.StockMinimo;
        }
    }

    public Producto? UltimoProducto { get; private set; }
    public int? StockPersistido { get; private set; }
    public int? StockMinimoPersistido { get; private set; }
    public int? UltimoIdProductoConsultado { get; private set; }
    public int? UltimaPaginaConsultada { get; private set; }
    public int? UltimoTamanoPaginaConsultado { get; private set; }
    public CancellationToken? TokenReporteInventario { get; private set; }
    public Optica.Api.Modules.Productos.DTOs.HistorialStockPaginadoResponseDto? RespuestaHistorialProducto { get; set; }
    public List<Producto> Productos { get; } = [];

    public Task<bool> ExisteCodigo(string codigo) => Task.FromResult(CodigoExiste);

    public Task<Producto> CrearConHistorialStock(Producto producto, int idResponsable)
    {
        producto.IdProducto = 1;
        UltimoProducto = producto;
        HistorialStock.AddRange(
            new HistorialStock
            {
                IdProducto = producto.IdProducto,
                IdResponsable = idResponsable,
                MotivoMovimiento = "Registro de nuevo producto",
                TipoStock = "Normal",
                ValorAnterior = 0,
                ValorActual = producto.Stock
            },
            new HistorialStock
            {
                IdProducto = producto.IdProducto,
                IdResponsable = idResponsable,
                MotivoMovimiento = "Registro de nuevo producto",
                TipoStock = "Minimo",
                ValorAnterior = 0,
                ValorActual = producto.StockMinimo
            });
        return Task.FromResult(producto);
    }

    public List<HistorialStock> HistorialStock { get; } = [];

    public Task<List<Producto>> Buscar(string termino) => Task.FromResult(Productos);

    public Task<List<Producto>> ObtenerParaReporteInventario(CancellationToken cancellationToken)
    {
        TokenReporteInventario = cancellationToken;
        return Task.FromResult(Productos);
    }

    public Task<Producto?> ObtenerPorId(int id) => Task.FromResult(ProductoActual ?? UltimoProducto);

    public Task<Producto> ActualizarConHistorialStock(Producto producto, int idResponsable, string motivoMovimiento)
    {
        if (FallarAlActualizarStock)
        {
            throw new InvalidOperationException("No se pudo guardar el producto.");
        }

        if (StockPersistido != producto.Stock)
        {
            HistorialStock.Add(new HistorialStock
            {
                IdProducto = producto.IdProducto,
                IdResponsable = idResponsable,
                MotivoMovimiento = motivoMovimiento,
                TipoStock = "Normal",
                ValorAnterior = StockPersistido ?? 0,
                ValorActual = producto.Stock
            });
        }

        if (StockMinimoPersistido != producto.StockMinimo)
        {
            HistorialStock.Add(new HistorialStock
            {
                IdProducto = producto.IdProducto,
                IdResponsable = idResponsable,
                MotivoMovimiento = motivoMovimiento,
                TipoStock = "Minimo",
                ValorAnterior = StockMinimoPersistido ?? 0,
                ValorActual = producto.StockMinimo
            });
        }

        StockPersistido = producto.Stock;
        StockMinimoPersistido = producto.StockMinimo;
        UltimoProducto = producto;
        return Task.FromResult(producto);
    }

    public Task<Optica.Api.Modules.Productos.DTOs.HistorialStockPaginadoResponseDto?> ConsultarHistorialStock(
        int idProducto,
        int pagina,
        int tamanoPagina)
    {
        UltimoIdProductoConsultado = idProducto;
        UltimaPaginaConsultada = pagina;
        UltimoTamanoPaginaConsultado = tamanoPagina;
        return Task.FromResult(RespuestaHistorialProducto);
    }

    public Task<Optica.Api.Modules.Productos.DTOs.HistorialStockPaginadoResponseDto> ConsultarHistorialStockReciente(
        int pagina,
        int tamanoPagina) => Task.FromResult(new Optica.Api.Modules.Productos.DTOs.HistorialStockPaginadoResponseDto(
            pagina,
            tamanoPagina,
            0,
            0,
            Array.Empty<Optica.Api.Modules.Productos.DTOs.HistorialStockMovimientoResponseDto>()));

    public Task<bool> Eliminar(int id)
    {
        ProductoEliminado = true;
        ProductoActual = null;
        UltimoProducto = null;
        return Task.FromResult(true);
    }
}

internal sealed class EntornoPrueba : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Optica.Tests";
    public string EnvironmentName { get; set; } = "Test";
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public string WebRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "optica-tests");
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
