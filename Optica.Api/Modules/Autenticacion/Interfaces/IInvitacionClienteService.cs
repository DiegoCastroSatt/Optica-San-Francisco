namespace Optica.Api.Modules.Autenticacion.Interfaces;

// Invitación para que un cliente creado por el personal defina su contraseña y active su cuenta.
public interface IInvitacionClienteService
{
    Task<ResultadoInvitacionCliente> InvitarAsync(int idCliente, CancellationToken cancellationToken);

    Task<ResultadoActivacionCliente> ActivarAsync(
        string token, string nuevaContrasena, string confirmarContrasena, CancellationToken cancellationToken);
}

public enum ResultadoInvitacionCliente
{
    Enviada,
    ClienteNoEncontrado,
    ClienteInactivo,
    SinCorreo,
    CuentaExistente,
    CorreoEnUso,
    Espera,
    ErrorEnvio
}

public enum ResultadoActivacionCliente
{
    Activada,
    EnlaceInvalido,
    ContrasenaInvalida,
    ContrasenasNoCoinciden
}
