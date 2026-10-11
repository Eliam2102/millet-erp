using FluentAssertions;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.NotaCargo;

/// <summary>
/// PRU-B (prueba de Uziel, CXP-14): quien crea la nota de cargo no puede autorizarla.
/// Las notas automáticas (sin creador) las autoriza cualquier usuario identificado.
/// </summary>
public sealed class NotaCargoAutorizacionTests
{
    private static Domain.NotaCargo.NotaCargo Crear(Guid? creadoPor) =>
        Domain.NotaCargo.NotaCargo.Crear(
            empresaId: Guid.NewGuid(),
            folio: FolioInternoNotaCargo.FromAnioSecuencial(2026, 8),
            proveedorId: Guid.NewGuid(),
            sucursalId: Guid.NewGuid(),
            concepto: "Cargo por flete",
            conceptoContableId: null,
            monto: 50m,
            moneda: "MXN",
            tipoCambio: null,
            facturaOrigenId: null,
            devolucionAProveedorId: null,
            creadoPor: creadoPor,
            ahora: DateTimeOffset.UtcNow);

    [Fact]
    public void Quien_la_creo_no_puede_autorizarla()
    {
        var usuario = Guid.NewGuid();
        var nota = Crear(usuario);
        var act = () => nota.Autorizar(usuario, DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("NCG_AUTOAUTORIZACION");
        nota.Estado.Should().Be(EstadoNotaCargo.Borrador);
    }

    [Fact]
    public void Otra_persona_la_autoriza()
    {
        var nota = Crear(Guid.NewGuid());
        var autoriza = Guid.NewGuid();
        nota.Autorizar(autoriza, DateTimeOffset.UtcNow);
        nota.Estado.Should().Be(EstadoNotaCargo.Autorizada);
        nota.AutorizadoPor.Should().Be(autoriza);
    }

    [Fact]
    public void Sin_usuario_identificado_se_rechaza()
    {
        var act = () => Crear(Guid.NewGuid()).Autorizar(null, DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("NCG_USUARIO_REQUERIDO");
    }

    [Fact]
    public void Nota_automatica_sin_creador_la_autoriza_un_usuario()
    {
        var nota = Crear(null);
        nota.Autorizar(Guid.NewGuid(), DateTimeOffset.UtcNow);
        nota.Estado.Should().Be(EstadoNotaCargo.Autorizada);
    }
}
