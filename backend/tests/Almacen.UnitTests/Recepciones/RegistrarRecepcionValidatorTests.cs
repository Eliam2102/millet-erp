using Millet.Almacen.Application.Recepciones;

namespace Millet.Almacen.UnitTests.Recepciones;

/// <summary>
/// Tests del validator FluentValidation de
/// <see cref="RegistrarRecepcionConFacturaCommand"/> (F2-PR2).
/// Cubre las pre-condiciones del input antes del handler.
/// </summary>
public class RegistrarRecepcionValidatorTests
{
    private readonly RegistrarRecepcionConFacturaValidator _validator = new();

    [Fact]
    public void Command_valido_no_genera_errores()
    {
        var result = _validator.Validate(NuevoComandoValido());
        result.IsValid.Should().BeTrue($"errores inesperados: {string.Join(", ", result.Errors)}");
    }

    [Fact]
    public void Command_sin_oc_id_falla()
    {
        var cmd = NuevoComandoValido() with { OrdenCompraId = Guid.Empty };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(cmd.OrdenCompraId));
    }

    [Fact]
    public void Command_sin_lineas_falla()
    {
        var cmd = NuevoComandoValido() with { Lineas = Array.Empty<RegistrarRecepcionLineaInput>() };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(cmd.Lineas));
    }

    [Fact]
    public void Command_con_fecha_fuera_de_rango_falla()
    {
        var cmd = NuevoComandoValido() with { FechaMovimiento = new DateOnly(1999, 1, 1) };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(cmd.FechaMovimiento));
    }

    [Fact]
    public void Linea_con_cantidad_cero_falla()
    {
        var cmd = NuevoComandoValido() with
        {
            Lineas = new List<RegistrarRecepcionLineaInput>
            {
                new(ArticuloId: Guid.NewGuid(), LineaOcId: null, Cantidad: 0, UbicacionReferencia: null, Comentario: null)
            }
        };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Cantidad"));
    }

    [Fact]
    public void Command_sin_cfdi_ni_uuid_fiscal_falla()
    {
        var cmd = NuevoComandoValido() with { CfdiRecibidoId = null, CfdiUuidFiscal = null };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(cmd.CfdiRecibidoId));
    }

    [Fact]
    public void Command_con_solo_uuid_fiscal_pasa()
    {
        var cmd = NuevoComandoValido() with
        {
            CfdiRecibidoId = null,
            CfdiUuidFiscal = "AD662D33-6934-459C-A128-BDF0393E0062",
        };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeTrue($"errores inesperados: {string.Join(", ", result.Errors)}");
    }

    [Fact]
    public void Command_con_uuid_fiscal_malformado_falla()
    {
        var cmd = NuevoComandoValido() with
        {
            CfdiRecibidoId = null,
            CfdiUuidFiscal = "no-es-un-uuid",
        };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(cmd.CfdiUuidFiscal));
    }

    [Fact]
    public void Linea_con_articulo_vacio_falla()
    {
        var cmd = NuevoComandoValido() with
        {
            Lineas = new List<RegistrarRecepcionLineaInput>
            {
                new(ArticuloId: Guid.Empty, LineaOcId: null, Cantidad: 5, UbicacionReferencia: null, Comentario: null)
            }
        };
        var result = _validator.Validate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("ArticuloId"));
    }

    private static RegistrarRecepcionConFacturaCommand NuevoComandoValido() => new(
        OrdenCompraId: Guid.NewGuid(),
        FechaMovimiento: new DateOnly(2026, 5, 23),
        CfdiRecibidoId: Guid.NewGuid(),
        CfdiUuidFiscal: null,
        Observaciones: null,
        Lineas: new List<RegistrarRecepcionLineaInput>
        {
            new(
                ArticuloId: Guid.NewGuid(),
                LineaOcId: Guid.NewGuid(),
                Cantidad: 10,
                UbicacionReferencia: "A-3-2",
                Comentario: null,
                UbicacionId: Guid.NewGuid())
        });
}
