using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;

namespace Millet.Almacen.UnitTests.Vales;

/// <summary>
/// Tests de validators del PR F5: vale + regularización + devolución
/// interna + MAT-REV.
/// </summary>
public class ValeValidatorsTests
{
    [Fact]
    public void Vale_command_valido_pasa()
    {
        var v = new RegistrarSalidaPorValeValidator();
        var cmd = new RegistrarSalidaPorValeCommand(
            FechaMovimiento: new DateOnly(2026, 5, 23),
            ValeBlobRef: "blob://vales/test.pdf",
            PersonaDestinatariaId: Guid.NewGuid(),
            Observaciones: null,
            Lineas: new[] { new RegistrarSalidaLineaInput(
                Guid.NewGuid(), null, 5, null, null, null, null, UbicacionId: Guid.NewGuid()) });
        v.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Vale_sin_blob_ref_falla()
    {
        var v = new RegistrarSalidaPorValeValidator();
        var cmd = new RegistrarSalidaPorValeCommand(
            FechaMovimiento: new DateOnly(2026, 5, 23),
            ValeBlobRef: "",
            PersonaDestinatariaId: null,
            Observaciones: null,
            Lineas: new[] { new RegistrarSalidaLineaInput(
                Guid.NewGuid(), null, 5, null, null, null, null, UbicacionId: Guid.NewGuid()) });
        v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Vale_linea_sin_ubicacion_falla()
    {
        // Salida-por-línea (vale): el bin es obligatorio por línea (mutación:
        // quitar la regla NotNull del validador del vale → este test pasa).
        var v = new RegistrarSalidaPorValeValidator();
        var cmd = new RegistrarSalidaPorValeCommand(
            FechaMovimiento: new DateOnly(2026, 5, 23),
            ValeBlobRef: "blob://vales/test.pdf",
            PersonaDestinatariaId: null,
            Observaciones: null,
            Lineas: new[] { new RegistrarSalidaLineaInput(
                Guid.NewGuid(), null, 5, null, null, null, null, UbicacionId: null) });
        var result = v.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("UbicacionId"));
    }

    [Fact]
    public void Regularizar_sin_movimiento_id_falla()
    {
        var v = new RegularizarSalidaPorValeValidator();
        var cmd = new RegularizarSalidaPorValeCommand(Guid.Empty, Guid.NewGuid());
        v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Integro")]
    [InlineData("UsadoParcial")]
    [InlineData("Danado")]
    public void DevInt_acepta_estados_validos(string estado)
    {
        var v = new AplicarDevolucionInternaValidator();
        var cmd = new AplicarDevolucionInternaCommand(
            SalidaOrigenId: Guid.NewGuid(),
            SubAlmacenDestinoId: Guid.NewGuid(),
            FechaMovimiento: new DateOnly(2026, 5, 23),
            EstadoMaterial: estado,
            Motivo: "test",
            Observaciones: null,
            Lineas: new[] { new DevolucionInternaLineaInput(Guid.NewGuid(), 2, UbicacionId: Guid.NewGuid()) });
        v.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void DevInt_rechaza_estado_invalido()
    {
        var v = new AplicarDevolucionInternaValidator();
        var cmd = new AplicarDevolucionInternaCommand(
            SalidaOrigenId: Guid.NewGuid(),
            SubAlmacenDestinoId: Guid.NewGuid(),
            FechaMovimiento: new DateOnly(2026, 5, 23),
            EstadoMaterial: "Inventado",
            Motivo: "test",
            Observaciones: null,
            Lineas: new[] { new DevolucionInternaLineaInput(Guid.NewGuid(), 2) });
        v.Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void BajaPorDano_command_valido_pasa()
    {
        var v = new BajaPorDanoValidator();
        var cmd = new BajaPorDanoCommand(
            SubAlmacenMatRevId: Guid.NewGuid(),
            FechaMovimiento: new DateOnly(2026, 5, 23),
            Motivo: "Material vencido",
            Lineas: new[] { new MatRevLineaInput(Guid.NewGuid(), 3) });
        v.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Reincorporacion_command_valido_pasa()
    {
        var v = new ReincorporacionTrasRevisionValidator();
        var cmd = new ReincorporacionTrasRevisionCommand(
            SubAlmacenDestinoId: Guid.NewGuid(),
            FechaMovimiento: new DateOnly(2026, 5, 23),
            Motivo: "Material apto tras revisión",
            Lineas: new[] { new MatRevLineaInput(Guid.NewGuid(), 3, UbicacionId: Guid.NewGuid()) });
        v.Validate(cmd).IsValid.Should().BeTrue();
    }
}
