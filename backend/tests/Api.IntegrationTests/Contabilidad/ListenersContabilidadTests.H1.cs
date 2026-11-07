using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.EventListeners;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Movimientos;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// H1 pasos 3 y 4 (86e3z5w25): el área (o el equipo) que el usuario captura en la alta desde el extracto (Tesorería) y al distribuir
/// el costo de un expediente (Comercio Exterior) viaja en el evento y la póliza la lleva en la línea que carga a gasto; sin ella la
/// póliza queda en Rechazos con «Dato faltante». Regla C-03 de prueba (ubicación y área) sobre la cuenta padre de gastos del
/// escenario; se borra al final. Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    [Fact]
    public async Task H1_pasos3y4_el_area_capturada_en_bancos_y_en_importacion_llega_a_la_poliza()
    {
        Escenario? e = null;
        Guid? gastos = null;
        try
        {
            e = await PrepararAsync();
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                gastos = await db.Cuentas.IgnoreQueryFilters().Where(c => c.Id == e.Cuentas["GASTO_IMPORTACION"]).Select(c => c.PadreId).SingleAsync();
            }
            foreach (var dim in new[] { "Dim1", "Dim2" })
                (await e.Admin.PostAsJsonAsync($"{Base}/reglas-dimension", new
                {
                    cuentaId = gastos, tipoDocumentoId = (Guid?)null, dimension = dim, requerimiento = "Obligatorio",
                    vigenteDesde = Hoy(), vigenteHasta = (DateOnly?)null, esPrueba = true, nota = "FIX regla C-03 (H1 pasos 3 y 4)",
                })).StatusCode.Should().Be(HttpStatusCode.Created);

            // El puerto: qué exige cada cuenta, qué centros ofrece y cómo valida lo capturado.
            Guid area;
            using (var scope = factory.Services.CreateScope())
            {
                var centros = scope.ServiceProvider.GetRequiredService<ICentroCostoContabilidadPort>();
                area = (await centros.ObtenerAsync([e.CentroCosto], default))[e.CentroCosto].Dim2Id!.Value;
                var puerto = scope.ServiceProvider.GetRequiredService<IDimensionesCapturaPort>();
                var bancos = new MovimientoCaptura(EmpresaBootstrapId, e.Sucursal, MovimientoBancarioRegistradoIntegrationEvent.WireEventType, "Base",
                    "Comisión bancaria", Hoy());
                var importacion = new MovimientoCaptura(EmpresaBootstrapId, e.Sucursal, "almacen.expediente.costo-aplicado.v1", "GastoImportacion",
                    null, Hoy());

                (await puerto.ExigenciasAsync(bancos, default)).Should().Match<ExigenciaDimensiones>(x => x.ExigeArea && !x.ExigeEquipo);
                (await puerto.ExigenciasAsync(importacion, default)).Should().Match<ExigenciaDimensiones>(x => x.ExigeArea && !x.ExigeEquipo);
                (await puerto.CentrosAsync(EmpresaBootstrapId, e.Sucursal, false, null, null, 200, default)).Should().Contain(c => c.Id == area);
                (await puerto.CentrosAsync(EmpresaBootstrapId, e.Sucursal, true, area, null, 200, default)).Should().Contain(c => c.Id == e.CentroCosto);

                var sinNada = await puerto.ValidarAsync(bancos, null, null, default);
                sinNada.Should().Match<DimensionesCapturaResultado>(x => x.Error == null && x.CentroCostoId == null && x.Faltante!.Contains("el área"));
                (await puerto.ValidarAsync(bancos, area, null, default)).Should().Match<DimensionesCapturaResultado>(x =>
                    x.Error == null && x.Faltante == null && x.CentroCostoId == area);
                (await puerto.ValidarAsync(importacion, area, e.CentroCosto, default)).Should().Match<DimensionesCapturaResultado>(x =>
                    x.Error == null && x.Faltante == null && x.CentroCostoId == e.CentroCosto);
                (await puerto.ValidarAsync(bancos, Guid.NewGuid(), null, default)).Error.Should().NotBeNull();
                (await puerto.ValidarAsync(bancos, e.CentroCosto, null, default)).Error.Should().NotBeNull("un equipo no es un área");
            }

            // Paso 3 · comisión con IVA: con el área, la póliza nace y solo la línea del concepto lleva dimensiones.
            Guid conArea = Guid.NewGuid(), sinArea = Guid.NewGuid();
            var eventoConArea = await Tesoreria(new MovimientoBancarioRegistradoIntegrationEvent(EmpresaBootstrapId, Ahora, conArea, Guid.NewGuid(),
                SentidoMovimiento.Egreso, 100m, "MXN", Hoy(), "EXT-FIX-H1", Guid.NewGuid(), "Comisión bancaria", 16m, SucursalId: e.Sucursal,
                CentroCostoId: area));
            (await RechazoAsync(eventoConArea)).Should().BeNull();
            var mb = (await PolizasAsync(conArea)).Should().ContainSingle().Subject;
            mb.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["CONCEPTO_BANCARIO_COMISION_BANCARIA"] && x.Cargo == 100m
                && x.Dim2Id == area && x.Dim1Id != null);
            mb.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["BANCOS"] && x.Abono == 116m && x.Dim2Id == null);

            // Sin el área: Rechazos con «Dato faltante», como hasta hoy.
            var eventoSinArea = await Tesoreria(new MovimientoBancarioRegistradoIntegrationEvent(EmpresaBootstrapId, Ahora, sinArea, Guid.NewGuid(),
                SentidoMovimiento.Egreso, 100m, "MXN", Hoy(), "EXT-FIX-H1B", Guid.NewGuid(), "Comisión bancaria", 16m, SucursalId: e.Sucursal));
            (await PolizasAsync(sinArea)).Should().BeEmpty();
            (await RechazoAsync(eventoSinArea))!.Causa.Should().Be(CausaRechazoContable.DatoFaltante);

            // Paso 4 · costo de importación: el equipo capturado al distribuir va en el cargo a gasto de importación (la ubicación se deriva).
            var expediente = Guid.NewGuid();
            var eventoCosto = await Almacen(new ExpedienteCostoAplicadoEvent(EmpresaBootstrapId, Ahora, expediente, 1,
                [new CostoPartidaAplicado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10_000m, 8_000m, 2_000m)])
                { SucursalId = e.Sucursal, CentroCostoId = e.CentroCosto });
            (await RechazoAsync(eventoCosto)).Should().BeNull();
            var ci = (await PolizasAsync(expediente)).Should().ContainSingle().Subject;
            ci.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["GASTO_IMPORTACION"] && x.Cargo == 2_000m
                && x.Dim3Id == e.CentroCosto && x.Dim1Id != null);
            ci.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["INVENTARIO"] && x.Cargo == 8_000m && x.Dim2Id == null);
        }
        finally
        {
            if (gastos is { } padre)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE regla_id IN (SELECT id FROM contabilidad.reglas_dimension WHERE cuenta_id = {0})", padre);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension WHERE cuenta_id = {0}", padre);
            }
            await LimpiarAsync(e);
        }
    }
}
