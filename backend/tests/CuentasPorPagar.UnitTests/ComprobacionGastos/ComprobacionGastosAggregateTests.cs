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
        var c = Crear();
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
    public void EnviarARevision_con_lineas_pasa_a_PorRevisar()
    {
        var c = Crear();
        AgregarCfdi(c);
        c.EnviarARevision(DateTimeOffset.UtcNow);
        c.Estado.Should().Be(EstadoComprobacionGastos.PorRevisar);
        c.FechaEnvioRevision.Should().NotBeNull();
    }

    [Fact]
    public void Autorizar_desde_Borrador_OK()
    {
        var c = Crear();
        AgregarCfdi(c);
        c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        c.Estado.Should().Be(EstadoComprobacionGastos.Autorizada);
        c.AutorizadoPor.Should().NotBeNull();
        c.FechaAutorizacion.Should().NotBeNull();
    }

    [Fact]
    public void Autorizar_desde_PorRevisar_OK()
    {
        var c = Crear();
        AgregarCfdi(c);
        c.EnviarARevision(DateTimeOffset.UtcNow);
        c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        c.Estado.Should().Be(EstadoComprobacionGastos.Autorizada);
    }

    [Fact]
    public void Autorizar_sin_lineas_rechaza()
    {
        var c = Crear();
        var act = () => c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_SIN_LINEAS");
    }

    [Fact]
    public void Aplicar_solo_permitido_desde_Autorizada()
    {
        var c = Crear();
        AgregarCfdi(c);
        var act = () => c.Aplicar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_NO_APLICABLE");
    }

    [Fact]
    public void Ciclo_Borrador_Autorizada_Aplicada()
    {
        var c = Crear();
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
        var c = Crear();
        AgregarCfdi(c);
        c.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        c.Aplicar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var act = () => c.Rechazar(Guid.NewGuid(), "x", DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMP_NO_RECHAZABLE");
    }
}
