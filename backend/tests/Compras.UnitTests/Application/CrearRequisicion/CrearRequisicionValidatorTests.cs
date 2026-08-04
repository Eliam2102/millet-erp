using Millet.Compras.Application.CrearRequisicion;
using Millet.Compras.Domain;

namespace Millet.Compras.UnitTests.Application.CrearRequisicion;

public class CrearRequisicionValidatorTests
{
    private readonly CrearRequisicionValidator _validator = new();

    private static CrearRequisicionCommand Valid(
        string sucursalCodigo = "MID",
        short folioAnio = 2026,
        string? descripcion = null,
        Guid? requisitanteId = null,
        Guid? sucursalId = null) =>
        new(
            SucursalId: sucursalId ?? Guid.CreateVersion7(),
            SucursalCodigo: sucursalCodigo,
            FolioAnio: folioAnio,
            DepartamentoId: Guid.CreateVersion7(),
            Clasificacion: Clasificacion.MateriaPrima,
            Prioridad: Prioridad.Normal,
            FechaSolicitud: DateTimeOffset.UtcNow,
            FechaEntregaDeseada: null,
            ProveedorSugeridoId: null,
            Descripcion: descripcion,
            RequisitanteId: requisitanteId);

    [Fact]
    public void Should_PassValidation_When_CommandIsValid()
    {
        var result = _validator.Validate(Valid());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_FailValidation_When_SucursalIdIsEmpty()
    {
        var result = _validator.Validate(Valid(sucursalId: Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName == nameof(CrearRequisicionCommand.SucursalId)
            && e.ErrorCode == "SUCURSAL_REQUERIDA");
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
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CrearRequisicionCommand.SucursalCodigo));
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

    [Fact]
    public void Should_FailValidation_When_DescripcionExceeds500Chars()
    {
        var result = _validator.Validate(Valid(descripcion: new string('x', 501)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "DESCRIPCION_DEMASIADO_LARGA");
    }

    [Fact]
    public void Should_PassValidation_When_DescripcionAtBoundary()
    {
        var result = _validator.Validate(Valid(descripcion: new string('x', 500)));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_PassValidation_When_DescripcionIsNull()
    {
        var result = _validator.Validate(Valid(descripcion: null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_PassValidation_When_RequisitanteIdIsNull()
    {
        var result = _validator.Validate(Valid(requisitanteId: null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Should_FailValidation_When_RequisitanteIdIsEmpty()
    {
        var result = _validator.Validate(Valid(requisitanteId: Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "REQUISITANTE_INVALIDO");
    }
}
