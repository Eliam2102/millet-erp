using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.TarjetaCredito;

/// <summary>
/// F7-PR6: tests de transiciones del cierre — Conciliado, Cerrado,
/// PagadoBanco; movimientos disputados / refund / especiales.
/// </summary>
public sealed class CierreEstadoCuentaTcTests
{
    private static EstadoCuentaTc CrearEC() =>
        EstadoCuentaTc.Crear(
            empresaId: Guid.NewGuid(),
            tarjetaId: Guid.NewGuid(),
            periodoDesde: new DateOnly(2026, 5, 1),
            periodoHasta: new DateOnly(2026, 5, 31),
            fechaCorte: new DateOnly(2026, 5, 15),
            fechaLimitePago: new DateOnly(2026, 5, 25));

    private static Tarjeta CrearTarjeta() =>
        Tarjeta.Crear(
            empresaId: Guid.NewGuid(),
            emisora: "Amex",
            perfilParser: "AMEX_MX",
            numero: NumeroTarjetaEnmascarado.FromUltimosCuatro("9999"),
            nombreAlias: "Test",
            titularId: Guid.NewGuid(),
            bancoProveedorId: Guid.NewGuid(),
            limiteCreditoMxn: 100000m,
            monedaDefault: "MXN",
            diaCorte: 15,
            diaLimitePago: 20,
            vigenciaDesde: new DateOnly(2026, 1, 1));

    // -------- MarcarConciliado --------

    [Fact]
    public void MarcarConciliado_diferencia_no_cero_rechaza()
    {
        var ec = CrearEC();
        ec.RegistrarArchivoBanco("blob", new string('a', 64), Guid.NewGuid(),
            "AMEX_MX", totalDeclaradoMxn: 1000m, ahora: DateTimeOffset.UtcNow);
        ec.ActualizarTotalConciliado(800m); // diferencia 200

        var act = () => ec.MarcarConciliado();
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EC_DIFERENCIA_NO_CERO");
    }

    [Fact]
    public void MarcarConciliado_lineas_pendientes_rechaza()
    {
        var ec = CrearEC();
        ec.RegistrarArchivoBanco("blob", new string('a', 64), Guid.NewGuid(),
            "AMEX_MX", totalDeclaradoMxn: 1000m, ahora: DateTimeOffset.UtcNow);
        var linea = ec.AgregarLinea(1, new DateOnly(2026, 5, 5), 1000m, "MXN", 1000m,
            "TEST", null, "Compra");
        // queda Pendiente
        ec.ActualizarTotalConciliado(1000m); // diferencia 0

        var act = () => ec.MarcarConciliado();
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EC_LINEAS_PENDIENTES");
    }

    [Fact]
    public void MarcarConciliado_diferencia_cero_y_sin_pendientes_OK()
    {
        var ec = CrearEC();
        ec.RegistrarArchivoBanco("blob", new string('a', 64), Guid.NewGuid(),
            "AMEX_MX", totalDeclaradoMxn: 1000m, ahora: DateTimeOffset.UtcNow);
        var linea = ec.AgregarLinea(1, new DateOnly(2026, 5, 5), 1000m, "MXN", 1000m,
            "TEST", null, "Compra");
        linea.MarcarMatched(Guid.NewGuid(), 100m);
        ec.ActualizarTotalConciliado(1000m);

        ec.MarcarConciliado();
        ec.Estado.Should().Be(EstadoCuentaTcStatus.Conciliado);
    }

    // -------- Cerrar --------

    [Fact]
    public void Cerrar_solo_desde_Conciliado()
    {
        var ec = CrearEC();
        var act = () => ec.Cerrar(Guid.NewGuid());
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EC_NO_CERRABLE");
    }

    [Fact]
    public void Cerrar_persiste_factura_id()
    {
        var ec = CrearEC();
        ec.RegistrarArchivoBanco("blob", new string('a', 64), Guid.NewGuid(),
            "AMEX_MX", totalDeclaradoMxn: 1000m, ahora: DateTimeOffset.UtcNow);
        var linea = ec.AgregarLinea(1, new DateOnly(2026, 5, 5), 1000m, "MXN", 1000m,
            "TEST", null, "Compra");
        linea.MarcarMatched(Guid.NewGuid(), 100m);
        ec.ActualizarTotalConciliado(1000m);
        ec.MarcarConciliado();

        var facturaId = Guid.NewGuid();
        ec.Cerrar(facturaId);

        ec.Estado.Should().Be(EstadoCuentaTcStatus.Cerrado);
        ec.FacturaProveedorId.Should().Be(facturaId);
    }

    [Fact]
    public void Cerrar_requiere_facturaId_no_vacio()
    {
        var ec = CrearEC();
        ec.RegistrarArchivoBanco("blob", new string('a', 64), Guid.NewGuid(),
            "AMEX_MX", totalDeclaradoMxn: 0m, ahora: DateTimeOffset.UtcNow);
        // Add 1 line, mark matched, total 0
        var linea = ec.AgregarLinea(1, new DateOnly(2026, 5, 5), 0.01m, "MXN", 0m,
            "TEST", null, "Compra");
        linea.MarcarMatched(Guid.NewGuid(), 100m);
        ec.ActualizarTotalConciliado(0m);
        ec.MarcarConciliado();

        var act = () => ec.Cerrar(Guid.Empty);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EC_FACTURA_BANCO_VACIA");
    }

    // -------- MarcarPagadoBanco --------

    [Fact]
    public void MarcarPagadoBanco_solo_desde_Cerrado()
    {
        var ec = CrearEC();
        var act = () => ec.MarcarPagadoBanco();
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EC_NO_PAGABLE_BANCO");
    }

    // -------- Refund factory --------

    [Fact]
    public void CapturarRefund_requiere_movimiento_original()
    {
        var t = CrearTarjeta();
        var act = () => MovimientoTarjetaCredito.CapturarRefund(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 10),
            montoOriginal: 500m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "AMAZON",
            conceptoContable: "Reembolso",
            movimientoOriginalId: Guid.Empty);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_MOV_ORIGINAL_VACIO");
    }

    [Fact]
    public void CapturarRefund_persiste_FK_a_original()
    {
        var t = CrearTarjeta();
        var originalId = Guid.NewGuid();
        var refund = MovimientoTarjetaCredito.CapturarRefund(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 12),
            montoOriginal: 500m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "AMAZON",
            conceptoContable: "Reembolso",
            movimientoOriginalId: originalId);

        refund.Tipo.Should().Be(TipoMovimientoTc.Refund);
        refund.MovimientoOriginalId.Should().Be(originalId);
    }

    // -------- Movimiento especial --------

    [Fact]
    public void CapturarMovimientoEspecial_rechaza_tipo_no_especial()
    {
        var t = CrearTarjeta();
        var act = () => MovimientoTarjetaCredito.CapturarMovimientoEspecial(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            tipo: TipoMovimientoTc.CompraSinCfdi,
            montoOriginal: 100m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "X",
            conceptoContable: "Y");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_MOV_TIPO_NO_ESPECIAL");
    }

    [Theory]
    [InlineData(TipoMovimientoTc.GastoFinanciero)]
    [InlineData(TipoMovimientoTc.Anualidad)]
    [InlineData(TipoMovimientoTc.ComisionDivisa)]
    public void CapturarMovimientoEspecial_acepta_3_tipos(TipoMovimientoTc tipo)
    {
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarMovimientoEspecial(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            tipo: tipo,
            montoOriginal: 100m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "BANCO",
            conceptoContable: "GASTO_FINANCIERO");
        mov.Tipo.Should().Be(tipo);
        mov.Estado.Should().Be(EstadoMovimientoTc.Registrado);
    }

    // -------- Disputa --------

    [Fact]
    public void Disputar_motivo_vacio_rechaza()
    {
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 500m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "X", descripcionLibre: null,
            conceptoContable: "Y", ticketBlobRef: null);

        var act = () => mov.Disputar("  ", new DateOnly(2026, 5, 20));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_MOV_MOTIVO_DISPUTA_VACIO");
    }

    [Fact]
    public void Disputar_marca_en_disputa_y_estado()
    {
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 500m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "X", descripcionLibre: null,
            conceptoContable: "Y", ticketBlobRef: null);

        mov.Disputar("Cargo desconocido", new DateOnly(2026, 5, 20));

        mov.EnDisputa.Should().BeTrue();
        mov.MotivoDisputa.Should().Be("Cargo desconocido");
        mov.Estado.Should().Be(EstadoMovimientoTc.EnDisputa);
    }

    [Fact]
    public void ResolverDisputa_legitimo_vuelve_a_Registrado()
    {
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 500m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "X", descripcionLibre: null,
            conceptoContable: "Y", ticketBlobRef: null);

        mov.Disputar("test", new DateOnly(2026, 5, 20));
        mov.ResolverDisputa(fueLegitimo: true);

        mov.EnDisputa.Should().BeFalse();
        mov.MotivoDisputa.Should().BeNull();
        mov.Estado.Should().Be(EstadoMovimientoTc.Registrado);
    }

    [Fact]
    public void ResolverDisputa_no_legitimo_pasa_a_Reversado()
    {
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 500m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "X", descripcionLibre: null,
            conceptoContable: "Y", ticketBlobRef: null);

        mov.Disputar("test", new DateOnly(2026, 5, 20));
        mov.ResolverDisputa(fueLegitimo: false);

        mov.Estado.Should().Be(EstadoMovimientoTc.Reversado);
    }

    // -------- PagadoAlBanco --------

    [Fact]
    public void MarcarPagadoAlBanco_solo_desde_ConciliadoConEstadoCuenta()
    {
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 500m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "X", descripcionLibre: null,
            conceptoContable: "Y", ticketBlobRef: null);

        var act = () => mov.MarcarPagadoAlBanco();
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_MOV_NO_PAGABLE_BANCO");
    }

    [Fact]
    public void MarcarPagadoAlBanco_disputa_se_omite_silenciosamente()
    {
        var t = CrearTarjeta();
        var mov = MovimientoTarjetaCredito.CapturarCompraSinCfdi(
            empresaId: t.EmpresaId, tarjeta: t,
            usuarioQueUsoId: t.TitularId,
            fechaMovimiento: new DateOnly(2026, 5, 5),
            montoOriginal: 500m, monedaOriginal: "MXN", tipoCambioCaptura: null,
            merchantRaw: "X", descripcionLibre: null,
            conceptoContable: "Y", ticketBlobRef: null);
        mov.Disputar("test", new DateOnly(2026, 5, 20));

        // No debe lanzar; el flujo de cierre del EC ignora movs en disputa.
        mov.MarcarPagadoAlBanco();
        mov.Estado.Should().Be(EstadoMovimientoTc.EnDisputa);
    }
}
