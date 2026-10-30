using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.P6;

// 86e3x4g77: saldo del grupo de clientes unificados. Exige cartera.leer y respeta el alcance de documentos por sucursal.
public sealed partial class P6SucursalEndpointsTests
{
    private static string RutaSaldoGrupo(Guid clienteId) => $"/api/v1/cuentas-por-cobrar/cartera/saldo-grupo/{clienteId}";

    private async Task<Guid> SembrarGrupoAsync(Guid principalId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var principal = new Cliente(principalId, $"P6G-{sufijo}-A", "Principal P6", OrigenMaster.Aw, referenciaExterna: $"P6G-{sufijo}-A");
        var secundario = new Cliente(Guid.NewGuid(), $"P6G-{sufijo}-B", "Secundario P6", OrigenMaster.Aw, referenciaExterna: $"P6G-{sufijo}-B");
        secundario.UnificarEn(principal, conHistorial: true, "Grupo de prueba P6", "p6", DateTimeOffset.UtcNow);
        db.Clientes.AddRange(principal, secundario);
        await db.SaveChangesAsync();
        return secundario.Id;
    }

    private async Task LimpiarGrupoAsync(Guid principalId, Guid secundarioId)
    {
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        await db.Clientes.Where(c => c.Id == secundarioId).ExecuteDeleteAsync();
        await db.Clientes.Where(c => c.Id == principalId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Saldo_grupo_sin_cartera_leer_es_403()
    {
        await using var datos = await PrepararAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var permisoId = await db.Permisos.Where(p => p.Codigo == PermisosCanonicos.CuentasPorCobrarCarteraLeer).Select(p => p.Id).SingleAsync();
            db.RolPermisos.RemoveRange(await db.RolPermisos.Where(p => p.RolId == datos.RolId && p.PermisoId == permisoId).ToListAsync());
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionCache>().InvalidateAllForUserAsync(datos.UsuarioId);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await datos.Operativo.GetAsync(RutaSaldoGrupo(datos.ClienteId))).StatusCode);
    }

    [Fact]
    public async Task Saldo_grupo_solo_suma_los_documentos_de_la_sucursal_del_usuario_y_el_corporativo_ve_todos()
    {
        await using var datos = await PrepararAsync();
        var secundarioId = await SembrarGrupoAsync(datos.ClienteId);
        try
        {
            async Task<(int Facturas, decimal Total)> Leer(HttpClient http)
            {
                var json = JsonDocument.Parse(await http.GetStringAsync(RutaSaldoGrupo(datos.ClienteId))).RootElement;
                Assert.Equal([datos.ClienteId, secundarioId], json.GetProperty("miembros").EnumerateArray().Select(m => m.GetProperty("clienteId").GetGuid()));
                var monedas = json.GetProperty("miembros")[0].GetProperty("monedas").EnumerateArray().ToList();
                return (monedas.Sum(m => m.GetProperty("facturasAbiertas").GetInt32()), monedas.Sum(m => m.GetProperty("saldoNeto").GetDecimal()));
            }

            // Cada sucursal sembró una factura de 116 MXN al principal del grupo.
            Assert.Equal((1, 116m), await Leer(datos.Operativo));
            Assert.Equal((2, 232m), await Leer(datos.Corporativo));
        }
        finally { await LimpiarGrupoAsync(datos.ClienteId, secundarioId); }
    }
}
