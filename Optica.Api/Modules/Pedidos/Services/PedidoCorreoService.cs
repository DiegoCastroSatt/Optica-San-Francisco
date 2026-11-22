using Optica.Api.Modules.Notificaciones.Interfaces;
using Optica.Api.Modules.Pedidos.Interfaces;
using Optica.Api.Modules.Pedidos.Models;
using System.Globalization;
using System.Text.Encodings.Web;

namespace Optica.Api.Modules.Pedidos.Services;

public sealed class PedidoCorreoService(IEmailService emailService) : IPedidoCorreoService
{
    private const string NombreCentro = "Centro Óptico San Francisco";
    private static readonly CultureInfo CulturaChile = CultureInfo.GetCultureInfo("es-CL");

    public async Task<bool> EnviarPedidoListoAsync(string? destinatario, string nombreCliente, Pedido pedido, CancellationToken cancellationToken)
    {
        var correo = PrepararPedidoListo(destinatario, nombreCliente, pedido);
        return (await emailService.EnviarConResultadoAsync(correo.Destinatario, correo.Asunto, correo.CuerpoHtml,
            cancellationToken, correo.Importante)).Exitoso;
    }

    public CorreoPedidoPendiente PrepararPedidoListo(string? destinatario, string nombreCliente, Pedido pedido)
    {
        destinatario ??= string.Empty;
        var nombre = HtmlEncoder.Default.Encode(nombreCliente);
        var numeroPedido = HtmlEncoder.Default.Encode(pedido.IdPedido.ToString(CultureInfo.InvariantCulture));
        var estado = HtmlEncoder.Default.Encode(DescribirEstado(pedido.Estado));
        var fechaEncargo = HtmlEncoder.Default.Encode(pedido.Fecha.ToString("dd/MM/yyyy", CulturaChile));
        // El monto se expresa en pesos chilenos, sin decimales.
        var monto = HtmlEncoder.Default.Encode("$" + pedido.Total.ToString("N0", CulturaChile));
        var filaEntrega = pedido.FechaEntregaEstimada is { } entrega
            ? FilaDetalle("Entrega estimada", HtmlEncoder.Default.Encode(entrega.ToString("dd/MM/yyyy", CulturaChile)))
            : string.Empty;
        var html = $"""
            <!doctype html><html lang="es"><head><meta charset="utf-8"></head><body style="margin:0;background:#f3f7f6;font-family:Arial,sans-serif;color:#18333c">
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:28px 12px"><tr><td align="center">
            <table role="presentation" width="600" cellspacing="0" cellpadding="0" style="max-width:600px;background:#fff;border-radius:14px;padding:32px">
            <tr><td><p style="margin:0 0 8px;color:#176b6c;font-weight:bold">{NombreCentro}</p>
            <h1 style="font-size:24px">¡Tu pedido está listo para retirar!</h1>
            <p>Hola, {nombre}. Ya puedes retirar tu pedido en nuestro centro óptico.</p>
            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f3f8f6;border-radius:8px;padding:8px 16px;margin:16px 0">
            {FilaDetalle("Pedido", "#" + numeroPedido)}
            {FilaDetalle("Estado", estado)}
            {FilaDetalle("Fecha de encargo", fechaEncargo)}
            {filaEntrega}
            {FilaDetalle("Monto total", monto)}
            </table>
            <p>Te esperamos en {NombreCentro}.</p>
            <p style="color:#63777c">Si tienes dudas, puedes contactarnos directamente en el centro.</p>
            </td></tr></table></td></tr></table></body></html>
            """;
        return new CorreoPedidoPendiente
        {
            PedidoId = pedido.IdPedido,
            Destinatario = destinatario,
            Asunto = $"Pedido #{pedido.IdPedido.ToString(CultureInfo.InvariantCulture)} listo para retirar - {NombreCentro}",
            CuerpoHtml = html,
            Importante = true,
            CreadoUtc = DateTime.UtcNow,
            ProximoIntentoUtc = DateTime.UtcNow
        };
    }

    // Muestra el estado con el mismo texto que ve el cliente en la interfaz.
    private static string DescribirEstado(string estado) =>
        estado == "Listo" ? "Listo para retirar" : estado;

    // El valor recibido ya debe venir codificado para HTML.
    private static string FilaDetalle(string etiqueta, string valorCodificado) =>
        $"""<tr><td style="padding:6px 0;color:#63777c">{etiqueta}</td><td align="right" style="padding:6px 0;font-weight:bold">{valorCodificado}</td></tr>""";
}
