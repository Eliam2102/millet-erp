using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

public sealed class FacturaProveedorAggregateTests
{
    private static Domain.FacturaProveedor.FacturaProveedor Capturar(decimal total = 1160m) =>
        Domain.FacturaProveedor.FacturaProveedor.CapturarConOc(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: null,
            uuidCfdi: null,
            proveedorId: Guid.NewGuid(),
            sucursalId: Guid.NewGuid(),
            folioProveedor: "F-001", serieProveedor: "A",
            fechaDocumento: DateTimeOffset.UtcNow,
            fechaContabilizacion: DateTimeOffset.UtcNow,
            fechaVencimiento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m,
            impuestosTrasladados: 160m, retenciones: 0m, total: total,
            ordenCompraId: Guid.NewGuid(),
            encargadoComprasSnapshot: null,
            tolerancia: Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: 0m,
            redondeoAplicado: 0m,
            ahora: DateTimeOffset.UtcNow);

    [Fact]
    public void CapturarConOc_crea_factura_en_Capturada_con_bitacora_inicial()
    {
        var f = Capturar();
        f.Estado.Should().Be(EstadoPasivo.Capturada);
        f.Total.Should().Be(1160m);
        f.SaldoPendiente.Should().Be(1160m);
        f.Bitacora.Should().HaveCount(1);
    }

    [Fact]
    public void CapturarConOc_rechaza_total_no_positivo()
    {
        var act = () => Capturar(total: 0m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_TOTAL_INVALIDO");
    }

    [Fact]
    public void Cancelar_desde_Capturada_pasa_a_Cancelada_con_motivo()
    {
        var f = Capturar();
        f.Cancelar(MotivoCancelacion.ErrorCaptura, texto: null, usuarioId: null, ahora: DateTimeOffset.UtcNow);

        f.Estado.Should().Be(EstadoPasivo.Cancelada);
        f.MotivoDeCancelacion.Should().Be(MotivoCancelacion.ErrorCaptura);
        f.Bitacora.Should().HaveCount(2); // captura + cancelación
    }

    [Fact]
    public void Cancelar_motivo_OtroConTexto_requiere_texto()
    {
        var f = Capturar();
        var act = () => f.Cancelar(MotivoCancelacion.OtroConTexto, texto: null, usuarioId: null, ahora: DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_MOTIVO_TEXTO_REQUERIDO");
    }

    [Fact]
    public void Cancelar_factura_Autorizada_no_permitido_en_F3()
    {
        var f = Capturar();
        f.Autorizar(usuarioId: null, ahora: DateTimeOffset.UtcNow);

        var act = () => f.Cancelar(MotivoCancelacion.ErrorCaptura, texto: null, usuarioId: null, ahora: DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_AUTORIZADA_NO_CANCELABLE");
    }

    [Fact]
    public void Autorizar_pasa_a_Autorizada_y_limpia_revision()
    {
        var f = Capturar();
        f.Autorizar(usuarioId: null, ahora: DateTimeOffset.UtcNow);

        f.Estado.Should().Be(EstadoPasivo.Autorizada);
        f.EnRevision.Should().BeFalse();
        f.Bitacora.Should().HaveCount(2);
    }

    [Fact]
    public void EditarCabecera_funciona_solo_en_Capturada()
    {
        var f = Capturar();
        f.EditarCabeceraPreAutorizacion(
            folioProveedor: "F-002",
            serieProveedor: "B",
            fechaVencimiento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60)),
            fechaContabilizacion: DateTimeOffset.UtcNow);

        f.FolioProveedor.Should().Be("F-002");
        f.SerieProveedor.Should().Be("B");

        f.Autorizar(usuarioId: null, ahora: DateTimeOffset.UtcNow);
        var act = () => f.EditarCabeceraPreAutorizacion(
            folioProveedor: "F-003", serieProveedor: "C",
            fechaVencimiento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            fechaContabilizacion: DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void AgregarLinea_persiste_posicion_correlativa()
    {
        var f = Capturar();
        var l1 = f.AgregarLinea(null, "01010101", "Servicio A", 1, "E48", null, 500m, 500m, null, null, null);
        var l2 = f.AgregarLinea(null, "01010101", "Servicio B", 2, "E48", null, 250m, 500m, null, null, null);

        l1.Posicion.Should().Be(1);
        l2.Posicion.Should().Be(2);
        f.Lineas.Should().HaveCount(2);
    }
}
