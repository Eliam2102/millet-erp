using FluentAssertions;
using Millet.CuentasPorPagar.Application.Reportes.Diot;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using NotaCredito = Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor;
using Millet.CuentasPorPagar.UnitTests.P8;
using Xunit;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.CuentasPorPagar.UnitTests.Reportes;

/// <summary>F1-CXP-10 · DIOT por flujo de efectivo y archivo de carga del SAT (54 campos).</summary>
public sealed class DiotTests
{
    private static readonly Guid Prov = Guid.NewGuid();
    private static TerceroDiot Nacional(string rfc = "FIX010101AB1") => new(Prov, rfc, "Proveedor ficticio", false, "MEX", null);

    private static MovimientoDiot Mov(decimal aplicado, decimal total, decimal @base, decimal iva, decimal ret = 0, decimal noAcred = 0,
        decimal tc = 1, bool devolucion = false, string op = CalculadoraDiot.OperacionServicios) =>
        new(Prov, Guid.NewGuid(), devolucion, aplicado, total, @base, iva, ret, noAcred, tc, op);

    private static RenglonDiot Uno(params MovimientoDiot[] m) =>
        CalculadoraDiot.Calcular(m, new Dictionary<Guid, TerceroDiot> { [Prov] = Nacional() }).Should().ContainSingle().Subject;

    [Fact]
    public void Pago_parcial_al_16_lleva_la_base_y_el_IVA_proporcionales_al_pago()
    {
        var r = Uno(Mov(aplicado: 580, total: 1160, @base: 1000, iva: 160));
        r.Actos16.Should().Be(500); r.IvaPagado.Should().Be(80); r.ActosTasa0.Should().Be(0); r.TipoTercero.Should().Be("04");
    }

    [Fact]
    public void Factura_con_conceptos_al_16_y_al_0_reparte_la_base_por_el_IVA()
    {
        // 750 al 16 % (IVA 120) + 250 al 0 %
        var r = Uno(Mov(aplicado: 1120, total: 1120, @base: 1000, iva: 120));
        r.Actos16.Should().Be(750); r.ActosTasa0.Should().Be(250); r.ActosRfn8.Should().Be(0);
    }

    [Fact]
    public void IVA_al_8_va_a_region_fronteriza_norte()
    {
        var r = Uno(Mov(aplicado: 1080, total: 1080, @base: 1000, iva: 80));
        // 80/1000 = 8 %: toda la base es de región fronteriza
        r.ActosRfn8.Should().Be(1000); r.Actos16.Should().Be(0); r.ActosTasa0.Should().Be(0);
    }

    [Fact]
    public void Gasto_no_deducible_lleva_su_IVA_como_no_acreditable_CXP_10_b()
    {
        var r = Uno(Mov(aplicado: 1160, total: 1160, @base: 1000, iva: 160, noAcred: 160));
        r.IvaNoAcreditable.Should().Be(160); r.IvaPagado.Should().Be(160); r.Actos16.Should().Be(1000);
    }

    [Fact]
    public void Flete_con_retencion_de_IVA_al_4_la_reporta_proporcional_al_pago()
    {
        var r = Uno(Mov(aplicado: 560, total: 1120, @base: 1000, iva: 160, ret: 40));
        r.IvaRetenido.Should().Be(20); r.Actos16.Should().Be(500);
    }

    [Fact]
    public void Dolares_se_valuan_con_el_tipo_de_cambio_del_pago()
    {
        var r = Uno(Mov(aplicado: 116, total: 116, @base: 100, iva: 16, tc: 17.4725m));
        r.Actos16.Should().Be(1747.25m); r.IvaPagado.Should().Be(279.56m);
    }

    [Fact]
    public void Nota_de_credito_va_en_devoluciones_y_resta_IVA()
    {
        var r = Uno(Mov(aplicado: 1160, total: 1160, @base: 1000, iva: 160),
            Mov(aplicado: 116, total: 116, @base: 100, iva: 16, devolucion: true));
        r.Actos16.Should().Be(1000); r.Devoluciones16.Should().Be(100); r.IvaPagado.Should().Be(144); r.Documentos.Should().Be(2);
    }

    [Fact]
    public void Un_renglon_por_tercero_y_tipo_de_operacion()
    {
        var renglones = CalculadoraDiot.Calcular(
            [Mov(1160, 1160, 1000, 160, op: "02"), Mov(1160, 1160, 1000, 160, op: "02"), Mov(1160, 1160, 1000, 160, op: "03")],
            new Dictionary<Guid, TerceroDiot> { [Prov] = Nacional() });
        renglones.Select(r => (r.TipoOperacion, r.Actos16)).Should().Equal(("02", 2000m), ("03", 1000m));
    }

    [Theory]
    [InlineData(new[] { "80131502" }, "06")]
    [InlineData(new[] { "78101802", "81112100" }, "03")]
    [InlineData(new[] { "30171500", "78101802" }, "02")]
    [InlineData(new string[0], "85")]
    public void Tipo_de_operacion_por_clave_de_producto(string[] claves, string esperado) =>
        CalculadoraDiot.TipoOperacionPorClaves(claves).Should().Be(esperado);

    [Fact]
    public void RFC_generico_es_proveedor_global_y_extranjero_es_05()
    {
        CalculadoraDiot.TipoTercero(Nacional("XAXX010101000")).Should().Be("15");
        CalculadoraDiot.TipoTercero(new(Prov, "XEXX010101000", "Foreign Inc", true, "USA", "95-0000000")).Should().Be("05");
    }

    [Fact]
    public void Renglon_nacional_con_54_campos_igual_al_archivo_de_Contabilidad()
    {
        var r = new RenglonDiot("04", "03", Nacional(), 0, 0, 29999.5m, 0, 0, 4800, 0, 0, 1);
        var linea = LayoutDiot.Renglon(r);
        linea.Split('|').Should().HaveCount(54);
        linea.Should().Be("04|03|FIX010101AB1|||||||||30000||0||||||||||||||||||||||||||||||||||0||0|0|||01");
    }

    [Fact]
    public void Renglon_extranjero_lleva_ID_fiscal_nombre_y_pais_sin_RFC_CXP_10_c()
    {
        var r = new RenglonDiot("05", "85", new(Prov, "XEXX010101000", "Foreign Inc", true, "usa", "95-0000000"), 0, 0, 0, 0, 1000, 0, 0, 0, 1);
        var c = LayoutDiot.Renglon(r).Split('|');
        c[2].Should().BeEmpty(); c[3].Should().Be("95-0000000"); c[4].Should().Be("Foreign Inc"); c[5].Should().Be("USA");
        c[50].Should().Be("1000"); c[53].Should().Be("01");
    }

    [Fact]
    public void Archivo_usa_CRLF_y_omite_renglones_sin_montos()
    {
        var con = new RenglonDiot("04", "02", Nacional(), 0, 0, 100, 0, 0, 16, 0, 0, 1);
        var sin = new RenglonDiot("04", "03", Nacional(), 0, 0, 0.4m, 0, 0, 0, 0, 0, 1);
        LayoutDiot.Archivo([con, sin]).Should().Be(LayoutDiot.Renglon(con) + "\r\n");
    }

    // ---------- Lectura del mes ----------

    private static Factura NuevaFactura(P8Fixture a, decimal subtotal, decimal iva, decimal total, string clave = "78101802", string moneda = "MXN", decimal? tc = null)
    {
        var fecha = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var f = Factura.CapturarSinOc(a.Current!.Value, null, null, a.Proveedor, a.Sucursal, "FIX-DIOT", null, fecha, fecha,
            new(2026, 12, 31), moneda, tc, subtotal, 0, iva, total - subtotal - iva < 0 ? subtotal + iva - total : 0, total, "FIX prueba DIOT", fecha);
        f.AgregarLinea(null, clave, "Concepto de prueba", 1, "E48", null, subtotal, subtotal, null, null, null);
        return f;
    }

    [Fact]
    public async Task Lee_pagos_del_mes_tarjeta_y_notas_y_excluye_otros_meses_revertidos_y_canceladas()
    {
        await using var a = new P8Fixture();
        var servicio = NuevaFactura(a, 1000, 160, 1160);
        var flete = NuevaFactura(a, 1000, 160, 1120, clave: "78101802");
        flete.AsignarRetencionesDetalle([new RetencionCfdi("002", 0.04m, 40)]);
        var tarjeta = NuevaFactura(a, 500, 80, 580, clave: "30171500");
        tarjeta.Autorizar(null, a.UtcNow);
        tarjeta.RegistrarPago(580, new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero), "Pago con tarjeta");
        var otroMes = NuevaFactura(a, 100, 16, 116);
        var revertido = new PagoProveedorLocal(a.Current!.Value, otroMes.Id, Guid.NewGuid(), 116, new(2026, 11, 3));
        revertido.Revertir();
        var nota = NotaCredito.Capturar(a.Current!.Value, null, Guid.NewGuid().ToString(), a.Proveedor, null, null,
            new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero), "MXN", null, 100, 16, 0, 116,
            TipoNotaCredito.Descuento, TipoRelacionCfdi.NotaCredito, Guid.NewGuid().ToString(), servicio.Id, null, a.UtcNow);
        a.Db.AddRange(servicio, flete, tarjeta, otroMes, nota, revertido,
            new PagoProveedorLocal(a.Current!.Value, servicio.Id, Guid.NewGuid(), 580, new(2026, 11, 10)),
            new PagoProveedorLocal(a.Current!.Value, servicio.Id, Guid.NewGuid(), 580, new(2026, 12, 1)),
            new PagoProveedorLocal(a.Current!.Value, flete.Id, Guid.NewGuid(), 1120, new(2026, 11, 30)),
            new PagoProveedorLocal(a.Current!.Value, otroMes.Id, Guid.NewGuid(), 116, new(2026, 10, 31)));
        await a.Db.SaveChangesAsync();

        var (renglones, pagos) = await new LectorDiot(a.Db, a).LeerAsync(2026, 11, default);

        pagos.Should().Be(3);
        var servicios = renglones.Single(r => r.TipoOperacion == "03");
        servicios.Actos16.Should().Be(500 + 1000); // mitad del servicio + flete completo
        servicios.Devoluciones16.Should().Be(100);
        servicios.IvaRetenido.Should().Be(40);
        servicios.IvaPagado.Should().Be(80 + 160 - 16);
        var bienes = renglones.Single(r => r.TipoOperacion == "02");
        bienes.Actos16.Should().Be(500); bienes.IvaPagado.Should().Be(80);
        renglones.Should().OnlyContain(r => r.TipoTercero == "04" && r.Tercero.Rfc == "FIX010101ABC");
    }

    [Fact]
    public async Task Pago_en_dolares_usa_el_tipo_de_cambio_del_pago_y_si_falta_el_de_la_factura()
    {
        await using var a = new P8Fixture();
        var usd = NuevaFactura(a, 100, 16, 116, moneda: "USD", tc: 17m);
        var conTc = new PagoProveedorLocal(a.Current!.Value, usd.Id, Guid.NewGuid(), 58, new(2026, 11, 3));
        conTc.AsignarMoneda("USD", 18m);
        a.Db.AddRange(usd, conTc, new PagoProveedorLocal(a.Current!.Value, usd.Id, Guid.NewGuid(), 58, new(2026, 11, 4)));
        await a.Db.SaveChangesAsync();

        var (renglones, _) = await new LectorDiot(a.Db, a).LeerAsync(2026, 11, default);

        renglones.Single().Actos16.Should().Be(50 * 18 + 50 * 17);
    }
}
