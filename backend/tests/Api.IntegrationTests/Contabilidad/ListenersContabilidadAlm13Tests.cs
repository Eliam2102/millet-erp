using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Integration;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// F1-ALM-13 (Contabilidad, Fernando Alejos 4-nov): la salida de almacén abona el inventario a la cuenta del tipo de artículo
/// (rol INVENTARIO_{grupo}); el artículo cuyo grupo no tiene rol con cuenta va a INVENTARIO. Es la misma regla del cuadre.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    [Fact]
    public async Task Alm13_la_salida_abona_el_inventario_a_la_cuenta_del_grupo_del_articulo()
    {
        Escenario? e = null;
        Guid? articulo = null;
        string? rol = null;
        try
        {
            e = await PrepararAsync();
            var grupo = $"FIX VIDRIO {e.Suf}";
            rol = "INVENTARIO_" + GenerarPolizaDesdeEventoHandler.Clasificador(grupo);
            Guid activo;
            using (var scope = factory.Services.CreateScope())
                activo = await scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>().Cuentas.IgnoreQueryFilters()
                    .Where(c => c.Codigo == Codigo(e.Suf, "1")).Select(c => c.Id).SingleAsync();
            var cuentaVidrio = Id(await CrearCuenta(e.Admin, Codigo(e.Suf, "1.20"), "FIX inventario vidrio", activo));
            var alta = await e.Admin.PostAsJsonAsync($"{Base}/motor/roles", new
            {
                Codigo = rol, Descripcion = "FIX inventario del grupo", CuentaId = cuentaVidrio, SucursalId = (Guid?)null,
                Moneda = (string?)null, VigenteDesde = new DateOnly(2000, 1, 1),
            });
            Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
            articulo = Guid.NewGuid();
            using (var scope = factory.Services.CreateScope())
            {
                var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                compartido.Articulos.Add(new Articulo(articulo.Value, $"FIX-A13-{e.Suf}", "FIX vidrio", "PZA", categoria: grupo));
                await compartido.SaveChangesAsync();
            }

            var salida = Guid.NewGuid();
            await Almacen(new SalidaRequisicionRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, salida, "SAL-FIX-A13", Hoy(), null, false, null, null,
                [
                    new LineaSalidaPayload(Guid.NewGuid(), articulo.Value, "PZA", 8m, 100m, 800m, e.CentroCosto, null),
                    new LineaSalidaPayload(Guid.NewGuid(), Guid.NewGuid(), "PZA", 2m, 100m, 200m, e.CentroCosto, null),
                ],
                SucursalId: e.Sucursal));

            var sm = (await PolizasAsync(salida)).Should().ContainSingle().Subject;
            sm.Partidas.Should().ContainSingle(x => x.CuentaId == cuentaVidrio).Which.Abono.Should().Be(800m);
            sm.Partidas.Should().ContainSingle(x => x.CuentaId == e.Cuentas["INVENTARIO"]).Which.Abono.Should().Be(200m);
        }
        finally
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                if (rol is not null)
                {
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.roles_cuenta_cambios WHERE rol_cuenta_id IN (SELECT id FROM contabilidad.roles_cuenta WHERE codigo = {0})", rol);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.roles_cuenta WHERE codigo = {0}", rol);
                }
                if (articulo is { } a)
                    await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Database
                        .ExecuteSqlRawAsync("DELETE FROM compartido.articulos WHERE id = {0}", a);
            }
            await LimpiarAsync(e);
        }
    }
}
