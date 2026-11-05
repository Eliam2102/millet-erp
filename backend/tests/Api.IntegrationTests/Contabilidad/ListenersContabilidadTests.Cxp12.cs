using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// CXP-12 (86e3jvvad) · la revaluación de cierre de saldos por pagar en moneda extranjera con el motor contable real (plantillas
/// sembradas por la migración): pérdida o utilidad no realizada contra el proveedor y la reversa del día 1 con los lados contrarios.
/// El evento lo publica CxP al correr el cierre; aquí se entrega como lo haría el outbox. Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private async Task BorrarPolizasDeAsync(params Guid[] documentos)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        foreach (var d in documentos)
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.reglas_dimension_uso WHERE consumidor = 'POLIZA' AND referencia IN (SELECT id::text FROM contabilidad.polizas WHERE origen_documento_id = {0})", d);
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.polizas WHERE origen_documento_id = {0}", d);
        }
    }

    [Fact]
    public async Task CXP12_la_perdida_no_realizada_se_asienta_contra_el_proveedor_y_el_dia_1_se_revierte_con_los_lados_contrarios()
    {
        Escenario? e = null;
        var revaluacion = Guid.NewGuid();
        try
        {
            e = await PrepararAsync();
            await AsegurarPeriodosAbiertosAsync(e.Admin, Hoy(), Hoy().AddDays(1));

            // 1,000 USD registrados a 18.00 con TC de cierre 18.50: pérdida de $500 (CXP-12-a).
            await Cxp(new RevaluacionCierreRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, revaluacion, e.Proveedor, e.Sucursal, Hoy(), 500m, 0m));
            var original = (await PolizasAsync(revaluacion)).Should().ContainSingle().Subject;
            original.TipoClave.Should().Be("AS");
            original.Estado.Should().Be(EstadoPoliza.Autorizada);
            original.Fecha.Should().Be(Hoy());
            original.Partidas.Should().HaveCount(2);
            original.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["PERDIDA_CAMBIARIA_NO_REALIZADA"] && x.Cargo == 500m);
            original.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["PROVEEDORES"] && x.Abono == 500m && x.ProveedorId == e.Proveedor);

            // Día 1: la reversa deja el saldo del proveedor y el resultado como estaban (CA10.8).
            await Cxp(new RevaluacionCierreRevertidaIntegrationEvent(EmpresaBootstrapId, Ahora, revaluacion, e.Proveedor, e.Sucursal, Hoy().AddDays(1), 500m, 0m));
            var polizas = await PolizasAsync(revaluacion);
            polizas.Should().HaveCount(2);
            var reversa = polizas.Single(p => p.Id != original.Id);
            reversa.Fecha.Should().Be(Hoy().AddDays(1));
            reversa.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["PROVEEDORES"] && x.Cargo == 500m && x.ProveedorId == e.Proveedor);
            reversa.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["PERDIDA_CAMBIARIA_NO_REALIZADA"] && x.Abono == 500m);
            var todas = polizas.SelectMany(p => p.Partidas).ToList();
            Saldo(todas, e.Cuentas["PROVEEDORES"]).Should().Be(0m);
            Saldo(todas, e.Cuentas["PERDIDA_CAMBIARIA_NO_REALIZADA"]).Should().Be(0m);
        }
        finally { await BorrarPolizasDeAsync(revaluacion); await LimpiarAsync(e); }
    }

    [Fact]
    public async Task CXP12_la_utilidad_no_realizada_va_al_reves_y_el_mismo_evento_no_duplica_la_poliza()
    {
        Escenario? e = null;
        var revaluacion = Guid.NewGuid();
        try
        {
            e = await PrepararAsync();
            await AsegurarPeriodosAbiertosAsync(e.Admin, Hoy());

            var evento = new RevaluacionCierreRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, revaluacion, e.Proveedor, e.Sucursal, Hoy(), 0m, 200m);
            var id = await Cxp(evento);
            await Cxp(evento, id);

            var poliza = (await PolizasAsync(revaluacion)).Should().ContainSingle().Subject;
            poliza.Partidas.Should().HaveCount(2);
            poliza.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["PROVEEDORES"] && x.Cargo == 200m && x.ProveedorId == e.Proveedor);
            poliza.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["UTILIDAD_CAMBIARIA_NO_REALIZADA"] && x.Abono == 200m);
        }
        finally { await BorrarPolizasDeAsync(revaluacion); await LimpiarAsync(e); }
    }
}
