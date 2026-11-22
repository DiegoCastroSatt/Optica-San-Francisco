namespace Optica.Api.Modules.Pedidos.Interfaces;

using Optica.Api.Modules.Pedidos.Models;

public interface IPedidoCorreoService
{
    Task<bool> EnviarPedidoListoAsync(string? destinatario, string nombreCliente, Pedido pedido, CancellationToken cancellationToken);

    CorreoPedidoPendiente PrepararPedidoListo(string? destinatario, string nombreCliente, Pedido pedido) =>
        throw new NotSupportedException("El servicio no prepara notificaciones persistentes.");
}
