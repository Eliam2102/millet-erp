using Millet.Compras.Application.Oc.CrearOrdenCompraVacia;

namespace Millet.Compras.UnitTests.Oc.Application;

/// <summary>
/// Tests del FluentValidation de <see cref="CrearOrdenCompraVaciaCommand"/>.
/// Verifica input shape — las invariantes del agregado se cubren en
/// <c>Oc.Domain.OrdenCompraTests</c>.
/// </summary>
public class CrearOrdenCompraVaciaValidatorTests
{
    private readonly CrearOrdenCompraVaciaValidator _validator = new();

    private static CrearOrdenCompraVaciaCommand Valid(
        string sucursalCodigo = "MID",
        short folioAnio = 2026,
        string moneda = "MXN",
        decimal? tipoCambio = null,
        bool sinRequisicionPrevia = false,
        string? motivoSinRequisicion = null,
        string? observaciones = null,
        Guid? sucursalDestinoId = null,
        Guid? proveedorId = null,
        Guid? encargadoComprasId = null,
        Guid? ocOrigenId = null) =>
        new(
            SucursalDestinoId: sucursalDestinoId ?? Guid.CreateVersion7(),
            SucursalCodigo: sucursalCodigo,
            FolioAnio: folioAnio,
            ProveedorId: proveedorId ?? Guid.CreateVersion7(),
            CondicionesPagoId: Guid.CreateVersion7(),
            UsoPrincipalId: Guid.CreateVersion7(),
            FechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            Moneda: moneda,
            TipoCambio: tipoCambio,
            SinRequisicionPrevia: sinRequisicionPrevia,
            MotivoSinRequisicion: motivoSinRequisicion,
            Observaciones: observaciones,
            EncargadoComprasId: encargadoComprasId,
            OcOrigenId: ocOrigenId);

    [Fact]
    public void Should_PassValidation_When_CommandIsValid()
    {
        var result = _validator.Validate(Valid());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_FailValidation_When_SucursalDestinoIsEmpty()
    {
        var result = _validator.Validate(Valid(sucursalDestinoId: Guid.Empty));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "SUCURSAL_REQUERIDA");
    }

    [Fact]
    public void Should_FailValidation_When_ProveedorIsEmpty()
    {
        var result = _validator.Validate(Valid(proveedorId: Guid.Empty));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "PROVEEDOR_REQUERIDO");
    }

    [Theory]
    [InlineData("M")]            // 1 letra
    [InlineData("MERIDA")]       // 6 letras
    [InlineData("mid")]          // minúsculas
    [InlineData("M1D")]          // dígito
    [InlineData("")]
    public void Should_FailValidation_When_SucursalCodigoIsInvalid(string codigo)
    {
        var result = _validator.Validate(Valid(sucursalCodigo: codigo));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CrearOrdenCompraVaciaCommand.SucursalCodigo));
    }

    [Theory]
    [InlineData((short)1999)]
    [InlineData((short)2101)]
    public void Should_FailValidation_When_FolioAnioOutOfRange(short anio)
    {
        var result = _validator.Validate(Valid(folioAnio: anio));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "FOLIO_ANIO_FUERA_DE_RANGO");
    }

    [Theory]
    [InlineData("")]
    [InlineData("MX")]            // 2 chars
    [InlineData("MXNN")]          // 4 chars
    public void Should_FailValidation_When_MonedaIsInvalid(string moneda)
    {
        var result = _validator.Validate(Valid(moneda: moneda));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CrearOrdenCompraVaciaCommand.Moneda));
    }

    [Fact]
    public void Should_FailValidation_When_TipoCambioIsZero()
    {
        var result = _validator.Validate(Valid(moneda: "USD", tipoCambio: 0m));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "TIPO_CAMBIO_INVALIDO");
    }

    [Fact]
    public void Should_FailValidation_When_TipoCambioIsNegative()
    {
        var result = _validator.Validate(Valid(moneda: "USD", tipoCambio: -1m));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "TIPO_CAMBIO_INVALIDO");
    }

    [Fact]
    public void Should_FailValidation_When_ObservacionesExceeds1000Chars()
    {
        var result = _validator.Validate(Valid(observaciones: new string('x', 1001)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "OBSERVACIONES_DEMASIADO_LARGAS");
    }

    [Fact]
    public void Should_PassValidation_When_ObservacionesAtBoundary()
    {
        var result = _validator.Validate(Valid(observaciones: new string('x', 1000)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_FailValidation_When_SinRqPreviaWithoutMotivo()
    {
        var result = _validator.Validate(Valid(sinRequisicionPrevia: true, motivoSinRequisicion: null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "MOTIVO_SIN_RQ_REQUERIDO");
    }

    [Fact]
    public void Should_FailValidation_When_MotivoSinRqExceeds500Chars()
    {
        var result = _validator.Validate(
            Valid(sinRequisicionPrevia: true, motivoSinRequisicion: new string('x', 501)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "MOTIVO_SIN_RQ_DEMASIADO_LARGO");
    }

    [Fact]
    public void Should_PassValidation_When_SinRqPreviaWithMotivo()
    {
        var result = _validator.Validate(Valid(
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Compra emergencia mantenimiento"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_FailValidation_When_EncargadoComprasIsEmpty()
    {
        var result = _validator.Validate(Valid(encargadoComprasId: Guid.Empty));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "ENCARGADO_INVALIDO");
    }

    [Fact]
    public void Should_PassValidation_When_EncargadoIsNull()
    {
        var result = _validator.Validate(Valid(encargadoComprasId: null));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_FailValidation_When_OcOrigenIsEmpty()
    {
        var result = _validator.Validate(Valid(ocOrigenId: Guid.Empty));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "OC_ORIGEN_INVALIDA");
    }
}
