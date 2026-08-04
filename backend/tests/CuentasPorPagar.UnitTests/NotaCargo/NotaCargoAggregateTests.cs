using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.NotaCargo;

public sealed class NotaCargoAggregateTests
{
    private static Domain.NotaCargo.NotaCargo Crear(decimal monto = 500m, string concepto = "Descuento por daño") =>
        Domain.NotaCargo.NotaCargo.Crear(
            empresaId: Guid.NewGuid(),
            folio: FolioInternoNotaCargo.FromAnioSecuencial(2026, 1),
            proveedorId: Guid.NewGuid(),
            sucursalId: Guid.NewGuid(),
            concepto: concepto,
            conceptoContableId: null,
            monto: monto,
            moneda: "MXN",
            tipoCambio: null,
            facturaOrigenId: Guid.NewGuid(),
            devolucionAProveedorId: null,
            creadoPor: Guid.NewGuid(),
            ahora: DateTimeOffset.UtcNow);

    [Fact]
    public void Crear_nace_Borrador_con_folio_y_monto()
    {
        var n = Crear(monto: 500m);
        n.Estado.Should().Be(EstadoNotaCargo.Borrador);
        n.Monto.Should().Be(500m);
        n.Folio.Valor.Should().Be("NCG-2026-000001");
        n.FolioAnio.Should().Be((short)2026);
    }

    [Fact]
    public void Crear_rechaza_monto_no_positivo()
    {
        var act = () => Crear(monto: 0m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NCG_MONTO_INVALIDO");
    }

    [Fact]
    public void Crear_rechaza_concepto_vacio()
    {
        var act = () => Crear(concepto: "   ");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NCG_CONCEPTO_VACIO");
    }

    [Fact]
    public void Autorizar_desde_Borrador_pasa_a_Autorizada()
    {
        var n = Crear();
        n.Autorizar(usuarioId: Guid.NewGuid(), DateTimeOffset.UtcNow);
        n.Estado.Should().Be(EstadoNotaCargo.Autorizada);
        n.FechaAutorizacion.Should().NotBeNull();
        n.AutorizadoPor.Should().NotBeNull();
    }

    [Fact]
    public void Autorizar_solo_permitido_desde_Borrador()
    {
        var n = Crear();
        n.Autorizar(usuarioId: null, DateTimeOffset.UtcNow);
        var act = () => n.Autorizar(usuarioId: null, DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NCG_NO_AUTORIZABLE");
    }

    [Fact]
    public void Aplicar_solo_permitido_desde_Autorizada()
    {
        var n = Crear();
        var act = () => n.Aplicar(usuarioId: null, DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NCG_NO_APLICABLE");
    }

    [Fact]
    public void Ciclo_Borrador_Autorizada_Aplicada()
    {
        var n = Crear();
        n.Autorizar(usuarioId: null, DateTimeOffset.UtcNow);
        n.Aplicar(usuarioId: null, DateTimeOffset.UtcNow);
        n.Estado.Should().Be(EstadoNotaCargo.Aplicada);
        n.FechaAplicacion.Should().NotBeNull();
    }

    [Fact]
    public void Formalizar_solo_permitido_desde_Aplicada()
    {
        var n = Crear();
        var act = () => n.Formalizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NCG_NO_FORMALIZABLE");
    }

    [Fact]
    public void Cancelar_desde_Borrador_o_Autorizada_OK()
    {
        var n = Crear();
        n.Cancelar("error", DateTimeOffset.UtcNow);
        n.Estado.Should().Be(EstadoNotaCargo.Cancelada);
        n.MotivoCancelacion.Should().Be("error");
    }

    [Fact]
    public void Cancelar_desde_Aplicada_no_permitido()
    {
        var n = Crear();
        n.Autorizar(usuarioId: null, DateTimeOffset.UtcNow);
        n.Aplicar(usuarioId: null, DateTimeOffset.UtcNow);
        var act = () => n.Cancelar("error", DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NCG_NO_CANCELABLE");
    }
}
