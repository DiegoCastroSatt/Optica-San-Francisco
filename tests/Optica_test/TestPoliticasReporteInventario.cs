using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Optica.Api.Modules.Accesos;
using Xunit;

namespace Optica.Tests;

public class TestPoliticasReporteInventario
{
    [Theory]
    [InlineData(PermisosAcceso.Administrador, false, true)]
    [InlineData(PermisosAcceso.Trabajador, true, false)]
    [InlineData(PermisosAcceso.Trabajador, false, false)]
    [InlineData("cliente", false, false)]
    public async Task ConsultarReporteInventario_SoloPermiteAdministradores(
        string tipoUsuario,
        bool tienePermisoInventario,
        bool permitido)
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddAuthorization(PermisosAcceso.Configurar);
        using var proveedor = servicios.BuildServiceProvider();

        var atributos = typeof(ProductosController).GetCustomAttributes<AuthorizeAttribute>()
            .Concat(typeof(ProductosController)
                .GetMethod(nameof(ProductosController.ConsultarReporteInventario))!
                .GetCustomAttributes<AuthorizeAttribute>());
        var politica = await AuthorizationPolicy.CombineAsync(
            proveedor.GetRequiredService<IAuthorizationPolicyProvider>(),
            atributos);
        Assert.NotNull(politica);

        var declaraciones = new List<Claim> { new(PermisosAcceso.Tipo, tipoUsuario) };
        if (tienePermisoInventario)
        {
            declaraciones.Add(new Claim(PermisosAcceso.Permiso, "inventario"));
        }

        var usuario = new ClaimsPrincipal(new ClaimsIdentity(declaraciones, "Prueba"));
        var resultado = await proveedor.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(usuario, null, politica);

        Assert.Equal(permitido, resultado.Succeeded);
    }
}
