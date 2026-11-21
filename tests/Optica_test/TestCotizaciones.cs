using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Optica.Api.Modules.Clientes.DTOs;
using Optica.Api.Modules.Clientes.Interfaces;
using Optica.Api.Modules.Clientes.Models;
using Optica.Api.Modules.Cotizaciones.Controllers;
using Optica.Api.Modules.Cotizaciones.DTOs;
using Optica.Api.Modules.Cotizaciones.Interfaces;
using Optica.Api.Modules.Cotizaciones.Models;
using Optica.Api.Modules.Cotizaciones.Services;
using Optica.Api.Modules.Pedidos.Controllers;
using Optica.Api.Modules.Pedidos.DTOs;
using Optica.Api.Modules.Pedidos.Exceptions;
using Optica.Api.Modules.Pedidos.Interfaces;
using Xunit;

namespace Optica.Ventas.Tests;

public class TestCotizaciones
{
    [Fact]
    public async Task Crear_ConDatosValidos_GeneraCotizacionPendiente()
    {
        var repo = new RepositorioCotizacionesFalso();
        var clientesRepo = new RepositorioClientesFalso
        {
            Cliente = new Cliente { Rut = "123456785", Nombre = "Juan", Apellido = "Pérez", Estado = "Activo" }
        };
        var servicio = new CotizacionService(repo, clientesRepo);

        var dto = new CreateCotizacionDto
        {
            Rut = "12.345.678-5",
            Fecha = new DateTime(2026, 11, 21),
            FechaEntregaEstimada = new DateTime(2026, 11, 28),
            Total = 45000,
            Productos = "Armazón Ray-Ban",
            Servicios = "Montaje",
            Anotaciones = "Antireflejo"
        };

        var resultado = await servicio.CrearCotizacionAsync(dto);

        Assert.Equal(1, resultado.IdCotizacion);
        Assert.Equal("123456785", resultado.Rut);
        Assert.Equal("Juan Pérez", resultado.NombreCliente);
        Assert.Equal(45000, resultado.Total);
        Assert.Equal("Pendiente", resultado.Estado);
        Assert.Equal("Armazón Ray-Ban", resultado.Productos);
        Assert.Equal("Montaje", resultado.Servicios);
        Assert.Equal("Antireflejo", resultado.Anotaciones);
        Assert.NotNull(repo.Guardada);
        Assert.Equal("Pendiente", repo.Guardada!.Estado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345678-9")] // Dígito verificador incorrecto
    public async Task Crear_ConRutInvalido_LanzaArgumentException(string rutInvalido)
    {
        var repo = new RepositorioCotizacionesFalso();
        var clientesRepo = new RepositorioClientesFalso();
        var servicio = new CotizacionService(repo, clientesRepo);

        var dto = new CreateCotizacionDto
        {
            Rut = rutInvalido,
            Total = 10000,
            Productos = "Lentes"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => servicio.CrearCotizacionAsync(dto));
        Assert.Null(repo.Guardada);
    }

    [Fact]
    public async Task Crear_ConClienteInactivoOInexistente_LanzaArgumentException()
    {
        var repo = new RepositorioCotizacionesFalso();
        var clientesRepo = new RepositorioClientesFalso { Cliente = null };
        var servicio = new CotizacionService(repo, clientesRepo);

        var dto = new CreateCotizacionDto
        {
            Rut = "12.345.678-5",
            Total = 10000,
            Productos = "Lentes"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => servicio.CrearCotizacionAsync(dto));
        Assert.Null(repo.Guardada);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public async Task Crear_ConTotalMenorOIgualACero_LanzaArgumentException(decimal totalInvalido)
    {
        var repo = new RepositorioCotizacionesFalso();
        var clientesRepo = new RepositorioClientesFalso
        {
            Cliente = new Cliente { Rut = "123456785", Nombre = "Juan", Apellido = "Pérez", Estado = "Activo" }
        };
        var servicio = new CotizacionService(repo, clientesRepo);

        var dto = new CreateCotizacionDto
        {
            Rut = "12.345.678-5",
            Total = totalInvalido,
            Productos = "Lentes"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => servicio.CrearCotizacionAsync(dto));
        Assert.Null(repo.Guardada);
    }

    [Fact]
    public async Task Crear_ConProductosYServiciosEnBlanco_EsValidoSiTieneTotal()
    {
        var repo = new RepositorioCotizacionesFalso();
        var clientesRepo = new RepositorioClientesFalso
        {
            Cliente = new Cliente { Rut = "123456785", Nombre = "Juan", Apellido = "Pérez", Estado = "Activo" }
        };
        var servicio = new CotizacionService(repo, clientesRepo);

        var dto = new CreateCotizacionDto
        {
            Rut = "12.345.678-5",
            Total = 15000,
            Productos = "   ",
            Servicios = null,
            Anotaciones = "Ajuste libre"
        };

        var resultado = await servicio.CrearCotizacionAsync(dto);
        Assert.NotNull(resultado);
        Assert.Equal(15000, resultado.Total);
    }

    [Fact]
    public async Task Controller_DevuelveConflict409_CuandoCotizacionYaFueConvertida()
    {
        var servicioPedidos = new ServicioPedidosConConflicto();
        var controller = new PedidosController(servicioPedidos);

        var dto = new CreatePedidoDto
        {
            Rut = "12.345.678-5",
            Fecha = DateTime.UtcNow,
            Total = 50000,
            IdCotizacion = 10
        };

        var resultado = await controller.CreatePedido(dto);

        var conflict = Assert.IsType<ObjectResult>(resultado);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    private sealed class RepositorioCotizacionesFalso : ICotizacionRepository
    {
        public Cotizacion? Guardada { get; private set; }
        private int _secuencia = 0;

        public Task<Cotizacion> CrearCotizacionAsync(Cotizacion cotizacion)
        {
            cotizacion.IdCotizacion = ++_secuencia;
            Guardada = cotizacion;
            return Task.FromResult(cotizacion);
        }

        public Task<Cotizacion?> ObtenerCotizacionPorIdAsync(int id)
        {
            return Task.FromResult(Guardada?.IdCotizacion == id ? Guardada : null);
        }
    }

    private sealed class RepositorioClientesFalso : IClienteRepository
    {
        public Cliente? Cliente { get; set; }
        public Task<Cliente?> ObtenerPorRutAsync(string rut) => Task.FromResult(Cliente);
        public Task<Cliente?> ObtenerPorIdAsync(int id) => Task.FromResult(Cliente);
        public Task<IEnumerable<Cliente>> BuscarAsync(string? termino) => Task.FromResult<IEnumerable<Cliente>>([]);
        public Task<bool> ExisteRutAsync(string rut) => Task.FromResult(Cliente != null);
        public Task<Cliente> CrearAsync(Cliente cliente) => Task.FromResult(cliente);
        public Task ActualizarAsync(Cliente cliente) => Task.CompletedTask;
        public Task<HistorialClienteDto?> ObtenerHistorialAsync(int idCliente) => Task.FromResult<HistorialClienteDto?>(null);
        public Task<PerfilClienteDto?> ObtenerPerfilAsync(int idCliente) => Task.FromResult<PerfilClienteDto?>(null);
        public Task ActualizarPerfilAsync(int idCliente, ActualizarPerfilClienteDto dto) => Task.CompletedTask;
    }

    private sealed class ServicioPedidosConConflicto : IPedidoService
    {
        public Task<PedidoResponseDto> CrearPedidoAsync(CreatePedidoDto dto)
        {
            throw new CotizacionYaConvertidaException(88, "Esta cotización ya fue convertida en el pedido N° 88.");
        }

        public Task<PedidoResponseDto> ActualizarEstadoAsync(int id, UpdateEstadoPedidoDto dto) => throw new NotImplementedException();
        public Task<IEnumerable<ClienteActivoDto>> BuscarClientesPorRutAsync(string rut) => throw new NotImplementedException();
        public Task<IEnumerable<ClienteActivoDto>> ObtenerClientesActivosAsync() => throw new NotImplementedException();
        public Task<PedidosPaginaResponseDto> ObtenerPaginaAsync(int pagina, int tamano, string termino) => throw new NotImplementedException();
        public Task<PedidoResponseDto?> ObtenerPedidoPorIdAsync(int id) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoResponseDto>> ObtenerPedidosAsync() => throw new NotImplementedException();
        public Task<IEnumerable<PedidoResponseDto>> ObtenerPedidosPorClienteIdAsync(int idCliente) => throw new NotImplementedException();
        public Task<IEnumerable<PedidoResponseDto>> ObtenerPedidosPorRutAsync(string rut) => throw new NotImplementedException();
    }
}
