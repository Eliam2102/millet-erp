using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.TarjetaCredito;

public sealed class MovimientoTcAggregateTests
{
    private static Tarjeta CrearTarjeta() =>
        Tarjeta.Crear(
            empresaId: Guid.NewGuid(),
            emisora: "Amex",
            perfilParser: "AMEX_MX",
            numero: NumeroTarjetaEnmascarado.FromUltimosCuatro("1234"),
            nombreAlias: "Amex Corporativa",
            titularId: Guid.NewGuid(),
            bancoProveedorId: Guid.NewGuid(),
            limiteCreditoMxn: 100000m,
            monedaDefault: "MXN",
            diaCorte: 15,
            diaLimitePago: 20,
            vigenciaDesde: new DateOnly(2026, 1, 1));

    [Fact]
    public void CapturarCompraConCfdi_MXN_crea_Registrado_con_montoMxn_igual()
    {
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarCompraConCfdi(
            empresaId: t.EmpresaId,
            tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 1160m,
            monedaOriginal: "MXN",
            tipoCambioCaptura: null,
            merchantRaw: "STARBUCKS CDMX",
            descripcionLibre: null,
            cfdiRecibidoId: Guid.NewGuid(),
            facturaProveedorId: Guid.NewGuid(),
            proveedorId: Guid.NewGuid(),
            conceptoContable: "Alimentos");

        mov.Tipo.Should().Be(TipoMovimientoTc.CompraConCfdi);
        mov.Estado.Should().Be(EstadoMovimientoTc.Registrado);
        mov.MontoOriginal.Should().Be(1160m);
        mov.MonedaOriginal.Should().Be("MXN");
        mov.TipoCambioCaptura.Should().BeNull();
        mov.MontoMxn.Should().Be(1160m);
        mov.MerchantNormalizado.Should().Be("STARBUCKS CDMX");
    }

    [Fact]
    public void CapturarCompraConCfdi_USD_convierte_con_tipo_cambio()
    {
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarCompraConCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 100m,
            monedaOriginal: "USD",
            tipoCambioCaptura: 17.5m,
            merchantRaw: "AMAZON.COM",
            descripcionLibre: null,
            cfdiRecibidoId: Guid.NewGuid(),
            facturaProveedorId: Guid.NewGuid(),
            proveedorId: Guid.NewGuid(),
            conceptoContable: "Sistemas");

        mov.MonedaOriginal.Should().Be("USD");
        mov.TipoCambioCaptura.Should().Be(17.5m);
        mov.MontoMxn.Should().Be(1750m);
    }

    [Fact]
    public void CapturarCompraConCfdi_MXN_con_tipo_cambio_rechaza()
    {
        var t = CrearTarjeta();
        var act = () => MovimientoTarjetaCredito.CapturarCompraConCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 100m, monedaOriginal: "MXN", tipoCambioCaptura: 17.5m,
            merchantRaw: "X", descripcionLibre: null,
            cfdiRecibidoId: Guid.NewGuid(), facturaProveedorId: Guid.NewGuid(),
            proveedorId: Guid.NewGuid(), conceptoContable: "Sistemas");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_MOV_TC_INESPERADO");
    }

    [Fact]
    public void CapturarCompraConCfdi_USD_sin_tipo_cambio_rechaza()
    {
        var t = CrearTarjeta();
        var act = () => MovimientoTarjetaCredito.CapturarCompraConCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 100m, monedaOriginal: "USD", tipoCambioCaptura: null,
            merchantRaw: "X", descripcionLibre: null,
            cfdiRecibidoId: Guid.NewGuid(), facturaProveedorId: Guid.NewGuid(),
            proveedorId: Guid.NewGuid(), conceptoContable: "Sistemas");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_MOV_TC_REQUERIDO");
    }

    [Fact]
    public void CapturarCompraConCfdi_cfdi_vacio_rechaza()
    {
        var t = CrearTarjeta();
        var act = () => MovimientoTarjetaCredito.CapturarCompraConCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 100m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "X", descripcionLibre: null,
            cfdiRecibidoId: Guid.Empty, facturaProveedorId: Guid.NewGuid(),
            proveedorId: Guid.NewGuid(), conceptoContable: "Sistemas");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_MOV_CFDI_VACIO");
    }

    [Fact]
    public void CapturarCompraSinCfdi_OK_sin_factura()
    {
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 500m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "  pemex  ", descripcionLibre: "Gasolina",
            conceptoContable: "Combustible",
            ticketBlobRef: "tickets/2026/05/abc.jpg");

        mov.Tipo.Should().Be(TipoMovimientoTc.CompraSinCfdi);
        mov.FacturaProveedorId.Should().BeNull();
        mov.CfdiRecibidoId.Should().BeNull();
        mov.TicketBlobRef.Should().Be("tickets/2026/05/abc.jpg");
        mov.MerchantNormalizado.Should().Be("PEMEX"); // upper + trim
    }

    [Fact]
    public void Capturar_tarjeta_cancelada_rechaza()
    {
        var t = CrearTarjeta();
        t.Cancelar(new DateOnly(2026, 12, 31));

        var act = () => MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 100m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "X", descripcionLibre: null,
            conceptoContable: "Y", ticketBlobRef: null);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_CANCELADA_SIN_MOVIMIENTOS");
    }

    [Fact]
    public void Capturar_usuario_no_autorizado_rechaza()
    {
        var t = CrearTarjeta();
        var act = () => MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: Guid.NewGuid(), // no es titular, no está en lista
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 100m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "X", descripcionLibre: null,
            conceptoContable: "Y", ticketBlobRef: null);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_USR_NO_AUTORIZADO");
    }

    [Fact]
    public void Merchant_normalizado_colapsa_espacios()
    {
        // Acceso a un Movimiento para inspeccionar el método de
        // normalización: lo verificamos vía propiedad.
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 100m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "  Café   El  Sol  ",
            descripcionLibre: null,
            conceptoContable: "Alimentos", ticketBlobRef: null);
        mov.MerchantNormalizado.Should().Be("CAFÉ EL SOL");
    }

    [Fact]
    public void Capturar_monto_no_positivo_rechaza()
    {
        var t = CrearTarjeta();
        var act = () => MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 0m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "X", descripcionLibre: null,
            conceptoContable: "Y", ticketBlobRef: null);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_MOV_MONTO_INVALIDO");
    }
}
