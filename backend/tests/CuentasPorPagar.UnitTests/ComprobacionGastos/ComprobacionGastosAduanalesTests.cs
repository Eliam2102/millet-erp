using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.ComprobacionGastos;

/// <summary>
/// F7-PR2: comprobación tipo <c>GastosAduanales</c> con doble firma
/// (Comercio Exterior + Dirección de Finanzas).
/// </summary>
public sealed class ComprobacionGastosAduanalesTests
{
    private static Domain.ComprobacionGastos.ComprobacionGastos CrearAduanales(
        string? pedimento = "26-13-3018-000123",
        TipoComprobacionGastos tipo = TipoComprobacionGastos.GastosAduanales) =>
        Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            empresaId: Guid.NewGuid(),
            tipo: tipo,
            sucursalId: Guid.NewGuid(),
            responsableId: Guid.NewGuid(),
            fechaInicio: new DateOnly(2026, 5, 1),
            fechaFin: new DateOnly(2026, 5, 15),
            moneda: "MXN",
            observaciones: null,
            ahora: DateTimeOffset.UtcNow,
            numeroPedimento: pedimento);

    private static void AgregarLinea(Domain.ComprobacionGastos.ComprobacionGastos c, decimal total = 5800m)
    {
        c.AgregarLinea(
            facturaProveedorId: Guid.NewGuid(),
            cfdiRecibidoId: null, uuidCfdi: null,
            proveedorId: Guid.NewGuid(),
            folioProveedor: "ADU-001",
            fechaCfdi: DateTimeOffset.UtcNow,
            subtotal: total / 1.16m,
            impuestosTrasladados: total - (total / 1.16m),
            retenciones: 0m,
            total: total,
            moneda: "MXN",
            concepto: "Aduanales");
    }

    [Fact]
    public void Crear_aduanales_requiere_numero_pedimento()
    {
        var act = () => CrearAduanales(pedimento: null);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_PEDIMENTO_REQUERIDO");
    }

    [Fact]
    public void Crear_aduanales_acepta_pedimento_y_lo_guarda()
    {
        var c = CrearAduanales(pedimento: "26-13-3018-000123");
        c.Tipo.Should().Be(TipoComprobacionGastos.GastosAduanales);
        c.NumeroPedimento.Should().Be("26-13-3018-000123");
    }

    [Fact]
    public void Crear_caja_chica_con_pedimento_rechaza()
    {
        var act = () => CrearAduanales(
            tipo: TipoComprobacionGastos.ReembolsoCajaChica,
            pedimento: "26-13-3018-000123");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_PEDIMENTO_NO_APLICA");
    }

    [Fact]
    public void Autorizar_simple_en_aduanales_rechaza()
    {
        var c = CrearAduanales();
        AgregarLinea(c);
        var act = () => c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "COMP_ADUANALES_REQUIERE_DOBLE_FIRMA");
    }

    [Fact]
    public void AutorizarNivel1_en_caja_chica_rechaza()
    {
        var c = Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            empresaId: Guid.NewGuid(),
            tipo: TipoComprobacionGastos.ReembolsoCajaChica,
            sucursalId: Guid.NewGuid(),
            responsableId: Guid.NewGuid(),
            fechaInicio: new DateOnly(2026, 5, 1),
            fechaFin: new DateOnly(2026, 5, 15),
            moneda: "MXN", observaciones: null,
            ahora: DateTimeOffset.UtcNow,
            destinoReposicion: DestinoReposicionCaja.CuentaSucursal);
        var act = () => c.AutorizarNivel1Aduanales(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_NIVEL1_SOLO_ADUANALES");
    }

    [Fact]
    public void AutorizarNivel1_desde_Borrador_pasa_a_AutorizadaNivel1()
    {
        var c = CrearAduanales();
        AgregarLinea(c);
        var ce = Guid.NewGuid();

        c.AutorizarNivel1Aduanales(ce, DateTimeOffset.UtcNow);

        c.Estado.Should().Be(EstadoComprobacionGastos.AutorizadaNivel1);
        c.AutorizadoPorNivel1.Should().Be(ce);
        c.FechaAutorizacionNivel1.Should().NotBeNull();
        c.AutorizadoPor.Should().BeNull(); // nivel 2 todavía pendiente
    }

    [Fact]
    public void AutorizarNivel1_sin_lineas_rechaza()
    {
        var c = CrearAduanales();
        var act = () => c.AutorizarNivel1Aduanales(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_SIN_LINEAS");
    }

    [Fact]
    public void AutorizarNivel2_solo_desde_AutorizadaNivel1()
    {
        var c = CrearAduanales();
        AgregarLinea(c);
        var act = () => c.AutorizarNivel2Aduanales(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_NIVEL2_NO_AUTORIZABLE");
    }

    [Fact]
    public void AutorizarNivel2_mismo_usuario_que_nivel1_rechaza()
    {
        var c = CrearAduanales();
        AgregarLinea(c);
        var ce = Guid.NewGuid();
        c.AutorizarNivel1Aduanales(ce, DateTimeOffset.UtcNow);

        var act = () => c.AutorizarNivel2Aduanales(ce, DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_NIVEL2_MISMO_USUARIO");
    }

    [Fact]
    public void Ciclo_completo_aduanales_AutorizadaNivel1_Autorizada_Aplicada()
    {
        var c = CrearAduanales();
        AgregarLinea(c, total: 5800m);
        AgregarLinea(c, total: 2320m);

        var ce = Guid.NewGuid();
        var df = Guid.NewGuid();

        c.AutorizarNivel1Aduanales(ce, DateTimeOffset.UtcNow);
        c.AutorizarNivel2Aduanales(df, DateTimeOffset.UtcNow);
        c.Aplicar(df, DateTimeOffset.UtcNow);

        c.Estado.Should().Be(EstadoComprobacionGastos.Aplicada);
        c.AutorizadoPorNivel1.Should().Be(ce);
        c.AutorizadoPor.Should().Be(df);
        c.MontoTotal.Should().Be(8120m);
    }

    [Fact]
    public void Rechazar_desde_AutorizadaNivel1_OK()
    {
        var c = CrearAduanales();
        AgregarLinea(c);
        c.AutorizarNivel1Aduanales(Guid.NewGuid(), DateTimeOffset.UtcNow);

        c.Rechazar(Guid.NewGuid(), "Documentación incompleta", DateTimeOffset.UtcNow);

        c.Estado.Should().Be(EstadoComprobacionGastos.Rechazada);
        c.MotivoRechazo.Should().Be("Documentación incompleta");
    }

    [Fact]
    public void AutorizarNivel2_aduanales_sin_nivel1_previo_rechaza()
    {
        var c = CrearAduanales();
        AgregarLinea(c);
        // estado: Borrador
        var act = () => c.AutorizarNivel2Aduanales(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_NIVEL2_NO_AUTORIZABLE");
    }
}
