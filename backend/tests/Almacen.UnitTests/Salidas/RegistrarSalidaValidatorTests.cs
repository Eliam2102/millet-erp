using Millet.Almacen.Application.Salidas;

namespace Millet.Almacen.UnitTests.Salidas;

/// <summary>
/// Tests del validator de
/// <see cref="RegistrarSalidaConRequisicionCommand"/> (F4-PR1).
/// </summary>
public class RegistrarSalidaValidatorTests
{
    private readonly RegistrarSalidaConRequisicionValidator _validator = new();

    [Fact]
    public void Command_valido_pasa()
    {
        var result = _validator.Validate(NuevoComandoValido());
        result.IsValid.Should().BeTrue($"errores: {string.Join(", ", result.Errors)}");
    }

    [Fact]
    public void Command_sin_rq_falla()
    {
        var cmd = NuevoComandoValido() with { RequisicionId = Guid.Empty };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(cmd.RequisicionId));
    }

    [Fact]
    public void Linea_sin_ubicacion_falla()
    {
        // Salida-por-línea C2: el bin es obligatorio por línea (mutación:
        // quitar la regla NotNull sobre UbicacionId → este test pasa).
        var cmd = NuevoComandoValido() with
        {
            Lineas = new List<RegistrarSalidaLineaInput>
            {
                new(Guid.NewGuid(), Guid.NewGuid(), 5, null, null, null, null, UbicacionId: null)
            }
        };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("UbicacionId"));
    }

    [Fact]
    public void Command_sin_lineas_falla()
    {
        var cmd = NuevoComandoValido() with { Lineas = Array.Empty<RegistrarSalidaLineaInput>() };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Linea_con_cantidad_cero_falla()
    {
        var cmd = NuevoComandoValido() with
        {
            Lineas = new List<RegistrarSalidaLineaInput>
            {
                new(Guid.NewGuid(), null, 0, null, null, null, null, Guid.NewGuid())
            }
        };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Cantidad"));
    }

    [Fact]
    public void Fecha_anio_1999_falla()
    {
        var cmd = NuevoComandoValido() with { FechaMovimiento = new DateOnly(1999, 1, 1) };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
    }

    private static RegistrarSalidaConRequisicionCommand NuevoComandoValido() => new(
        RequisicionId: Guid.NewGuid(),
        FechaMovimiento: new DateOnly(2026, 5, 23),
        PersonaDestinatariaId: Guid.NewGuid(),
        Observaciones: null,
        Lineas: new List<RegistrarSalidaLineaInput>
        {
            new(
                ArticuloId: Guid.NewGuid(),
                LineaRqId: Guid.NewGuid(),
                Cantidad: 5,
                CentroCostoId: null,
                ProyectoId: null,
                UbicacionReferencia: null,
                Comentario: null,
                UbicacionId: Guid.NewGuid())
        });
}
