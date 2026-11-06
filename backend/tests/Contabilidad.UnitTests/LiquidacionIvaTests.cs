using Millet.Contabilidad.Application.Iva;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>K10.7 · liquidación mensual de IVA: resumen del mes, retenciones y marca de cumplimiento por documento. Datos FIX.</summary>
public class LiquidacionIvaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly (string, string) Cobro = ("tesoreria", "pago_cliente");
    private static readonly (string, string) Pago = ("tesoreria", "pago_factura_proveedor");
    private static readonly DateOnly Noviembre = new(2026, 11, 15);

    private static SaldoIvaDocumento Venta(decimal @base, decimal iva, decimal retenido = 0m, string metodo = "PUE") =>
        SaldoIvaDocumento.Crear(Empresa, LadoIva.Trasladado, N.DocComprobante, Guid.NewGuid(), "FV-FIX", "UUID-FIX", metodo, Guid.NewGuid(),
            Guid.NewGuid(), new DateOnly(2026, 11, 1), "MXN", null, @base, iva, retenido, Guid.NewGuid());

    private static SaldoIvaDocumento Compra(decimal @base, decimal iva, decimal retenido = 0m, string metodo = "PUE", string? uuid = "UUID-FIX") =>
        SaldoIvaDocumento.Crear(Empresa, LadoIva.Acreditable, N.DocFacturaProveedor, Guid.NewGuid(), "FP-FIX", uuid, metodo, Guid.NewGuid(),
            Guid.NewGuid(), new DateOnly(2026, 11, 1), "MXN", null, @base, iva, retenido, Guid.NewGuid());

    /// <summary>Aplica un pago o cobro con la proporción del saldo (como el motor) y devuelve su movimiento.</summary>
    private static ReclasificacionIva Aplicar(SaldoIvaDocumento s, decimal importe)
    {
        var (iva, ret) = s.Calcular(importe);
        return s.Aplicar(s.Lado == LadoIva.Trasladado ? Cobro : Pago, Guid.NewGuid(), Noviembre, importe, iva, ret, Guid.NewGuid());
    }

    private static ResumenLiquidacionIva Liquidar(params (SaldoIvaDocumento Saldo, decimal Importe)[] aplicaciones)
    {
        var movimientos = aplicaciones.Select(a => Aplicar(a.Saldo, a.Importe)).ToList();
        var saldos = aplicaciones.Select(a => a.Saldo).DistinctBy(s => s.Id).ToDictionary(s => s.Id);
        return LiquidacionIvaHandlers.Resumen(movimientos, saldos);
    }

    [Fact]
    public void K107a_el_iva_a_pagar_del_mes_es_el_cobrado_menos_el_pagado_y_cuadra_con_la_suma_por_documento()
    {
        var ventaCobrada = Venta(11_600m, 1_600m);
        var ventaAl30 = Venta(5_800m, 800m);
        var compraPagada = Compra(4_640m, 640m);

        var r = Liquidar((ventaCobrada, 11_600m), (ventaAl30, 1_740m), (compraPagada, 4_640m));

        (r.IvaTrasladadoCobrado, r.IvaAcreditablePagado).Should().Be((1_840m, 640m), "1,600 + 30 % de 800 cobrados; 640 pagados");
        (r.IvaACargo, r.IvaAFavor).Should().Be((1_200m, 0m));
        (ventaCobrada.IvaReclasificado + ventaAl30.IvaReclasificado - compraPagada.IvaReclasificado).Should().Be(r.IvaACargo,
            "la suma por documento cuadra con el resumen");
    }

    [Fact]
    public void K107a_si_se_pago_mas_iva_del_que_se_cobro_queda_a_favor()
    {
        var r = Liquidar((Venta(1_160m, 160m), 1_160m), (Compra(11_600m, 1_600m), 11_600m));
        (r.IvaACargo, r.IvaAFavor).Should().Be((0m, 1_440m));
    }

    [Fact]
    public void K107c_el_iva_retenido_por_el_cliente_en_un_flete_se_resta_del_iva_a_pagar()
    {
        // Flete facturado: subtotal 1,000 · IVA 160 · el cliente retiene 4 % (40) y paga 1,120.
        var flete = Venta(1_120m, 160m, 40m);
        var r = Liquidar((flete, 1_120m));
        (r.IvaTrasladadoCobrado, r.IvaRetenidoPorClientes, r.IvaACargo).Should().Be((160m, 40m, 120m));
    }

    [Fact]
    public void K107c_el_iva_retenido_a_un_transportista_se_muestra_por_enterar_y_no_resta()
    {
        // Flete recibido: subtotal 1,000 · IVA 160 · Millet retiene 40 y paga 1,120 (supuesto a validar, doc 15 §4).
        var r = Liquidar((Venta(11_600m, 1_600m), 11_600m), (Compra(1_120m, 160m, 40m), 1_120m));
        (r.IvaAcreditablePagado, r.IvaRetenidoAProveedores, r.IvaACargo).Should().Be((160m, 40m, 1_440m));
    }

    [Fact]
    public void K107b_una_factura_ppd_pagada_sin_rep_queda_pendiente()
    {
        var factura = Compra(11_600m, 1_600m, metodo: "PPD");
        Aplicar(factura, 11_600m);
        LiquidacionIvaHandlers.MotivosPendiente(factura, "PPD", tieneRep: false).Should().Equal("PPD pagado sin REP");
        LiquidacionIvaHandlers.MotivosPendiente(factura, "PPD", tieneRep: true).Should().BeEmpty("con su REP queda completa");
    }

    [Fact]
    public void K107b_pue_pagada_con_cfdi_queda_completa_y_con_saldo_o_sin_cfdi_queda_pendiente()
    {
        var pagada = Compra(1_160m, 160m);
        Aplicar(pagada, 1_160m);
        LiquidacionIvaHandlers.MotivosPendiente(pagada, "PUE", null).Should().BeEmpty();

        var parcial = Compra(1_160m, 160m, uuid: null);
        Aplicar(parcial, 580m);
        LiquidacionIvaHandlers.MotivosPendiente(parcial, "PUE", null).Should().Equal("Sin CFDI", "Con saldo por pagar");
    }
}
