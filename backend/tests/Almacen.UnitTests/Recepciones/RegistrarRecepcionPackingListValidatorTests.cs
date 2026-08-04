using Millet.Almacen.Application.Recepciones;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// Tests del validator de
/// <see cref="RegistrarRecepcionConPackingListCommand"/> (F3-PR1).
/// </summary>
public class RegistrarRecepcionPackingListValidatorTests
{
    private readonly RegistrarRecepcionConPackingListValidator _validator = new();

    [Fact]
    public void Command_valido_pasa()
    {
        var cmd = NuevoComandoValido();
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Command_sin_packing_list_blob_ref_falla()
    {
        var cmd = NuevoComandoValido() with { PackingListBlobRef = "" };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(cmd.PackingListBlobRef));
    }

    [Fact]
    public void Command_sin_oc_id_falla()
    {
        var cmd = NuevoComandoValido() with { OrdenCompraId = Guid.Empty };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Command_sin_lineas_falla()
    {
        var cmd = NuevoComandoValido() with { Lineas = Array.Empty<RegistrarRecepcionLineaInput>() };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }

    private static RegistrarRecepcionConPackingListCommand NuevoComandoValido() => new(
        OrdenCompraId: Guid.NewGuid(),
        FechaMovimiento: new DateOnly(2026, 5, 23),
        PackingListBlobRef: "blob://packing-lists/2026/abc.pdf",
        Observaciones: null,
        Lineas: new List<RegistrarRecepcionLineaInput>
        {
            new(
                ArticuloId: Guid.NewGuid(),
                LineaOcId: null,
                Cantidad: 100,
                UbicacionReferencia: null,
                Comentario: null,
                UbicacionId: Guid.NewGuid())
        });
}
