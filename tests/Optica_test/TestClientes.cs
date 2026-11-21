using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Optica.Api.Data;
using Optica.Api.Modules.Autenticacion.Interfaces;
using Optica.Api.Modules.Clientes.Controllers;
using Optica.Api.Modules.Clientes.DTOs;
using Optica.Api.Modules.Clientes.Models;
using Optica.Api.Modules.Clientes.Repositories;
using Optica.Api.Modules.Clientes.Services;
using Xunit;
using Optica.Api.Modules.Pedidos.Models;
using Optica.Api.Modules.RegistroRecetas.Models;

namespace Optica.Ventas.Tests;

public class TestClientes : IDisposable
{
    private readonly OpticaDbContext db = new(new DbContextOptionsBuilder<OpticaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => db.Dispose();

    private readonly InvitacionFalsa invitacion = new();

    private ClientesController Controlador()
    {
        var repo = new ClienteRepository(db);
        var service = new ClienteService(repo);
        return new ClientesController(service, invitacion, NullLogger<ClientesController>.Instance);
    }

    private sealed class InvitacionFalsa : IInvitacionClienteService
    {
        public List<int> Invitados { get; } = new();
        public ResultadoInvitacionCliente Resultado { get; set; } = ResultadoInvitacionCliente.Enviada;

        public bool Fallar { get; set; }

        public Task<ResultadoInvitacionCliente> InvitarAsync(int idCliente, CancellationToken cancellationToken)
        {
            Invitados.Add(idCliente);
            if (Fallar) throw new InvalidOperationException("Fallo simulado");
            return Task.FromResult(Resultado);
        }

        public Task<ResultadoActivacionCliente> ActivarAsync(string token, string nuevaContrasena, string confirmarContrasena, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Registrar_ConCorreoEnviaInvitacionYLaInformaEnLaRespuesta()
    {
        var resultado = Assert.IsType<CreatedAtActionResult>(await Controlador().Registrar(ValidoDto()));

        var guardado = await db.Clientes.SingleAsync();
        Assert.Equal(new[] { guardado.IdCliente }, invitacion.Invitados);
        var cuerpo = resultado.Value!;
        Assert.Equal(true, cuerpo.GetType().GetProperty("invitacionEnviada")!.GetValue(cuerpo));
        Assert.Equal(guardado.Rut, cuerpo.GetType().GetProperty("Rut")!.GetValue(cuerpo));
    }

    [Fact]
    public async Task Registrar_SinCorreoNoEnviaInvitacion()
    {
        var dto = ValidoDto();
        dto.Correo = null;
        dto.Telefono = "912345678";

        var resultado = Assert.IsType<CreatedAtActionResult>(await Controlador().Registrar(dto));

        Assert.Empty(invitacion.Invitados);
        Assert.Equal(false, resultado.Value!.GetType().GetProperty("invitacionEnviada")!.GetValue(resultado.Value));
    }

    [Fact]
    public async Task Registrar_FalloDeInvitacionNoDeshaceElAlta()
    {
        invitacion.Resultado = ResultadoInvitacionCliente.ErrorEnvio;

        Assert.IsType<CreatedAtActionResult>(await Controlador().Registrar(ValidoDto()));
        Assert.Single(await db.Clientes.ToListAsync());
    }

    [Fact]
    public async Task Invitar_ErrorInesperadoDevuelve500ConMensaje()
    {
        invitacion.Fallar = true;
        var resultado = Assert.IsType<ObjectResult>(await Controlador().Invitar(7));
        Assert.Equal(500, resultado.StatusCode);
    }

    [Fact]
    public async Task Registrar_ExcepcionEnInvitacionNoDeshaceElAlta()
    {
        invitacion.Fallar = true;
        Assert.IsType<CreatedAtActionResult>(await Controlador().Registrar(ValidoDto()));
        Assert.Single(await db.Clientes.ToListAsync());
    }

    [Theory]
    [InlineData(ResultadoInvitacionCliente.Enviada, typeof(OkObjectResult))]
    [InlineData(ResultadoInvitacionCliente.ClienteNoEncontrado, typeof(NotFoundObjectResult))]
    [InlineData(ResultadoInvitacionCliente.CuentaExistente, typeof(ConflictObjectResult))]
    [InlineData(ResultadoInvitacionCliente.SinCorreo, typeof(BadRequestObjectResult))]
    public async Task Invitar_TraduceElResultadoAHttp(ResultadoInvitacionCliente resultado, Type esperado)
    {
        invitacion.Resultado = resultado;
        Assert.IsType(esperado, await Controlador().Invitar(7));
        Assert.Equal(new[] { 7 }, invitacion.Invitados);
    }

    private PerfilClienteController ControladorPerfil(int? idCliente)
    {
        var controlador = new PerfilClienteController(new ClienteService(new ClienteRepository(db)));
        var identidad = idCliente.HasValue
            ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, idCliente.Value.ToString()) }, "Prueba")
            : new ClaimsIdentity();
        controlador.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identidad) } };
        return controlador;
    }

    [Fact]
    public async Task Perfil_ConsultaYActualizaSoloElClienteDeLaSesion()
    {
        var propio = new Cliente { Rut = "11111111-1", Nombre = "Ana", Apellido = "Uno", Correo = "ana@example.test", Estado = "Activo" };
        var ajeno = new Cliente { Rut = "22222222-2", Nombre = "Luis", Apellido = "Dos", Correo = "luis@example.test", Estado = "Activo" };
        db.Clientes.AddRange(propio, ajeno);
        await db.SaveChangesAsync();
        var controlador = ControladorPerfil(propio.IdCliente);

        var lectura = Assert.IsType<OkObjectResult>((await controlador.Obtener()).Result);
        Assert.Equal(propio.Rut, Assert.IsType<PerfilClienteDto>(lectura.Value).Rut);
        var actualizacion = Assert.IsType<OkObjectResult>((await controlador.Actualizar(new ActualizarPerfilClienteDto
            { Nombre = "Ana María", Apellido = "Uno", Telefono = "+56 9 1234 5678" })).Result);
        Assert.Equal("Ana María", Assert.IsType<PerfilClienteDto>(actualizacion.Value).Nombre);
        db.ChangeTracker.Clear();
        Assert.Equal("Ana María", (await db.Clientes.SingleAsync(c => c.IdCliente == propio.IdCliente)).Nombre);
        Assert.Equal("Luis", (await db.Clientes.SingleAsync(c => c.IdCliente == ajeno.IdCliente)).Nombre);
        Assert.Equal("11111111-1", (await db.Clientes.SingleAsync(c => c.IdCliente == propio.IdCliente)).Rut);
    }

    [Fact]
    public async Task Perfil_SinIdentidadDevuelve401YEndpointExigeAutenticacion()
    {
        var controlador = ControladorPerfil(null);
        Assert.IsType<UnauthorizedResult>((await controlador.Obtener()).Result);
        var autorizacion = Assert.Single(typeof(PerfilClienteController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));
        Assert.Equal("ClienteAutenticado", ((Microsoft.AspNetCore.Authorization.AuthorizeAttribute)autorizacion).Policy);
    }

    [Fact]
    public async Task Perfil_RechazaTelefonoInvalido()
    {
        var cliente = new Cliente { Rut = "12345678-5", Nombre = "Ana", Apellido = "Pérez", Correo = "ana@example.test", Estado = "Activo" };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        var respuesta = await ControladorPerfil(cliente.IdCliente).Actualizar(new ActualizarPerfilClienteDto
            { Nombre = "Ana", Apellido = "Pérez", Telefono = "123" });
        Assert.IsType<BadRequestObjectResult>(respuesta.Result);
    }

    private static CrearClienteDto ValidoDto() => new()
    {
        Rut = " 12.345.678-5 ",
        Nombre = "Ana",
        Apellido = "Pérez",
        Correo = " ana@example.test "
    };

    [Fact]
    public async Task Registrar_NormalizaRutYContactoYAsignaEstado()
    {
        var dto = ValidoDto();
        var resultado = Assert.IsType<CreatedAtActionResult>(await Controlador().Registrar(dto));
        var guardado = await db.Clientes.SingleAsync();

        // Tu servicio elimina espacios y puntos, conservando el guion
        Assert.Equal("12345678-5", guardado.Rut);
        Assert.Equal("ana@example.test", guardado.Correo);
        Assert.Equal("Activo", guardado.Estado);
        Assert.NotEqual(default, guardado.FechaRegistro);
        Assert.Null(guardado.Telefono);
    }

    [Fact]
    public async Task Registrar_AceptaSoloTelefono()
    {
        var dto = ValidoDto();
        dto.Correo = null;
        dto.Telefono = " 912345678 ";

        Assert.IsType<CreatedAtActionResult>(await Controlador().Registrar(dto));
        Assert.Equal("912345678", (await db.Clientes.SingleAsync()).Telefono);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", " ")]
    public async Task Registrar_RechazaFaltaDeContacto(string? telefono, string? correo)
    {
        var dto = ValidoDto();
        dto.Telefono = telefono;
        dto.Correo = correo;

        Assert.IsType<BadRequestObjectResult>(await Controlador().Registrar(dto));
        Assert.Empty(await db.Clientes.ToListAsync());
    }

    [Fact]
    public async Task Registrar_RechazaRutDuplicado()
    {
        // Se siembra el cliente con el formato limpio que maneja el repositorio
        db.Clientes.Add(new Cliente { Rut = "12345678-5", Correo = "otro@example.test", Nombre = "Otro", Apellido = "Cliente" });
        await db.SaveChangesAsync();

        // ValidoDto() tiene " 12.345.678-5 ", que limpia a "12345678-5" y debe disparar Conflict
        Assert.IsType<ConflictObjectResult>(await Controlador().Registrar(ValidoDto()));
        Assert.Single(await db.Clientes.ToListAsync());
    }

    [Fact]
    public async Task Actualizar_ConservaRutEstadoYFecha()
    {
        var cliente = new Cliente
        {
            Rut = "12345678-5",
            Nombre = "Ana",
            Apellido = "Pérez",
            Correo = "ana@example.test",
            Estado = "Activo",
            FechaRegistro = new DateTime(2025, 1, 1)
        };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();

        var cambios = new ActualizarClienteDto
        {
            Nombre = " María ",
            Apellido = " Soto ",
            Correo = " nueva@example.test "
        };

        Assert.IsType<OkObjectResult>(await Controlador().Actualizar(cliente.IdCliente, cambios));

        db.ChangeTracker.Clear();
        var guardado = await db.Clientes.SingleAsync();
        Assert.Equal("María", guardado.Nombre);
        Assert.Equal("Soto", guardado.Apellido);
        Assert.Equal(cliente.Rut, guardado.Rut);
        Assert.Equal("Activo", guardado.Estado);
        Assert.Equal(new DateTime(2025, 1, 1), guardado.FechaRegistro);
    }

    [Fact]
    public async Task Actualizar_InexistenteDevuelve404()
    {
        var cambios = new ActualizarClienteDto
        {
            Nombre = "Prueba",
            Apellido = "Inexistente",
            Correo = "prueba@example.test"
        };
        Assert.IsType<NotFoundObjectResult>(await Controlador().Actualizar(999, cambios));
    }

    [Theory]
    [InlineData("Activo")]
    [InlineData("Inactivo")]
    public async Task CambiarEstado_PersisteSinEliminarCliente(string estado)
    {
        var cliente = new Cliente { Rut = "12345678-5", Nombre = "Ana", Apellido = "Pérez", Correo = "ana@example.test", Estado = "Activo" };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();

        Assert.IsType<OkObjectResult>(await Controlador().CambiarEstado(cliente.IdCliente, new CambiarEstadoDto { NuevoEstado = estado }));
        db.ChangeTracker.Clear();
        Assert.Equal(estado, (await db.Clientes.SingleAsync()).Estado);
    }

    [Fact]
    public async Task CambiarEstado_RechazaEstadoInvalido()
    {
        var cliente = new Cliente { Rut = "12345678-5", Nombre = "Ana", Apellido = "Pérez", Correo = "ana@example.test", Estado = "Activo" };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();

        Assert.IsType<BadRequestObjectResult>(await Controlador().CambiarEstado(cliente.IdCliente, new CambiarEstadoDto { NuevoEstado = "Borrado" }));
        Assert.Equal("Activo", (await db.Clientes.SingleAsync()).Estado);
    }

    [Theory]
    [InlineData("12.345.678-5", true)]
    [InlineData("12345678-9", false)]
    [InlineData(null, false)]
    [InlineData("abc", false)]
    public void Rut_ValidaDigitoVerificador(string? rut, bool valido) => Assert.Equal(valido, RutChilenoValidator.EsValido(rut));

    [Theory]
    [InlineData("+56 9 1234 5678", true)]
    [InlineData("912345678", true)]
    [InlineData("123", false)]
    [InlineData(null, false)]
    public void Telefono_ValidaFormato(string? telefono, bool valido) => Assert.Equal(valido, TelefonoChilenoValidator.EsValido(telefono));

    [Fact]
    public async Task ObtenerHistorial_ClienteExistente_RetornaOkConHistorialCompleto()
    {
        // 1. Arrange: Crear cliente, receta con graduaciones y un pedido
        var cliente = new Cliente { Rut = "12345678-7", Nombre = "Juan", Apellido = "Pérez", Correo = "juan@test.cl", Estado = "Activo" };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();

        var receta = new Receta
        {
            ClienteId = cliente.IdCliente,
            Fecha = DateTime.Today,
            Observaciones = "Control anual",
            Graduaciones = new List<Graduacion>
            {
                new() { Ojo = "OD", Esfera = -1.25m, Cilindro = -0.50m, Eje = 90 },
                new() { Ojo = "OI", Esfera = -1.00m, Cilindro = -0.25m, Eje = 85 }
            }
        };
        db.Recetas.Add(receta);

        var pedido = new Pedido
        {
            Rut = "12.345.678-7", // Formato con puntos para comprobar que normaliza
            Fecha = DateTime.Today,
            Estado = "Entregado",
            Total = 99980m,
            Anotaciones = "Lentes listos"
        };
        db.Pedidos.Add(pedido);
        await db.SaveChangesAsync();

        // 2. Act
        var respuesta = await Controlador().ObtenerHistorial(cliente.IdCliente);

        // 3. Assert
        var okResult = Assert.IsType<OkObjectResult>(respuesta.Result);
        var historial = Assert.IsType<HistorialClienteDto>(okResult.Value);

        Assert.Equal(cliente.IdCliente, historial.IdCliente);
        Assert.Equal("Juan Pérez", historial.NombreCompleto);
        Assert.Single(historial.Recetas);
        Assert.Equal(2, historial.Recetas[0].Graduaciones.Count);
        Assert.Single(historial.Pedidos);
        Assert.Equal(99980m, historial.Pedidos[0].Total);
    }

    [Fact]
    public async Task ObtenerHistorial_ClienteInexistente_Retorna404()
    {
        var respuesta = await Controlador().ObtenerHistorial(9999);
        Assert.IsType<NotFoundObjectResult>(respuesta.Result);
    }
}
