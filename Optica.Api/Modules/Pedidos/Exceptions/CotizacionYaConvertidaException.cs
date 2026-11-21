using System;

namespace Optica.Api.Modules.Pedidos.Exceptions;

public class CotizacionYaConvertidaException : Exception
{
    public int IdPedidoExistente { get; }

    public CotizacionYaConvertidaException(int idPedidoExistente, string mensaje)
        : base(mensaje)
    {
        IdPedidoExistente = idPedidoExistente;
    }
}
