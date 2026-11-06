using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>C1.6 · saldo de IVA por documento: proporción, remanente del último pago, reversa y notas. Datos FIX.</summary>
public class ControlIvaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly (string, string) Pago = ("tesoreria", "pago_factura_proveedor");

    private static SaldoIvaDocumento Factura(decimal @base = 11_600m, decimal iva = 1_600m, decimal retenido = 0m) =>
        SaldoIvaDocumento.Crear(Empresa, LadoIva.Acreditable, N.DocFacturaProveedor, Guid.NewGuid(), null, "UUID-FIX", "PPD", Guid.NewGuid(),
            Guid.NewGuid(), new DateOnly(2026, 11, 1), "MXN", null, @base, iva, retenido, Guid.NewGuid());

    [Fact]
    public void C16a_pago_del_50_por_ciento_reclasifica_800_de_1600()
    {
        var saldo = Factura();
        saldo.Calcular(5_800m).Should().Be((800m, 0m));
    }

    [Fact]
    public void El_pago_que_salda_toma_el_remanente_sin_dejar_centavos()
    {
        var saldo = Factura(1_160m, 160m);
        var ivas = new List<decimal>();
        foreach (var importe in new[] { 386.67m, 386.67m, 386.66m })
        {
            var (iva, ret) = saldo.Calcular(importe);
            saldo.Aplicar(Pago, Guid.NewGuid(), new DateOnly(2026, 11, 2), importe, iva, ret, Guid.NewGuid());
            ivas.Add(iva);
        }
        ivas.Should().Equal(53.33m, 53.33m, 53.34m);
        (saldo.IvaPendiente, saldo.Saldado).Should().Be((0m, true));
    }

    [Fact]
    public void C16b_la_reversa_devuelve_lo_aplicado_y_el_iva_a_pendiente()
    {
        var saldo = Factura();
        var r = saldo.Aplicar(Pago, Guid.NewGuid(), new DateOnly(2026, 11, 2), 5_800m, 800m, 0m, Guid.NewGuid());
        saldo.Revertir(r, DateTimeOffset.UtcNow);
        (saldo.Aplicado, saldo.IvaReclasificado, r.AnuladaEn is not null).Should().Be((0m, 0m, true));
        saldo.Revertir(r, DateTimeOffset.UtcNow);
        saldo.Aplicado.Should().Be(0m, "una reversa repetida no resta dos veces");
    }

    [Fact]
    public void La_retencion_de_iva_se_reclasifica_en_la_misma_proporcion()
    {
        var saldo = Factura(1_120m, 160m, 40m);
        saldo.Calcular(560m).Should().Be((80m, 20m));
    }

    [Fact]
    public void La_nota_de_credito_reduce_base_e_iva_y_el_pago_del_resto_salda()
    {
        var saldo = Factura();
        var nota = saldo.Reducir(("cuentas_por_pagar", "nota_credito_proveedor"), Guid.NewGuid(), new DateOnly(2026, 11, 3), 1_160m, 160m, 0m,
            Guid.NewGuid());
        nota.Tipo.Should().Be(TipoMovimientoIva.Nota);
        (saldo.Base, saldo.Iva).Should().Be((10_440m, 1_440m));
        saldo.Calcular(10_440m).Should().Be((1_440m, 0m));
    }

    [Fact]
    public void Un_documento_cancelado_no_reclasifica()
    {
        var saldo = Factura();
        saldo.Cancelar();
        saldo.Calcular(5_800m).Should().Be((0m, 0m));
    }

    [Fact]
    public void Aplicaciones_del_cobro_de_mostrador_solo_toman_la_parte_no_efectivo()
    {
        var comprobante = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, OcurridoEn = DateTimeOffset.UtcNow, CobroMostradorId = Guid.NewGuid(), CajaSesionId = Guid.NewGuid(),
            ComprobanteId = comprobante, FormasPago = new[] { new { FormaPago = "04", Importe = 1_000m }, new { FormaPago = "01", Importe = 1_320m } },
            Moneda = "MXN",
        });
        var e = N.Normalizar(N.CobroMostradorRegistrado, Guid.NewGuid(), payload);
        ControlIvaDocumentos.Aplicaciones(e).Should().ContainSingle()
            .Which.Should().Be(new ControlIvaDocumentos.Aplicacion(N.DocComprobante, comprobante, 1_000m));
    }

    [Fact]
    public void Aplicaciones_del_pago_de_cliente_agrupan_por_factura()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var payload = JsonSerializer.Serialize(new
        {
            EmpresaId = Empresa, ClienteId = Guid.NewGuid(), MovimientoBancarioId = Guid.NewGuid(), Monto = 900m, Moneda = "MXN",
            FechaValor = new DateOnly(2026, 11, 2),
            Facturas = new[] { new { FacturaVentaId = a, ImporteAplicado = 300m }, new { FacturaVentaId = b, ImporteAplicado = 500m },
                new { FacturaVentaId = a, ImporteAplicado = 100m } },
        });
        var e = N.Normalizar(N.PagoClienteConfirmado, Guid.NewGuid(), payload);
        ControlIvaDocumentos.Aplicaciones(e).Select(x => (x.DocumentoId, x.Importe)).Should().BeEquivalentTo(new[] { (a, 400m), (b, 500m) });
    }

    [Fact]
    public void Sin_aplicaciones_el_campo_de_iva_va_en_cero_para_las_plantillas_v2()
    {
        var e = new EventoContable(N.CajaSesionCerrada, Guid.NewGuid(), Empresa, new DateOnly(2026, 11, 2),
            new DocumentoOrigenPoliza("facturacion", "caja_sesion", Guid.NewGuid(), null, null), null, null, null, null, null, "MXN", null,
            new Dictionary<string, decimal?> { ["Total"] = 100m }, [], "{}");
        ControlIvaDocumentos.ConCampoIva(e).Importes[ControlIvaDocumentos.CampoIva].Should().Be(0m);
    }
}
