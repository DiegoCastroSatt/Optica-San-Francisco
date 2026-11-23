using Microsoft.Extensions.Logging.Abstractions;
using Optica.Api.Modules.AvisosReposicion.DTOs;
using Optica.Api.Modules.AvisosReposicion.Interfaces;
using Optica.Api.Modules.AvisosReposicion.Models;
using Optica.Api.Modules.AvisosReposicion.Services;
using Optica.Api.Modules.Clientes.Models;
using Optica.Api.Modules.Notificaciones.Interfaces;
using Optica.Api.Modules.Productos.Models;
using Xunit;

namespace Optica.Ventas.Tests;

public sealed class TestAvisosReposicion
{
    [Fact]
    public async Task Crear_GuardaAvisoAbiertoConLaFechaUtcYElProductoSolicitado()
    {
        var ahora = new DateTimeOffset(2026, 11, 23, 16, 0, 0, TimeSpan.Zero);
        var repositorio = new RepositorioAvisos();
        var servicio = CrearServicio(repositorio, new CorreoPrueba(), new RelojPrueba(ahora));

        var resultado = await servicio.Crear(7, 12, CancellationToken.None);

        Assert.Equal(41, resultado.IdAviso);
        Assert.Equal(12, resultado.IdProducto);
        Assert.Equal("Armazón agotado", resultado.NombreProducto);
        Assert.Equal(ahora, resultado.Fecha);
        Assert.Equal("Abierta", resultado.Estado);
        Assert.NotNull(repositorio.AvisoCreado);
        Assert.Equal(repositorio.Cliente!.Rut, repositorio.AvisoCreado.Rut);
        Assert.Equal(12, repositorio.AvisoCreado.IdProducto);
        Assert.Equal(ahora.UtcDateTime, repositorio.AvisoCreado.Fecha);
        Assert.Equal("Abierta", repositorio.AvisoCreado.Estado);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Crear_RechazaIdentificadorDeProductoInvalidoAntesDeConsultarDatos(int idProducto)
    {
        var repositorio = new RepositorioAvisos();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CrearServicio(repositorio).Crear(7, idProducto, CancellationToken.None));

        Assert.Equal(0, repositorio.ConsultasCliente);
        Assert.Equal(0, repositorio.ConsultasProducto);
    }

    [Fact]
    public async Task Crear_RechazaClienteInexistente()
    {
        var repositorio = new RepositorioAvisos { Cliente = null };

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            CrearServicio(repositorio).Crear(7, 12, CancellationToken.None));

        Assert.Equal(0, repositorio.ConsultasProducto);
    }

    [Fact]
    public async Task Crear_RechazaProductoInexistente()
    {
        var repositorio = new RepositorioAvisos { Producto = null };

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            CrearServicio(repositorio).Crear(7, 12, CancellationToken.None));

        Assert.Equal(1, repositorio.ConsultasCliente);
    }

    [Fact]
    public async Task Crear_RechazaProductoQueYaTieneStock()
    {
        var repositorio = new RepositorioAvisos { Producto = new Producto { IdProducto = 12, Stock = 1 } };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CrearServicio(repositorio).Crear(7, 12, CancellationToken.None));

        Assert.Equal(0, repositorio.ConsultasAvisoAbierto);
        Assert.Null(repositorio.AvisoCreado);
    }

    [Fact]
    public async Task Crear_RechazaAvisoAbiertoDuplicado()
    {
        var repositorio = new RepositorioAvisos { ExisteAviso = true };

        await Assert.ThrowsAsync<AvisoReposicionDuplicadoException>(() =>
            CrearServicio(repositorio).Crear(7, 12, CancellationToken.None));

        Assert.Equal(1, repositorio.ConsultasAvisoAbierto);
        Assert.Null(repositorio.AvisoCreado);
    }

    [Fact]
    public async Task ProcesarReposiciones_EnviaCorreoHtmlSeguroYCierraElAviso()
    {
        var repositorio = new RepositorioAvisos
        {
            AvisosPendientes = [CrearAvisoAdministracion("<script>alert(1)</script>", "<b>Cliente</b>")]
        };
        var correo = new CorreoPrueba();

        await CrearServicio(repositorio, correo).ProcesarReposicionesDisponibles(CancellationToken.None);

        Assert.Equal("cliente@example.test", correo.Destinatario);
        Assert.Equal("Producto disponible nuevamente: <script>alert(1)</script>", correo.Asunto);
        Assert.Contains("&lt;b&gt;Cliente&lt;/b&gt;", correo.CuerpoHtml);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", correo.CuerpoHtml);
        Assert.DoesNotContain("<script>", correo.CuerpoHtml);
        Assert.Equal([29], repositorio.AvisosCerrados);
    }

    [Fact]
    public async Task ProcesarReposiciones_NoCierraElAvisoSiFallaElEnvio()
    {
        var repositorio = new RepositorioAvisos
        {
            AvisosPendientes = [CrearAvisoAdministracion()]
        };
        var correo = new CorreoPrueba { Resultado = new ResultadoEnvioCorreo(false, "SMTP no disponible.") };

        await CrearServicio(repositorio, correo).ProcesarReposicionesDisponibles(CancellationToken.None);

        Assert.Equal(1, correo.Envios);
        Assert.Empty(repositorio.AvisosCerrados);
    }

    private static AvisoReposicionService CrearServicio(
        RepositorioAvisos repositorio,
        CorreoPrueba? correo = null,
        TimeProvider? reloj = null) =>
        new(
            repositorio,
            correo ?? new CorreoPrueba(),
            reloj ?? new RelojPrueba(new DateTimeOffset(2026, 11, 23, 16, 0, 0, TimeSpan.Zero)),
            NullLogger<AvisoReposicionService>.Instance);

    private static AvisoReposicionAdministracionResponseDto CrearAvisoAdministracion(
        string nombreProducto = "Armazón agotado",
        string nombreCliente = "Cliente de prueba") =>
        new(
            29,
            "12345678-5",
            nombreCliente,
            "cliente@example.test",
            12,
            nombreProducto,
            new DateTimeOffset(2026, 11, 23, 16, 0, 0, TimeSpan.Zero),
            "Abierta");

    private sealed class RepositorioAvisos : IAvisoReposicionRepository
    {
        public Cliente? Cliente { get; set; } = new()
        {
            IdCliente = 7,
            Rut = "12345678-5",
            Nombre = "Cliente",
            Apellido = "de prueba"
        };
        public Producto? Producto { get; set; } = new()
        {
            IdProducto = 12,
            Nombre = "Armazón agotado",
            Stock = 0
        };
        public bool ExisteAviso { get; set; }
        public int ConsultasCliente { get; private set; }
        public int ConsultasProducto { get; private set; }
        public int ConsultasAvisoAbierto { get; private set; }
        public AvisoReposicion? AvisoCreado { get; private set; }
        public IReadOnlyList<AvisoReposicionAdministracionResponseDto> AvisosPendientes { get; set; } = [];
        public List<int> AvisosCerrados { get; } = [];

        public Task<Cliente?> ObtenerCliente(int idCliente, CancellationToken cancellationToken)
        {
            ConsultasCliente++;
            return Task.FromResult(Cliente);
        }

        public Task<Producto?> ObtenerProducto(int idProducto, CancellationToken cancellationToken)
        {
            ConsultasProducto++;
            return Task.FromResult(Producto);
        }

        public Task<bool> ExisteAvisoAbierto(string rut, int idProducto, CancellationToken cancellationToken)
        {
            ConsultasAvisoAbierto++;
            return Task.FromResult(ExisteAviso);
        }

        public Task<AvisoReposicion> Crear(AvisoReposicion aviso, CancellationToken cancellationToken)
        {
            aviso.IdAviso = 41;
            AvisoCreado = aviso;
            return Task.FromResult(aviso);
        }

        public Task<IReadOnlyList<AvisoReposicionResponseDto>> ListarPorCliente(
            int idCliente,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AvisoReposicionResponseDto>>([]);

        public Task<IReadOnlyList<AvisoReposicionAdministracionResponseDto>> ListarTodas(
            CancellationToken cancellationToken) =>
            Task.FromResult(AvisosPendientes);

        public Task<IReadOnlyList<AvisoReposicionAdministracionResponseDto>> ListarPendientesConStock(
            CancellationToken cancellationToken) =>
            Task.FromResult(AvisosPendientes);

        public Task Cerrar(int idAviso, CancellationToken cancellationToken)
        {
            AvisosCerrados.Add(idAviso);
            return Task.CompletedTask;
        }
    }

    private sealed class CorreoPrueba : IEmailService
    {
        public ResultadoEnvioCorreo Resultado { get; set; } = new(true);
        public string? Destinatario { get; private set; }
        public string? Asunto { get; private set; }
        public string? CuerpoHtml { get; private set; }
        public int Envios { get; private set; }

        public Task<bool> EnviarAsync(
            string? destinatario,
            string asunto,
            string cuerpoHtml,
            CancellationToken cancellationToken,
            bool importante = false,
            byte[]? adjunto = null,
            string? nombreAdjunto = null,
            string? tipoAdjunto = null) =>
            Task.FromResult(Resultado.Exitoso);

        public Task<ResultadoEnvioCorreo> EnviarConResultadoAsync(
            string? destinatario,
            string asunto,
            string cuerpoHtml,
            CancellationToken cancellationToken,
            bool importante = false,
            byte[]? adjunto = null,
            string? nombreAdjunto = null,
            string? tipoAdjunto = null)
        {
            Envios++;
            Destinatario = destinatario;
            Asunto = asunto;
            CuerpoHtml = cuerpoHtml;
            return Task.FromResult(Resultado);
        }
    }

    private sealed class RelojPrueba(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
