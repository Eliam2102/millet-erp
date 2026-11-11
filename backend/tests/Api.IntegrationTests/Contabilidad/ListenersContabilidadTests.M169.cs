using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.EventListeners;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Contabilidad.Infrastructure.Persistence.Migrations;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// M-169 (Contabilidad, 10-nov; Laura Cerón vía Guillermo Pantoja) · póliza del costo de importación por grupo contable con la
/// plantilla y las cuentas de la migración <c>M169GastoImportacionPorGrupo</c>: honorarios aduanales a 601.73.01.00 (personas
/// físicas) o 601.73.02.00 (personas morales), contrapartida a 201.07.00.00 GASTOS ADUANALES POR APLICAR. Las cuentas del catálogo
/// del 5-nov se dan de alta aquí porque la base de pruebas no trae el catálogo de Millet; la subcuenta de fletes y maniobras todavía
/// no existe en el catálogo, así que el evento va a Rechazos («Rol sin cuenta») y, al asignarle una cuenta FIX, se reprocesa sin
/// duplicar. El evento es el record de Almacén serializado como el outbox.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private static readonly (string Codigo, string Nombre, short Naturaleza, short Nivel)[] CuentasM169 =
    [
        ("201.07.00.00", "GASTOS ADUANALES POR APLICAR", 1, 2),
        ("601.73.01.00", "HONORARIOS ADUANALES PERSONAS FÍSICAS", 0, 3),
        ("601.73.02.00", "HONORARIOS ADUANALES PERSONAS MORALES", 0, 3),
    ];

    private static readonly string[] RolesM169 =
    [
        "GASTOS_IMPORTACION_POR_APLICAR", "GASTO_IMPORTACION_HONORARIOS_PF", "GASTO_IMPORTACION_HONORARIOS_PM",
        "GASTO_IMPORTACION_FLETES_MANIOBRAS", "GASTO_IMPORTACION_ALMACENAJES", "GASTO_IMPORTACION_OTROS",
    ];

    [Fact]
    public async Task M169_honorarios_y_flete_a_su_cuenta_contra_201_07_y_sin_cuenta_de_fletes_a_rechazos_y_reproceso_sin_duplicar()
    {
        Escenario? e = null;
        var previo = new Dictionary<string, Guid?>();
        var creadas = new List<string>();
        try
        {
            e = await PrepararAsync();
            Dictionary<string, Guid> cuenta;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                foreach (var r in await db.RolesCuenta.IgnoreQueryFilters().AsNoTracking()
                             .Where(r => r.EmpresaId == EmpresaBootstrapId && RolesM169.Contains(r.Codigo) && r.SucursalId == null && r.Moneda == null).ToListAsync())
                    previo[r.Codigo] = r.CuentaId;
                previo.Keys.Should().BeEquivalentTo(RolesM169, "la migración crea los roles de cada grupo");

                foreach (var (codigo, nombre, naturaleza, nivel) in CuentasM169)
                    if (await db.Database.ExecuteSqlRawAsync("""
                            INSERT INTO contabilidad.cuentas_contables
                                (id, empresa_id, codigo, nombre, padre_id, nivel, naturaleza, tipo, estatus, cuenta_control, no_afectable_manual,
                                 codigo_agrupador, grupo_reporte, clase, rubro_id, version, created_at, updated_at, created_by, updated_by)
                            VALUES (gen_random_uuid(), {0}, {1}, {2}, NULL, {3}, {4}, 1, 0, 0, false, NULL, NULL, 0, NULL, 1, now(), now(), 'fix-M169', 'fix-M169')
                            ON CONFLICT (empresa_id, codigo) DO NOTHING
                            """, EmpresaBootstrapId, codigo, nombre, nivel, naturaleza) == 1)
                        creadas.Add(codigo);
                var codigos = CuentasM169.Select(c => c.Codigo).ToArray();
                cuenta = await db.Cuentas.IgnoreQueryFilters().AsNoTracking().Where(c => c.EmpresaId == EmpresaBootstrapId && codigos.Contains(c.Codigo))
                    .ToDictionaryAsync(c => c.Codigo, c => c.Id);

                // Roles base sin cuenta y la misma asignación que corre la migración.
                await db.Database.ExecuteSqlRawAsync("""
                    UPDATE contabilidad.roles_cuenta SET cuenta_id = NULL
                     WHERE empresa_id = {0} AND codigo = ANY({1}) AND sucursal_id IS NULL AND moneda IS NULL
                    """, EmpresaBootstrapId, RolesM169);
                await db.Database.ExecuteSqlRawAsync(M169GastoImportacionPorGrupo.AsignarCuentasSql);
                var asignadas = await db.RolesCuenta.IgnoreQueryFilters().AsNoTracking()
                    .Where(r => r.EmpresaId == EmpresaBootstrapId && RolesM169.Contains(r.Codigo) && r.SucursalId == null && r.Moneda == null)
                    .ToDictionaryAsync(r => r.Codigo, r => r.CuentaId);
                asignadas["GASTOS_IMPORTACION_POR_APLICAR"].Should().Be(cuenta["201.07.00.00"]);
                asignadas["GASTO_IMPORTACION_HONORARIOS_PF"].Should().Be(cuenta["601.73.01.00"]);
                asignadas["GASTO_IMPORTACION_HONORARIOS_PM"].Should().Be(cuenta["601.73.02.00"]);
                asignadas["GASTO_IMPORTACION_FLETES_MANIOBRAS"].Should().BeNull("Millet abrirá la subcuenta durante las pruebas");
                asignadas["GASTO_IMPORTACION_ALMACENAJES"].Should().BeNull();
                asignadas["GASTO_IMPORTACION_OTROS"].Should().BeNull();
            }

            // Honorarios del agente (persona moral) $12,000 y flete $8,000: sin cuenta de fletes, a Rechazos y sin póliza.
            var expediente = Guid.NewGuid();
            var aplicado = new ExpedienteCostoAplicadoEvent(EmpresaBootstrapId, Ahora, expediente, 1,
                [new CostoPartidaAplicado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 20_000m, 0m, 20_000m)])
            {
                SucursalId = e.Sucursal,
                GastosPorGrupo = [new("HONORARIOS_PM", 12_000m), new("FLETES_MANIOBRAS", 8_000m)],
            };
            var evento = await Almacen(aplicado);
            var rechazo = await RechazoAsync(evento);
            rechazo.Should().NotBeNull();
            rechazo!.Causa.Should().Be(CausaRechazoContable.RolSinCuenta);
            rechazo.Detalle.Should().Be("GASTO_IMPORTACION_FLETES_MANIOBRAS");
            (await PolizasAsync(expediente)).Should().BeEmpty();

            // Contabilidad asigna la cuenta y reprocesa: nace una sola póliza.
            Guid fletes;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                var gastos = await db.Cuentas.IgnoreQueryFilters().Where(c => c.Codigo == Codigo(e.Suf, "5")).Select(c => c.Id).SingleAsync();
                fletes = Id(await CrearCuenta(e.Admin, Codigo(e.Suf, "5.20"), "FIX fletes y maniobras de importación", gastos));
                await db.Database.ExecuteSqlRawAsync("""
                    UPDATE contabilidad.roles_cuenta SET cuenta_id = {0}
                     WHERE empresa_id = {1} AND codigo = 'GASTO_IMPORTACION_FLETES_MANIOBRAS' AND sucursal_id IS NULL AND moneda IS NULL
                    """, fletes, EmpresaBootstrapId);
            }
            (await e.Admin.PostAsync($"/api/v1/contabilidad/motor/rechazos/{rechazo.Id}/reprocesar", null)).EnsureSuccessStatusCode();

            var ci = (await PolizasAsync(expediente)).Should().ContainSingle().Subject;
            ci.Partidas.Should().HaveCount(3, "sin partida de inventario ni líneas en 0");
            ci.Partidas.Should().Contain(x => x.CuentaId == cuenta["601.73.02.00"] && x.Cargo == 12_000m && x.Abono == 0m);
            ci.Partidas.Should().Contain(x => x.CuentaId == fletes && x.Cargo == 8_000m && x.Abono == 0m);
            ci.Partidas.Should().Contain(x => x.CuentaId == cuenta["201.07.00.00"] && x.Abono == 20_000m && x.Cargo == 0m);
            (await RechazoAsync(evento))!.Estado.Should().Be(EstadoRechazoContable.Reprocesado);

            // Reentrega del mismo evento y segundo reproceso: sigue una sola póliza.
            await Almacen(aplicado, evento);
            (await e.Admin.PostAsync($"/api/v1/contabilidad/motor/rechazos/{rechazo.Id}/reprocesar", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await PolizasAsync(expediente)).Should().ContainSingle();

            // Honorarios de un agente persona física: a 601.73.01.00.
            var otro = Guid.NewGuid();
            await Almacen(new ExpedienteCostoAplicadoEvent(EmpresaBootstrapId, Ahora, otro, 1,
                [new CostoPartidaAplicado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5_000m, 0m, 5_000m)])
            { SucursalId = e.Sucursal, GastosPorGrupo = [new("HONORARIOS_PF", 5_000m)] });
            var pf = (await PolizasAsync(otro)).Should().ContainSingle().Subject;
            pf.Partidas.Should().HaveCount(2);
            pf.Partidas.Should().Contain(x => x.CuentaId == cuenta["601.73.01.00"] && x.Cargo == 5_000m);
            pf.Partidas.Should().Contain(x => x.CuentaId == cuenta["201.07.00.00"] && x.Abono == 5_000m);
        }
        finally
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                foreach (var (codigo, anterior) in previo)
                    await db.Database.ExecuteSqlRawAsync("""
                        UPDATE contabilidad.roles_cuenta SET cuenta_id = {0}
                         WHERE empresa_id = {1} AND codigo = {2} AND sucursal_id IS NULL AND moneda IS NULL
                        """, anterior!, EmpresaBootstrapId, codigo);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.roles_cuenta_cambios WHERE usuario = 'migration-M169' AND empresa_id = {0}", EmpresaBootstrapId);
            }
            await LimpiarAsync(e);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
                foreach (var codigo in creadas)
                {
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE empresa_id = {0} AND codigo = {1})", EmpresaBootstrapId, codigo);
                    await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables WHERE empresa_id = {0} AND codigo = {1} AND created_by = 'fix-M169'", EmpresaBootstrapId, codigo);
                }
            }
        }
    }
}
