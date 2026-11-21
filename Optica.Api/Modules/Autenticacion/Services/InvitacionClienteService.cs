using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Optica.Api.Data;
using Optica.Api.Modules.Autenticacion.Interfaces;
using Optica.Api.Modules.Clientes.Models;
using Optica.Api.Modules.Notificaciones.Interfaces;

namespace Optica.Api.Modules.Autenticacion.Services;

// Reutiliza el token, el hash y la plantilla del correo de confirmación de cuenta.
// La cuenta invitada queda en "PendienteActivacion", sin contraseña, hasta que el cliente la define.
public sealed class InvitacionClienteService(
    OpticaDbContext db,
    IPasswordHasher<CuentaCliente> passwordHasher,
    IEmailService emailService,
    TimeProvider proveedorTiempo,
    ILogger<InvitacionClienteService> logger,
    IConfiguration configuracion) : IInvitacionClienteService
{
    public const string EstadoPendienteActivacion = "PendienteActivacion";
    private static readonly TimeSpan VigenciaInvitacion = TimeSpan.FromDays(7);
    private static readonly TimeSpan EsperaEntreEnvios = TimeSpan.FromMinutes(1);

    public async Task<ResultadoInvitacionCliente> InvitarAsync(int idCliente, CancellationToken cancellationToken)
    {
        var cliente = await db.Clientes.SingleOrDefaultAsync(c => c.IdCliente == idCliente, cancellationToken);
        if (cliente is null) return ResultadoInvitacionCliente.ClienteNoEncontrado;
        if (cliente.Estado != "Activo") return ResultadoInvitacionCliente.ClienteInactivo;

        var correo = cliente.Correo?.Trim();
        if (string.IsNullOrEmpty(correo) || correo.Length > 100 || !MailAddress.TryCreate(correo, out _))
            return ResultadoInvitacionCliente.SinCorreo;
        var correoNormalizado = AutenticacionClienteService.NormalizarCorreo(correo);

        var cuenta = await db.CuentasCliente.SingleOrDefaultAsync(c => c.IdCliente == idCliente, cancellationToken);
        if (cuenta is not null && cuenta.Estado != EstadoPendienteActivacion)
            return ResultadoInvitacionCliente.CuentaExistente;
        if (await db.CuentasCliente.AnyAsync(c => c.IdCliente != idCliente && c.CorreoNormalizado == correoNormalizado, cancellationToken))
            return ResultadoInvitacionCliente.CorreoEnUso;

        var ahora = proveedorTiempo.GetUtcNow().UtcDateTime;
        if (cuenta?.UltimoCorreoConfirmacionUtc is { } ultimo && ahora - ultimo < EsperaEntreEnvios)
            return ResultadoInvitacionCliente.Espera;

        if (cuenta is null)
        {
            cuenta = new CuentaCliente
            {
                IdCliente = cliente.IdCliente,
                Estado = EstadoPendienteActivacion,
                // Sin contraseña: un hash vacío nunca valida y el estado impide iniciar sesión.
                ContrasenaHash = string.Empty,
                FechaCreacion = ahora
            };
            db.CuentasCliente.Add(cuenta);
        }

        // Si el personal corrigió el correo del cliente, la invitación se envía al correo actual.
        cuenta.Correo = correo;
        cuenta.CorreoNormalizado = correoNormalizado;
        var token = AutenticacionClienteService.CrearTokenConfirmacion();
        cuenta.TokenConfirmacionHash = AutenticacionClienteService.HashToken(token);
        cuenta.TokenConfirmacionExpiraUtc = ahora.Add(VigenciaInvitacion);
        cuenta.UltimoCorreoConfirmacionUtc = ahora;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (AutenticacionClienteService.EsDuplicadoMySql(ex))
        {
            db.ChangeTracker.Clear();
            return ResultadoInvitacionCliente.CorreoEnUso;
        }

        var cuerpoHtml = AutenticacionClienteService.CrearCuerpoCorreoCuenta(
            cliente.Nombre,
            AutenticacionClienteService.CrearEnlaceFrontend(configuracion, "cliente/activar-cuenta", token),
            titulo: "Activa tu cuenta",
            introduccion: $"registramos tus datos en {AutenticacionClienteService.NombreCentro}. Crea tu contraseña para consultar tus pedidos, recetas y reservas en línea.",
            textoBoton: "Crear mi contraseña",
            vigencia: "Este enlace vence en 7 días y solo puede utilizarse una vez.",
            aviso: "Si no reconoces este registro, puedes ignorar este mensaje.");
        var envio = await emailService.EnviarConResultadoAsync(
            correo, $"Activa tu cuenta - {AutenticacionClienteService.NombreCentro}", cuerpoHtml, cancellationToken, importante: true);
        if (envio.Exitoso) return ResultadoInvitacionCliente.Enviada;

        logger.LogWarning("No se pudo enviar la invitación de la cuenta del cliente {ClienteId}.", cliente.IdCliente);
        return ResultadoInvitacionCliente.ErrorEnvio;
    }

    public async Task<ResultadoActivacionCliente> ActivarAsync(
        string token, string nuevaContrasena, string confirmarContrasena, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return ResultadoActivacionCliente.EnlaceInvalido;
        if (!AutenticacionClienteService.ContrasenaValida(nuevaContrasena ?? string.Empty))
            return ResultadoActivacionCliente.ContrasenaInvalida;
        if (!string.Equals(nuevaContrasena, confirmarContrasena, StringComparison.Ordinal))
            return ResultadoActivacionCliente.ContrasenasNoCoinciden;

        var hashToken = AutenticacionClienteService.HashToken(token);
        var ahora = proveedorTiempo.GetUtcNow().UtcDateTime;
        var cuenta = await db.CuentasCliente.Include(c => c.Cliente).SingleOrDefaultAsync(c =>
            c.TokenConfirmacionHash == hashToken
                && c.Estado == EstadoPendienteActivacion
                && c.TokenConfirmacionExpiraUtc > ahora, cancellationToken);
        if (cuenta is null || cuenta.Cliente?.Estado != "Activo")
            return ResultadoActivacionCliente.EnlaceInvalido;

        var contrasenaHash = passwordHasher.HashPassword(cuenta, nuevaContrasena!);

        if (db.Database.IsRelational())
        {
            // Actualización condicionada al token: si dos solicitudes usan el mismo enlace, solo una gana.
            var actualizadas = await db.CuentasCliente
                .Where(c => c.IdCuenta == cuenta.IdCuenta
                    && c.TokenConfirmacionHash == hashToken
                    && c.Estado == EstadoPendienteActivacion)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(c => c.ContrasenaHash, contrasenaHash)
                    .SetProperty(c => c.Estado, "Activa")
                    .SetProperty(c => c.TokenConfirmacionHash, (string?)null)
                    .SetProperty(c => c.TokenConfirmacionExpiraUtc, (DateTime?)null)
                    .SetProperty(c => c.UltimoCorreoConfirmacionUtc, (DateTime?)null), cancellationToken);
            return actualizadas == 1 ? ResultadoActivacionCliente.Activada : ResultadoActivacionCliente.EnlaceInvalido;
        }

        cuenta.ContrasenaHash = contrasenaHash;
        cuenta.Estado = "Activa";
        cuenta.TokenConfirmacionHash = null;
        cuenta.TokenConfirmacionExpiraUtc = null;
        cuenta.UltimoCorreoConfirmacionUtc = null;
        await db.SaveChangesAsync(cancellationToken);
        return ResultadoActivacionCliente.Activada;
    }
}
