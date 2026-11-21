using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Optica.Api.Data;
using Optica.Api.Modules.Autenticacion.DTOs;
using Optica.Api.Modules.Autenticacion.Interfaces;
using Optica.Api.Modules.Autenticacion.Services;
using Optica.Api.Modules.Clientes.DTOs;
using Optica.Api.Modules.Clientes.Models;
using Optica.Api.Modules.Clientes.Repositories;
using Optica.Api.Modules.Clientes.Services;
using Optica.Api.Modules.Notificaciones.Interfaces;
using Xunit;

namespace Optica.Ventas.Tests;

// Cliente creado por el personal: recibe una invitación, define su contraseña e inicia sesión.
public sealed class TestInvitacionClientes : IDisposable
{
    private const string Contrasena = "Optica123!";

    private readonly OpticaDbContext db = new(new DbContextOptionsBuilder<OpticaDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private readonly RelojManual reloj = new(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
    private readonly CorreoFalso correo = new();

    private readonly IConfiguration configuracion = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Correo:UrlBaseFrontend"] = "http://localhost:4200" })
        .Build();

    public void Dispose() => db.Dispose();

    private InvitacionClienteService Invitacion() => new(
        db, new PasswordHasher<CuentaCliente>(), correo, reloj, NullLogger<InvitacionClienteService>.Instance, configuracion);

    private AutenticacionClienteService Autenticacion() => new(
        db, new PasswordHasher<CuentaCliente>(), correo, reloj, NullLogger<AutenticacionClienteService>.Instance, configuracion);

    private async Task<Cliente> CrearClienteComoAdmin(string? correoCliente = "ana@example.test") =>
        await new ClienteService(new ClienteRepository(db)).RegistrarAsync(new CrearClienteDto
        {
            Rut = "12.345.678-5",
            Nombre = "Ana",
            Apellido = "Pérez",
            Correo = correoCliente,
            Telefono = "912345678"
        });

    [Fact]
    public async Task FlujoCompleto_ClienteCreadoPorAdminActivaLaCuentaEIniciaSesion()
    {
        var cliente = await CrearClienteComoAdmin();

        Assert.Equal(ResultadoInvitacionCliente.Enviada, await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None));
        var cuenta = await db.CuentasCliente.SingleAsync();
        Assert.Equal(InvitacionClienteService.EstadoPendienteActivacion, cuenta.Estado);
        Assert.Equal("ana@example.test", correo.Destinatario);
        Assert.Contains("Activa tu cuenta", correo.Cuerpo);

        // Antes de activar no puede iniciar sesión.
        var antes = await Autenticacion().IniciarSesionAsync(
            new LoginClienteRequest { Correo = "12345678-5", Contrasena = Contrasena }, CancellationToken.None);
        Assert.False(antes.CredencialesValidas);

        var token = ExtraerToken(correo.Cuerpo!);
        Assert.Equal(ResultadoActivacionCliente.Activada,
            await Invitacion().ActivarAsync(token, Contrasena, Contrasena, CancellationToken.None));

        var porRut = await Autenticacion().IniciarSesionAsync(
            new LoginClienteRequest { Correo = "12345678-5", Contrasena = Contrasena }, CancellationToken.None);
        var porCorreo = await Autenticacion().IniciarSesionAsync(
            new LoginClienteRequest { Correo = "ANA@example.test", Contrasena = Contrasena }, CancellationToken.None);
        Assert.True(porRut.CredencialesValidas);
        Assert.True(porCorreo.CredencialesValidas);
        Assert.Equal("Ana Pérez", porRut.Sesion?.Nombre);

        // El enlace es de un solo uso.
        Assert.Equal(ResultadoActivacionCliente.EnlaceInvalido,
            await Invitacion().ActivarAsync(token, "OtraClave123!", "OtraClave123!", CancellationToken.None));
    }

    [Fact]
    public async Task Invitar_ClienteSinCorreoNoCreaCuenta()
    {
        var cliente = await CrearClienteComoAdmin(correoCliente: null);

        Assert.Equal(ResultadoInvitacionCliente.SinCorreo, await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None));
        Assert.Empty(await db.CuentasCliente.ToListAsync());
        Assert.Null(correo.Destinatario);
    }

    [Fact]
    public async Task Invitar_RechazaClienteInexistenteEInactivo()
    {
        Assert.Equal(ResultadoInvitacionCliente.ClienteNoEncontrado, await Invitacion().InvitarAsync(999, CancellationToken.None));

        var cliente = await CrearClienteComoAdmin();
        cliente.Estado = "Inactivo";
        await db.SaveChangesAsync();
        Assert.Equal(ResultadoInvitacionCliente.ClienteInactivo, await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None));
    }

    [Fact]
    public async Task Invitar_NoTocaUnaCuentaYaActiva()
    {
        var cliente = await CrearClienteComoAdmin();
        db.CuentasCliente.Add(new CuentaCliente
        {
            IdCliente = cliente.IdCliente, Correo = "ana@example.test", CorreoNormalizado = "ANA@EXAMPLE.TEST",
            ContrasenaHash = "hash-existente", Estado = "Activa"
        });
        await db.SaveChangesAsync();

        Assert.Equal(ResultadoInvitacionCliente.CuentaExistente, await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None));
        Assert.Equal("hash-existente", (await db.CuentasCliente.SingleAsync()).ContrasenaHash);
    }

    [Fact]
    public async Task Invitar_RechazaCorreoDeLaCuentaDeOtroCliente()
    {
        var otro = new Cliente { Rut = "11111111-1", Nombre = "Otra", Apellido = "Persona", Estado = "Activo" };
        db.Clientes.Add(otro);
        await db.SaveChangesAsync();
        db.CuentasCliente.Add(new CuentaCliente
        {
            IdCliente = otro.IdCliente, Correo = "ana@example.test", CorreoNormalizado = "ANA@EXAMPLE.TEST",
            ContrasenaHash = "hash", Estado = "Activa"
        });
        await db.SaveChangesAsync();
        var cliente = await CrearClienteComoAdmin();

        Assert.Equal(ResultadoInvitacionCliente.CorreoEnUso, await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None));
        Assert.Single(await db.CuentasCliente.ToListAsync());
    }

    [Fact]
    public async Task Reenviar_EsperaUnMinutoEInvalidaElEnlaceAnterior()
    {
        var cliente = await CrearClienteComoAdmin();
        await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None);
        var primerToken = ExtraerToken(correo.Cuerpo!);

        Assert.Equal(ResultadoInvitacionCliente.Espera, await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None));

        reloj.Avanzar(TimeSpan.FromMinutes(2));
        Assert.Equal(ResultadoInvitacionCliente.Enviada, await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None));
        Assert.Single(await db.CuentasCliente.ToListAsync());

        Assert.Equal(ResultadoActivacionCliente.EnlaceInvalido,
            await Invitacion().ActivarAsync(primerToken, Contrasena, Contrasena, CancellationToken.None));
        Assert.Equal(ResultadoActivacionCliente.Activada,
            await Invitacion().ActivarAsync(ExtraerToken(correo.Cuerpo!), Contrasena, Contrasena, CancellationToken.None));
    }

    [Fact]
    public async Task Reenviar_UsaElCorreoActualizadoPorElPersonal()
    {
        var cliente = await CrearClienteComoAdmin();
        await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None);
        cliente.Correo = "ana.nueva@example.test";
        await db.SaveChangesAsync();
        reloj.Avanzar(TimeSpan.FromMinutes(2));

        await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None);

        Assert.Equal("ana.nueva@example.test", correo.Destinatario);
        Assert.Equal("ANA.NUEVA@EXAMPLE.TEST", (await db.CuentasCliente.SingleAsync()).CorreoNormalizado);
    }

    [Fact]
    public async Task Activar_RechazaEnlaceVencidoYContrasenasInvalidas()
    {
        var cliente = await CrearClienteComoAdmin();
        await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None);
        var token = ExtraerToken(correo.Cuerpo!);

        Assert.Equal(ResultadoActivacionCliente.ContrasenaInvalida,
            await Invitacion().ActivarAsync(token, "corta", "corta", CancellationToken.None));
        Assert.Equal(ResultadoActivacionCliente.ContrasenasNoCoinciden,
            await Invitacion().ActivarAsync(token, Contrasena, "Optica123?", CancellationToken.None));
        Assert.Equal(ResultadoActivacionCliente.EnlaceInvalido,
            await Invitacion().ActivarAsync("token-inventado", Contrasena, Contrasena, CancellationToken.None));

        reloj.Avanzar(TimeSpan.FromDays(8));
        Assert.Equal(ResultadoActivacionCliente.EnlaceInvalido,
            await Invitacion().ActivarAsync(token, Contrasena, Contrasena, CancellationToken.None));
        Assert.Equal(InvitacionClienteService.EstadoPendienteActivacion, (await db.CuentasCliente.SingleAsync()).Estado);
    }

    [Fact]
    public async Task CuentaInvitada_NoSeActivaConElEnlaceDeConfirmacionDeCorreo()
    {
        var cliente = await CrearClienteComoAdmin();
        await Invitacion().InvitarAsync(cliente.IdCliente, CancellationToken.None);
        var token = ExtraerToken(correo.Cuerpo!);

        // El token de invitación no sirve en el endpoint de confirmación de correo.
        Assert.False(await Autenticacion().ConfirmarCuentaAsync(token, CancellationToken.None));
        Assert.Equal(InvitacionClienteService.EstadoPendienteActivacion, (await db.CuentasCliente.SingleAsync()).Estado);
    }

    private static string ExtraerToken(string html)
    {
        const string marcador = "activar-cuenta?token=";
        var inicio = html.IndexOf(marcador, StringComparison.Ordinal) + marcador.Length;
        var fin = html.IndexOf('"', inicio);
        return Uri.UnescapeDataString(html[inicio..fin]);
    }

    private sealed class RelojManual(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset ahora = inicio;
        public void Avanzar(TimeSpan intervalo) => ahora = ahora.Add(intervalo);
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private sealed class CorreoFalso : IEmailService
    {
        public string? Destinatario { get; private set; }
        public string? Cuerpo { get; private set; }

        public Task<ResultadoEnvioCorreo> EnviarConResultadoAsync(
            string destinatario, string asunto, string cuerpoHtml, CancellationToken cancellationToken, bool importante = false)
        {
            Destinatario = destinatario;
            Cuerpo = cuerpoHtml;
            return Task.FromResult(new ResultadoEnvioCorreo(true));
        }

        public Task<bool> EnviarAsync(string? destinatario, string asunto, string cuerpoHtml, CancellationToken cancellationToken,
            bool importante = false, byte[]? adjuntoBytes = null, string? nombreArchivoAdjunto = null, string? tipoContenidoAdjunto = null)
        {
            Destinatario = destinatario;
            Cuerpo = cuerpoHtml;
            return Task.FromResult(true);
        }
    }
}
