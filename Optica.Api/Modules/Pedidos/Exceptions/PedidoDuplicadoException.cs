using System;

namespace Optica.Api.Modules.Pedidos.Exceptions;

public class PedidoDuplicadoException : Exception
{
    public int IdPedidoExistente { get; }

    public PedidoDuplicadoException(int idPedidoExistente, string mensaje)
        : base(mensaje)
    {
        IdPedidoExistente = idPedidoExistente;
    }
}
