using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Optica.Api.Modules.Accesos;
using Optica.Api.Modules.Autenticacion.Controllers;
using Optica.Api.Modules.Pedidos.Controllers;
using Xunit;

namespace Optica.Tests;

public class TestPoliticasPedidos
{
    [Theory]
    [InlineData(nameof(PedidosController.ObtenerMisPedidos), "cliente", false, true)]
    [InlineData(nameof(PedidosController.ObtenerMisPedidos), "Trabajador", true, false)]
    [InlineData(nameof(PedidosController.ObtenerMisPedidos), "Trabajador", false, false)]
    [InlineData(nameof(PedidosController.ObtenerMisPedidos), "Administrador", false, false)]
    [InlineData(nameof(PedidosController.GetPedidosPorRut), "cliente", false, false)]
    [InlineData(nameof(PedidosController.GetPedidos), "cliente", false, false)]
    [InlineData(nameof(PedidosController.GetPedidoById), "cliente", false, false)]
    [InlineData(nameof(PedidosController.GetPedidosPorRut), "Trabajador", true, true)]
    [InlineData(nameof(PedidosController.GetPedidosPorRut), "Trabajador", false, false)]
    [InlineData(nameof(PedidosController.GetPedidosPorRut), "Administrador", false, true)]
    public async Task CombinaLasPoliticasSinPermitirAlClienteAccederARutasDePersonal(
        string accion, string tipoUsuario, bool permisoPedidos, bool esperadoPermitido)
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddAuthorization(options =>
        {
            PermisosAcceso.Configurar(options);
            options.AddPolicy(AutenticacionClientesController.Politica, p => p
                .AddAuthenticationSchemes(AutenticacionClientesController.Esquema)
                .RequireAuthenticatedUser().RequireClaim(AutenticacionClientesController.ClaimTipo, "cliente"));
        });
        using var provider = servicios.BuildServiceProvider();

        var metodo = typeof(PedidosController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .First(m => m.Name == accion);

        var atributos = typeof(PedidosController).GetCustomAttributes<AuthorizeAttribute>()
            .Concat(metodo.GetCustomAttributes<AuthorizeAttribute>());
        var politica = await AuthorizationPolicy.CombineAsync(provider.GetRequiredService<IAuthorizationPolicyProvider>(), atributos);
        Assert.NotNull(politica);

        var claims = new List<Claim> { new(PermisosAcceso.Tipo, tipoUsuario) };
        if (permisoPedidos)
        {
            claims.Add(new Claim(PermisosAcceso.Permiso, "pedidos"));
        }
        var usuario = new ClaimsPrincipal(new ClaimsIdentity(claims, "Prueba"));
        var resultado = await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(usuario, null, politica!);
        Assert.Equal(esperadoPermitido, resultado.Succeeded);
    }
}
