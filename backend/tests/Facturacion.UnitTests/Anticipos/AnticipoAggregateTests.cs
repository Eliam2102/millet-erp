using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Anticipos;

public sealed class AnticipoAggregateTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static Anticipo Nuevo(decimal montoCobrado = 1000m) => Anticipo.Crear(
        empresaId: Guid.NewGuid(),
        clienteId: Guid.NewGuid(),
        receptorRfc: "AAA010101AAA",
        tipoAnticipo: TipoAnticipo.ClientesMxp,
        moneda: "MXN",
        montoCobrado: montoCobrado,
        facturaAnticipoId: Guid.NewGuid());

    // ---- Anticipo (saldo) ----

    [Fact]
    public void Crear_abre_el_saldo_completo()
    {
        var a = Nuevo(1000m);

        a.Estado.Should().Be(EstadoAnticipo.Abierto);
        a.MontoCobrado.Should().Be(1000m);
        a.MontoAmortizado.Should().Be(0m);
        a.Saldo.Should().Be(1000m);
        a.SaldoDisponible.Should().Be(1000m);
    }

    [Fact]
    public void Vincular_compromete_disponible_pero_no_reduce_el_saldo_amortizado()
    {
        var a = Nuevo(1000m);

        a.Vincular(Guid.NewGuid(), 400m, Ahora);

        // M2 no amortiza: el saldo persistido no cambia, solo baja el disponible.
        a.Saldo.Should().Be(1000m);
        a.MontoAmortizado.Should().Be(0m);
        a.SaldoDisponible.Should().Be(600m);
        a.Vinculaciones.Should().HaveCount(1);
        a.Estado.Should().Be(EstadoAnticipo.Abierto);
    }

    [Fact]
    public void Vincular_dos_facturas_acumula_el_compromiso()
    {
        var a = Nuevo(1000m);

        a.Vincular(Guid.NewGuid(), 400m, Ahora);
        a.Vincular(Guid.NewGuid(), 350m, Ahora);

        a.SaldoDisponible.Should().Be(250m);
    }

    [Fact]
    public void Vincular_mas_que_el_disponible_lanza_SALDO_INSUFICIENTE()
    {
        var a = Nuevo(1000m);
        a.Vincular(Guid.NewGuid(), 800m, Ahora);

        var act = () => a.Vincular(Guid.NewGuid(), 300m, Ahora);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_SALDO_INSUFICIENTE");
    }

    [Fact]
    public void Vincular_la_misma_factura_dos_veces_lanza_YA_VINCULADO()
    {
        var a = Nuevo(1000m);
        var facturaId = Guid.NewGuid();
        a.Vincular(facturaId, 100m, Ahora);

        var act = () => a.Vincular(facturaId, 100m, Ahora);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_YA_VINCULADO");
    }

    [Fact]
    public void Vincular_importe_no_positivo_lanza_IMPORTE_INVALIDO()
    {
        var a = Nuevo(1000m);

        var act = () => a.Vincular(Guid.NewGuid(), 0m, Ahora);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_IMPORTE_INVALIDO");
    }

    // ---- RegistrarAmortizacion (M3) ----

    [Fact]
    public void RegistrarAmortizacion_reduce_el_saldo_y_asocia_la_NC()
    {
        var a = Nuevo(1000m);
        var facturaId = Guid.NewGuid();
        var ncId = Guid.NewGuid();

        a.RegistrarAmortizacion(facturaId, ncId, 400m, Ahora);

        a.MontoAmortizado.Should().Be(400m);
        a.Saldo.Should().Be(600m);
        a.Estado.Should().Be(EstadoAnticipo.Abierto);
        a.Vinculaciones.Single().NcAmortizacionId.Should().Be(ncId);
    }

    [Fact]
    public void RegistrarAmortizacion_total_pasa_a_Amortizado()
    {
        var a = Nuevo(1000m);

        a.RegistrarAmortizacion(Guid.NewGuid(), Guid.NewGuid(), 1000m, Ahora);

        a.Saldo.Should().Be(0m);
        a.Estado.Should().Be(EstadoAnticipo.Amortizado);
    }

    [Fact]
    public void RegistrarAmortizacion_reutiliza_la_vinculacion_pre_existente_M2()
    {
        var a = Nuevo(1000m);
        var facturaId = Guid.NewGuid();
        a.Vincular(facturaId, 300m, Ahora); // M2 previo

        a.RegistrarAmortizacion(facturaId, Guid.NewGuid(), 300m, Ahora);

        a.Vinculaciones.Should().HaveCount(1); // no duplica
        a.MontoAmortizado.Should().Be(300m);
        a.Saldo.Should().Be(700m);
    }

    [Fact]
    public void RegistrarAmortizacion_dos_veces_la_misma_factura_lanza_YA_AMORTIZADO()
    {
        var a = Nuevo(1000m);
        var facturaId = Guid.NewGuid();
        a.RegistrarAmortizacion(facturaId, Guid.NewGuid(), 200m, Ahora);

        var act = () => a.RegistrarAmortizacion(facturaId, Guid.NewGuid(), 200m, Ahora);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_YA_AMORTIZADO");
    }

    [Fact]
    public void RegistrarAmortizacion_mas_que_el_disponible_lanza_SALDO_INSUFICIENTE()
    {
        var a = Nuevo(1000m);

        var act = () => a.RegistrarAmortizacion(Guid.NewGuid(), Guid.NewGuid(), 1500m, Ahora);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_SALDO_INSUFICIENTE");
    }

    // ---- FacturaAnticipo (CFDI) ----

    private static DatosFiscalesReceptor Receptor(string rfc = "AAA010101AAA") => new(
        Rfc: rfc, Nombre: "Cliente Maquila", RegimenFiscal: "601", CodigoPostal: "97000",
        UsoCfdi: "G03", Pais: "MEX", EsGenerico: DatosFiscalesReceptor.EsRfcGenerico(rfc));

    private static FacturaAnticipo CrearFactura(DatosFiscalesReceptor receptor, decimal montoBase = 1000m) =>
        FacturaAnticipo.CrearBorrador(
            empresaId: Guid.NewGuid(), folio: "FANT-000001", folioNumero: 1, sucursalId: Guid.NewGuid(),
            cajaId: null, usuarioEmisorId: null, receptor: receptor,
            emisor: new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"),
            formaPago: "03", moneda: "MXN", tipoCambio: null,
            periodoAnio: 2026, periodoMes: 5, tipoAnticipo: TipoAnticipo.ClientesMxp,
            pedidoFacturableId: null, anticipoId: Guid.NewGuid(), montoBase: montoBase, tasaIvaTraslado: 0.16m);

    [Fact]
    public void FacturaAnticipo_calcula_totales_con_iva()
    {
        var f = CrearFactura(Receptor(), montoBase: 1000m);

        f.Tipo.Should().Be(TipoComprobante.Ingreso);
        f.Subtotal.Should().Be(1000m);
        f.ImpuestosTrasladados.Should().Be(160m);
        f.Total.Should().Be(1160m);
        f.Estado.Should().Be(EstadoTimbrado.Borrador);
    }

    [Fact]
    public void FacturaAnticipo_con_receptor_generico_es_rechazada()
    {
        var act = () => CrearFactura(Receptor("XAXX010101000"));

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "ANTICIPO_RECEPTOR_GENERICO");
    }
}
