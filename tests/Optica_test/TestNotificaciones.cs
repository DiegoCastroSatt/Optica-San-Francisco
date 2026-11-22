using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Optica.Api.Modules.Notificaciones.Services;
using Optica.Api.Modules.Pedidos.Models;
using Optica.Api.Modules.Pedidos.Services;
using Xunit;

namespace Optica.Ventas.Tests;

public sealed class TestNotificaciones
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(11, 360)]
    [InlineData(25, 360)]
    public void PoliticaReintentos_UsaEsperaExponencialConTope(int intento, int minutos)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutos), PoliticaReintentosCorreo.ObtenerPausa(intento));
    }

    [Theory]
    [InlineData(null, "servidor.example.test", "remitente@example.test")]
    [InlineData("destinatario-invalido", "servidor.example.test", "remitente@example.test")]
    [InlineData("cliente@example.test", "", "remitente@example.test")]
    [InlineData("cliente@example.test", "servidor.example.test", "")]
    public async Task Smtp_NoEnviaCuandoFaltaDestinatarioOConfiguracion(
        string? destinatario, string smtpHost, string remitente)
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Correo:SmtpHost"] = smtpHost,
            ["Correo:Remitente"] = remitente
        }).Build();
        var servicio = new SmtpEmailService(
            configuracion,
            NullLogger<SmtpEmailService>.Instance,
            new FabricaHttpClientePrueba());

        var resultado = await servicio.EnviarConResultadoAsync(
            destinatario, "Asunto", "<p>Mensaje</p>", CancellationToken.None);

        Assert.False(resultado.Exitoso);
        Assert.NotNull(resultado.Error);
    }

    [Fact]
    public void PrepararPedidoListo_EscapaDatosDelClienteYMarcaNotificacionImportante()
    {
        var servicio = new PedidoCorreoService(new CorreoFalso());

        var correo = servicio.PrepararPedidoListo("ana@example.test", "<script>alert(1)</script>", new Pedido { IdPedido = 42, Estado = "Listo" });

        Assert.Equal("ana@example.test", correo.Destinatario);
        Assert.Contains("&lt;script&gt;", correo.CuerpoHtml);
        Assert.DoesNotContain("<script>", correo.CuerpoHtml);
        Assert.Contains("#42", correo.CuerpoHtml);
        Assert.True(correo.Importante);
        Assert.Equal(42, correo.PedidoId);
    }

    [Fact]
    public void PrepararPedidoListo_IncluyeEstadoFechaYMontoDelPedido()
    {
        var servicio = new PedidoCorreoService(new CorreoFalso());
        var pedido = new Pedido
        {
            IdPedido = 7,
            Estado = "Listo",
            Fecha = new DateTime(2026, 11, 3, 15, 30, 0),
            FechaEntregaEstimada = new DateTime(2026, 11, 20),
            Total = 129990m,
            Anotaciones = "Nota interna del taller"
        };

        var correo = servicio.PrepararPedidoListo("ana@example.test", "Ana Pérez", pedido);

        Assert.Contains("#7", correo.Asunto);
        Assert.Contains("#7", correo.CuerpoHtml);
        Assert.Contains("Listo para retirar", correo.CuerpoHtml);
        Assert.Contains("03-11-2026", correo.CuerpoHtml);
        Assert.Contains("20-11-2026", correo.CuerpoHtml);
        Assert.Contains("$129.990", correo.CuerpoHtml);
        Assert.DoesNotContain("Nota interna", correo.CuerpoHtml);
    }

    [Fact]
    public void PrepararPedidoListo_OmiteEntregaEstimadaCuandoNoExiste()
    {
        var servicio = new PedidoCorreoService(new CorreoFalso());

        var correo = servicio.PrepararPedidoListo("ana@example.test", "Ana", new Pedido { IdPedido = 8, Estado = "Listo" });

        Assert.DoesNotContain("Entrega estimada", correo.CuerpoHtml);
    }

    private sealed class CorreoFalso : Optica.Api.Modules.Notificaciones.Interfaces.IEmailService
    {
        public Task<bool> EnviarAsync(
            string? destinatario,
            string asunto,
            string cuerpoHtml,
            CancellationToken cancellationToken,
            bool importante = false,
            byte[]? adjunto = null,
            string? nombreAdjunto = null,
            string? tipoAdjunto = null) => Task.FromResult(true);
    }

    private sealed class FabricaHttpClientePrueba : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
