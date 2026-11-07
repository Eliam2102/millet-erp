using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// H1 paso 2 (86e3z5w25): el área o equipo de un gasto de caja chica, viáticos o tarjeta llega a la póliza GC (la ubicación se
/// deriva); sin él, si la cuenta lo exige por C-03, la póliza queda en Rechazos. Datos FIX.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private static GastoRegistradoIntegrationEvent GastoFix(Guid sucursal, Guid documento, Guid? centro) => new(
        EmpresaBootstrapId, Ahora, GastoRegistradoIntegrationEvent.OrigenCajaChica, documento, documento, null, Hoy(), null, null, null,
        Deducible: true, ClasificacionNoDeducible: null, Subtotal: 100m, IvaTrasladado: 16m, IvaNoAcreditable: 0m, Total: 116m,
        Moneda: "MXN", ConceptoContable: "FIX papelería", SucursalId: sucursal, CentroCostoId: centro);

    [Fact]
    public async Task H1_gasto_con_area_de_la_sucursal_genera_su_poliza_y_sin_area_queda_en_rechazos()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            foreach (var dim in new[] { "Dim1", "Dim2" })
                (await e.Admin.PostAsJsonAsync($"{Base}/reglas-dimension", new
                {
                    cuentaId = e.Cuentas["GASTO"], tipoDocumentoId = (Guid?)null, dimension = dim, requerimiento = "Obligatorio",
                    vigenteDesde = Hoy(), vigenteHasta = (DateOnly?)null, esPrueba = true, nota = "FIX regla C-03 (H1 gastos)",
                })).StatusCode.Should().Be(HttpStatusCode.Created);

            Guid area;
            using (var scope = factory.Services.CreateScope())
            {
                var centros = scope.ServiceProvider.GetRequiredService<Millet.Contabilidad.Application.Ports.ICentroCostoContabilidadPort>();
                area = (await centros.ObtenerAsync([e.CentroCosto], default))[e.CentroCosto].Dim2Id!.Value;
                var puerto = scope.ServiceProvider.GetRequiredService<IDimensionesGastoPort>();

                (await puerto.ExigenciasAsync(EmpresaBootstrapId, e.Sucursal, Hoy(), default)).Should()
                    .Match<ExigenciaDimensionesGasto>(x => x.ExigeArea && !x.ExigeEquipo && x.CuentaCodigo != null);
                (await puerto.CentrosAsync(EmpresaBootstrapId, e.Sucursal, 2, null, null, 200, default)).Should().Contain(c => c.Id == area);
                (await puerto.CentrosAsync(EmpresaBootstrapId, e.Sucursal, 3, area, null, 200, default)).Should().Contain(c => c.Id == e.CentroCosto);
                (await puerto.ValidarCentroAsync(EmpresaBootstrapId, e.Sucursal, area, default)).Should().BeNull();
                (await puerto.ValidarCentroAsync(EmpresaBootstrapId, e.Sucursal, Guid.NewGuid(), default)).Should().NotBeNull();
            }

            // CxP: la pantalla pregunta con los permisos de captura, no con los de Contabilidad.
            var dims = await e.Admin.GetFromJsonAsync<Dictionary<string, object?>>(
                $"/api/v1/cuentas-por-pagar/gastos/dimensiones?sucursalId={e.Sucursal}");
            dims!["exigeArea"]!.ToString().Should().Be("True");
            (await e.Admin.GetStringAsync($"/api/v1/cuentas-por-pagar/gastos/centros?sucursalId={e.Sucursal}&nivel=2&limit=200"))
                .Should().Contain(area.ToString());

            // Con área: póliza con la ubicación derivada (Dim1) y el área (Dim2) en el cargo al gasto.
            var conArea = Guid.NewGuid();
            var evento = await Cxp(GastoFix(e.Sucursal, conArea, area));
            (await RechazoAsync(evento)).Should().BeNull();
            var gc = (await PolizasAsync(conArea)).Should().ContainSingle().Subject;
            gc.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["GASTO"] && x.Cargo == 100m && x.Dim2Id == area && x.Dim1Id != null);

            // Sin área: la cuenta la exige, la póliza no nace y el evento queda en Rechazos.
            var sinArea = Guid.NewGuid();
            var rechazado = await Cxp(GastoFix(e.Sucursal, sinArea, null));
            (await RechazoAsync(rechazado)).Should().NotBeNull();
            (await PolizasAsync(sinArea)).Should().BeEmpty();
        }
        finally
        {
            if (e is not null)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE regla_id IN (SELECT id FROM contabilidad.reglas_dimension WHERE cuenta_id = {0})", e.Cuentas["GASTO"]);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension WHERE cuenta_id = {0}", e.Cuentas["GASTO"]);
            }
            await LimpiarAsync(e);
        }
    }
}
