using Millet.CuentasPorPagar.Application.Reportes.AntiguedadSaldos;
using Millet.CuentasPorPagar.Application.Reportes.CarteraPorCategoriaRevision;
using Millet.CuentasPorPagar.Application.Reportes.AuxiliarProveedores;
using Millet.CuentasPorPagar.Application.Reportes.AntiguedadAnticipos;
using Millet.CuentasPorPagar.Application.Reportes.PasivosObras;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.SharedKernel.Application.Exceptions;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.UnitTests.P8;

public sealed class ReportesP8Tests
{
    private static readonly DateOnly Corte = new(2026, 9, 30);
    [Fact]
    public async Task Corte_pasado_excluye_pagos_NC_anticipos_y_cargos_posteriores_y_separa_monedas()
    {
        await using var a = new P8Fixture();
        var f = a.Nueva(total: 1000); f.Autorizar(null, f.FechaDocumento);
        f.RegistrarPago(100, new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero), "FIX pago anterior");
        f.RegistrarPago(200, a.UtcNow, "FIX pago posterior");
        f.AplicarNotaCredito(100, new(2026, 10, 9)); f.AplicarAnticipo(100, new(2026, 10, 9));
        f.AplicarNotaCargo(50, Guid.NewGuid(), new(2026, 10, 9));
        a.Db.Add(f); a.Db.Add(a.Nueva("USD", 50));
        var futura = a.Nueva(total: 999); futura.EditarCabeceraPreAutorizacion("FIX", null, new(2026, 10, 15), a.UtcNow);
        // Fecha documento futura: factory explícito, no fecha contable como sustituto.
        var propiedad = a.Db.Entry(futura).Property(x => x.FechaDocumento); propiedad.CurrentValue = a.UtcNow;
        a.Db.Add(futura); await a.Db.SaveChangesAsync(); a.Db.ChangeTracker.Clear();
        var antiguedad = await new AntiguedadSaldosProveedoresHandler(a.Lector, a).Handle(new(Corte), default);
        var cartera = await new CarteraPorCategoriaRevisionHandler(a.Lector, a).Handle(new(Corte), default);
        var auxiliar = await new AuxiliarProveedoresHandler(a.Lector, a).Handle(new(Corte), default);
        foreach (var reporte in new[] { antiguedad, cartera, auxiliar })
        {
            reporte.Filas.Should().HaveCount(2);
            reporte.Filas.Single(r => (string)r["moneda"]! == "MXN")["total"].Should().Be(900m);
            reporte.Filas.Single(r => (string)r["moneda"]! == "USD")["total"].Should().Be(50m);
            reporte.Filas.Should().OnlyContain(r => (string)r["proveedor_nombre"]! == "Proveedor ficticio P8" && (string)r["rfc"]! == "FIX010101ABC");
            reporte.Columnas.Should().NotContain(c => c.Key == "proveedor_id");
            var totales = (IEnumerable<Dictionary<string, object?>>)reporte.Totales!["por_moneda"]!;
            totales.Single(t => (string)t["moneda"]! == "MXN")["total"].Should().Be(900m);
            totales.Single(t => (string)t["moneda"]! == "USD")["total"].Should().Be(50m);
        }
        antiguedad.Filas[0]["por_vencer"].Should().Be(900m);
    }
    [Fact]
    public async Task Pago_completo_posterior_y_cancelacion_posterior_no_borran_el_saldo_pasado()
    {
        await using var a = new P8Fixture();
        var pagada = a.Nueva(); pagada.Autorizar(null, pagada.FechaDocumento); pagada.RegistrarPago(1000, a.UtcNow, "FIX pago");
        var cancelada = a.Nueva(); cancelada.Cancelar(MotivoCancelacion.ErrorCaptura, "FIX", null, a.UtcNow);
        a.Db.AddRange(pagada, cancelada); await a.Db.SaveChangesAsync();
        var reporte = await new AuxiliarProveedoresHandler(a.Lector, a).Handle(new(Corte), default);
        reporte.Filas.Single()["total"].Should().Be(2000m);
    }
    [Fact]
    public async Task Anticipo_amortizado_se_excluye_hoy_y_conserva_saldo_antes_de_amortizar()
    {
        await using var a = new P8Fixture();
        var anticipo = global::Millet.CuentasPorPagar.Domain.AnticipoProveedor.AnticipoProveedor.Capturar(a.Current!.Value, null, Guid.NewGuid().ToString(), a.Proveedor, "FANT", "FIX", new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), "MXN", null, 100, null, null, a.UtcNow);
        var f = a.Nueva(); anticipo.Amortizar(100, a.UtcNow); f.AplicarAnticipo(100, new(2026, 10, 9), anticipo.Id);
        a.Db.AddRange(anticipo, f); await a.Db.SaveChangesAsync();
        var h = new AntiguedadAnticiposProveedoresHandler(a.Db, a, a);
        (await h.Handle(new(Corte), default)).Filas.Single()["saldo_amortizable"].Should().Be(100m);
        (await h.Handle(new(new(2026, 10, 9)), default)).Filas.Should().BeEmpty();
    }
    [Fact]
    public async Task Obra_es_independiente_de_sucursal_y_filtra_el_pasivo()
    {
        await using var a = new P8Fixture(); a.Db.AddRange(a.Nueva(obra:"FIX-OBRA-A"), a.Nueva(obra:"FIX-OBRA-B")); await a.Db.SaveChangesAsync();
        var r = await new PasivosObrasHandler(a.Lector, a).Handle(new(Obra:"FIX-OBRA-A", FechaCorte:Corte), default);
        r.Filas.Should().HaveCount(1); r.Filas[0]["obra"].Should().Be("FIX-OBRA-A");
    }
    [Fact]
    public async Task Reporte_sin_filtro_de_sucursal_respeta_asignaciones_del_usuario()
    {
        await using var a = new P8Fixture(); a.Corporativo = false;
        var f = a.Nueva(); a.Db.Entry(f).Property(x => x.SucursalId).CurrentValue = Guid.NewGuid();
        a.Db.AddRange(f, a.Nueva()); await a.Db.SaveChangesAsync();
        (await new AuxiliarProveedoresHandler(a.Lector, a).Handle(new(Corte), default)).Filas.Single()["numero_facturas"].Should().Be(1);
    }
    [Fact]
    public async Task Reverso_despues_del_corte_no_reabre_saldo_antes_del_corte()
    {
        await using var a = new P8Fixture(); var f=a.Nueva(); f.Autorizar(null, f.FechaDocumento);
        f.RegistrarPago(1000, new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero), "FIX"); f.RevertirPago(1000, a.UtcNow, "FIX reverso");
        a.Db.Add(f); await a.Db.SaveChangesAsync();
        (await new AuxiliarProveedoresHandler(a.Lector, a).Handle(new(Corte), default)).Filas.Should().BeEmpty();
    }
}
