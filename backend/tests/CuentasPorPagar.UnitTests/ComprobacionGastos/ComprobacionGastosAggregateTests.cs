using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.ComprobacionGastos;

public sealed class ComprobacionGastosAggregateTests
{
    private static Domain.ComprobacionGastos.ComprobacionGastos Crear(
        TipoComprobacionGastos tipo = TipoComprobacionGastos.ReembolsoCajaChica,
        string moneda = "MXN") =>
        Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            empresaId: Guid.NewGuid(),
            tipo: tipo,
            sucursalId: Guid.NewGuid(),
            responsableId: Guid.NewGuid(),
            fechaInicio: new DateOnly(2026, 5, 1),
            fechaFin: new DateOnly(2026, 5, 15),
            moneda: moneda,
            observaciones: null,
            ahora: DateTimeOffset.UtcNow,
            numeroPedimento: tipo == TipoComprobacionGastos.GastosAduanales ? "26-13-3018-000123" : null,
            destinoReposicion: tipo == TipoComprobacionGastos.ReembolsoCajaChica
                ? DestinoReposicionCaja.CuentaSucursal
                : null);

    private static void AgregarCfdi(
        Domain.ComprobacionGastos.ComprobacionGastos c,
        decimal total = 116m)
    {
        c.AgregarLinea(
            facturaProveedorId: Guid.NewGuid(),
            cfdiRecibidoId: null,
            uuidCfdi: null,
            proveedorId: Guid.NewGuid(),
            folioProveedor: "F-001",
            fechaCfdi: DateTimeOffset.UtcNow,
            subtotal: total / 1.16m,
            impuestosTrasladados: total - (total / 1.16m),
            retenciones: 0m,
            total: total,
            moneda: "MXN",
            concepto: "Papelería");
    }

    [Fact]
    public void Crear_nace_Borrador_con_monto_total_cero()
    {
        var c = Crear();
        c.Estado.Should().Be(EstadoComprobacionGastos.Borrador);
        c.MontoTotal.Should().Be(0m);
        c.Lineas.Should().BeEmpty();
    }

    [Fact]
    public void Crear_rechaza_periodo_invalido()
    {
        var act = () => Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            empresaId: Guid.NewGuid(),
            tipo: TipoComprobacionGastos.ReembolsoCajaChica,
            sucursalId: Guid.NewGuid(),
            responsableId: Guid.NewGuid(),
            fechaInicio: new DateOnly(2026, 5, 15),
            fechaFin: new DateOnly(2026, 5, 1),
            moneda: "MXN",
            observaciones: null,
            ahora: DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_PERIODO_INVALIDO");
    }

    [Fact]
    public void Crear_rechaza_sucursal_vacia()
    {
        var act = () => Domain.ComprobacionGastos.ComprobacionGastos.Crear(
            empresaId: Guid.NewGuid(),
            tipo: TipoComprobacionGastos.ReembolsoCajaChica,
            sucursalId: Guid.Empty,
            responsableId: Guid.NewGuid(),
            fechaInicio: new DateOnly(2026, 5, 1),
            fechaFin: new DateOnly(2026, 5, 15),
            moneda: "MXN",
            observaciones: null,
            ahora: DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_SUCURSAL_VACIA");
    }

    [Fact]
    public void AgregarLinea_suma_al_monto_total()
    {
        var c = Crear();
        AgregarCfdi(c, total: 116m);
        AgregarCfdi(c, total: 232m);

        c.Lineas.Should().HaveCount(2);
        c.MontoTotal.Should().Be(348m);
    }

    [Fact]
    public void AgregarLinea_rechaza_moneda_diferente()
    {
        var c = Crear(moneda: "MXN");
        var act = () => c.AgregarLinea(
            facturaProveedorId: Guid.NewGuid(), cfdiRecibidoId: null, uuidCfdi: null,
            proveedorId: Guid.NewGuid(), folioProveedor: null, fechaCfdi: DateTimeOffset.UtcNow,
            subtotal: 100m, impuestosTrasladados: 16m, retenciones: 0m, total: 116m,
            moneda: "USD", concepto: null);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_LINEA_MONEDA_MISMATCH");
    }

    [Fact]
    public void AgregarLinea_solo_permitido_en_Borrador()
    {
        var c = Crear(TipoComprobacionGastos.Viaticos);
        AgregarCfdi(c);
        c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var act = () => AgregarCfdi(c);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_NO_EDITABLE");
    }

    [Fact]
    public void EnviarARevision_sin_lineas_rechaza()
    {
        var c = Crear();
        var act = () => c.EnviarARevision(DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_SIN_LINEAS");
    }

    [Fact]
    public void EnviarARevision_no_caja_chica_pasa_a_PorRevisar()
    {
        var c = Crear(TipoComprobacionGastos.Viaticos);
        AgregarCfdi(c);
        c.EnviarARevision(DateTimeOffset.UtcNow);
        c.Estado.Should().Be(EstadoComprobacionGastos.PorRevisar);
        c.FechaEnvioRevision.Should().NotBeNull();
    }

    [Fact]
    public void Autorizar_desde_Borrador_OK()
    {
        var c = Crear(TipoComprobacionGastos.Viaticos);
        AgregarCfdi(c);
        c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        c.Estado.Should().Be(EstadoComprobacionGastos.Autorizada);
        c.AutorizadoPor.Should().NotBeNull();
        c.FechaAutorizacion.Should().NotBeNull();
    }

    [Fact]
    public void Autorizar_desde_PorRevisar_OK()
    {
        var c = Crear(TipoComprobacionGastos.Viaticos);
        AgregarCfdi(c);
        c.EnviarARevision(DateTimeOffset.UtcNow);
        c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        c.Estado.Should().Be(EstadoComprobacionGastos.Autorizada);
    }

    [Fact]
    public void Autorizar_sin_lineas_rechaza()
    {
        var c = Crear(TipoComprobacionGastos.Viaticos);
        var act = () => c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_SIN_LINEAS");
    }

    [Fact]
    public void Aplicar_solo_permitido_desde_Autorizada()
    {
        var c = Crear(TipoComprobacionGastos.Viaticos);
        AgregarCfdi(c);
        var act = () => c.Aplicar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_NO_APLICABLE");
    }

    [Fact]
    public void Ciclo_Borrador_Autorizada_Aplicada()
    {
        var c = Crear(TipoComprobacionGastos.Viaticos);
        AgregarCfdi(c, total: 580m);
        c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        c.Aplicar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        c.Estado.Should().Be(EstadoComprobacionGastos.Aplicada);
        c.MontoTotal.Should().Be(580m);
    }

    [Fact]
    public void Rechazar_desde_Borrador_OK_con_motivo()
    {
        var c = Crear();
        c.Rechazar(Guid.NewGuid(), "CFDI no válido", DateTimeOffset.UtcNow);
        c.Estado.Should().Be(EstadoComprobacionGastos.Rechazada);
        c.MotivoRechazo.Should().Be("CFDI no válido");
    }

    [Fact]
    public void Rechazar_sin_motivo_lanza()
    {
        var c = Crear();
        var act = () => c.Rechazar(Guid.NewGuid(), "   ", DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_MOTIVO_VACIO");
    }

    [Fact]
    public void Rechazar_desde_Aplicada_no_permitido()
    {
        var c = Crear(TipoComprobacionGastos.Viaticos);
        AgregarCfdi(c);
        c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        c.Aplicar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var act = () => c.Rechazar(Guid.NewGuid(), "x", DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_NO_RECHAZABLE");
    }

    private static Domain.ComprobacionGastos.ComprobacionGastos CajaChicaConLineas()
    {
        var c = Crear();
        AgregarCfdi(c);
        return c;
    }

    private static void Code(Action act, string code) =>
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == code);

    [Fact]
    public void EnviarARevision_caja_chica_pasa_a_PendienteAprobarPasivo()
    {
        var c = CajaChicaConLineas();
        c.EnviarARevision(DateTimeOffset.UtcNow);
        c.Estado.Should().Be(EstadoComprobacionGastos.PendienteAprobarPasivo);
        c.FechaEnvioRevision.Should().NotBeNull();
    }

    [Fact]
    public void EnviarARevision_aduanal_pasa_a_PorRevisar()
    {
        var c = Crear(TipoComprobacionGastos.GastosAduanales);
        AgregarCfdi(c);
        c.EnviarARevision(DateTimeOffset.UtcNow);
        c.Estado.Should().Be(EstadoComprobacionGastos.PorRevisar);
    }

    [Fact]
    public void AprobarPasivo_desde_PendienteAprobarPasivo_aplica()
    {
        var c = CajaChicaConLineas();
        c.EnviarARevision(DateTimeOffset.UtcNow);
        var u = Guid.NewGuid();
        var t = DateTimeOffset.UtcNow;
        c.AprobarPasivo(u, t);
        c.Estado.Should().Be(EstadoComprobacionGastos.Aplicada);
        c.AutorizadoPor.Should().Be(u);
        c.AplicadoPor.Should().Be(u);
        c.FechaAutorizacion.Should().Be(t);
        c.FechaAplicacion.Should().Be(t);
    }

    [Fact]
    public void AprobarPasivo_desde_PorRevisar_legado_aplica()
    {
        var c = CajaChicaConLineas();
        typeof(Domain.ComprobacionGastos.ComprobacionGastos).GetProperty("Estado")!
            .SetValue(c, EstadoComprobacionGastos.PorRevisar);
        c.AprobarPasivo(Guid.NewGuid(), DateTimeOffset.UtcNow);
        c.Estado.Should().Be(EstadoComprobacionGastos.Aplicada);
    }

    [Fact]
    public void AprobarPasivo_desde_Borrador_Rechazada_Aplicada_no_permitido()
    {
        var borrador = CajaChicaConLineas();
        Code(() => borrador.AprobarPasivo(Guid.NewGuid(), DateTimeOffset.UtcNow), "COMP_NO_APROBABLE_PASIVO");

        var rechazada = CajaChicaConLineas();
        rechazada.EnviarARevision(DateTimeOffset.UtcNow);
        rechazada.Rechazar(Guid.NewGuid(), "x", DateTimeOffset.UtcNow);
        Code(() => rechazada.AprobarPasivo(Guid.NewGuid(), DateTimeOffset.UtcNow), "COMP_NO_APROBABLE_PASIVO");

        var aplicada = CajaChicaConLineas();
        aplicada.EnviarARevision(DateTimeOffset.UtcNow);
        aplicada.AprobarPasivo(Guid.NewGuid(), DateTimeOffset.UtcNow);
        Code(() => aplicada.AprobarPasivo(Guid.NewGuid(), DateTimeOffset.UtcNow), "COMP_NO_APROBABLE_PASIVO");
    }

    [Fact]
    public void AprobarPasivo_aduanal_solo_caja_chica()
    {
        var c = Crear(TipoComprobacionGastos.GastosAduanales);
        AgregarCfdi(c);
        c.EnviarARevision(DateTimeOffset.UtcNow);
        Code(() => c.AprobarPasivo(Guid.NewGuid(), DateTimeOffset.UtcNow), "COMP_APROBAR_PASIVO_SOLO_CAJA_CHICA");
    }

    [Fact]
    public void Rechazar_desde_PendienteAprobarPasivo_con_y_sin_motivo()
    {
        var sin = CajaChicaConLineas();
        sin.EnviarARevision(DateTimeOffset.UtcNow);
        Code(() => sin.Rechazar(Guid.NewGuid(), " ", DateTimeOffset.UtcNow), "COMP_MOTIVO_VACIO");

        var con = CajaChicaConLineas();
        con.EnviarARevision(DateTimeOffset.UtcNow);
        con.Rechazar(Guid.NewGuid(), "Falta soporte", DateTimeOffset.UtcNow);
        con.Estado.Should().Be(EstadoComprobacionGastos.Rechazada);
        con.MotivoRechazo.Should().Be("Falta soporte");
    }

    [Fact]
    public void Autorizar_caja_chica_requiere_AprobarPasivo()
    {
        var c = CajaChicaConLineas();
        Code(() => c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow), "COMP_CAJA_CHICA_REQUIERE_APROBAR_PASIVO");
    }
}
