using Millet.Compras.Application.Lineas.AgregarLinea;

namespace Millet.Compras.UnitTests.Application.Lineas;

public class AgregarLineaValidatorTests
{
    private readonly AgregarLineaValidator _validator = new();

    // Fase E PR2.1: el CC-Máquina es obligatorio; el comando "válido" lo lleva.
    private static readonly Guid CcValido = Guid.Parse("0c000000-0000-0000-0000-000000000001");

    private static AgregarLineaCommand Valid(
        decimal cantidad = 10m,
        string unidadMedida = "PZA",
        decimal precio = 15.50m,
        string moneda = "MXN",
        string? proyecto = null,
        string? notas = null) =>
        new(
            RequisicionId: Guid.CreateVersion7(),
            ArticuloId: Guid.CreateVersion7(),
            Cantidad: cantidad,
            UnidadMedida: unidadMedida,
            PrecioEstimadoMonto: precio,
            PrecioEstimadoMoneda: moneda,
            CentroCostoId: CcValido,
            Proyecto: proyecto,
            Notas: notas);

    [Fact]
    public void Should_PassValidation_When_CommandIsValid()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void Should_FailValidation_When_CentroCostoIdIsNull()
    {
        // Fase E PR2.1: línea de RQ sin CC-Máquina → rechazo.
        var result = _validator.Validate(Valid() with { CentroCostoId = null });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "LINEA_RQ_CENTRO_COSTO_REQUERIDO");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Should_FailValidation_When_CantidadIsNotPositive(decimal cantidad)
    {
        var result = _validator.Validate(Valid(cantidad: cantidad));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "CANTIDAD_INVALIDA");
    }

    [Fact]
    public void Should_FailValidation_When_UnidadMedidaIsEmpty()
    {
        var result = _validator.Validate(Valid(unidadMedida: ""));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "UNIDAD_MEDIDA_REQUERIDA");
    }

    [Fact]
    public void Should_FailValidation_When_UnidadMedidaTooLong()
    {
        var result = _validator.Validate(Valid(unidadMedida: new string('x', 21)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "UNIDAD_MEDIDA_DEMASIADO_LARGA");
    }

    [Fact]
    public void Should_FailValidation_When_PrecioIsNegative()
    {
        var result = _validator.Validate(Valid(precio: -1m));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "PRECIO_NEGATIVO");
    }

    [Theory]
    [InlineData("mxn")]      // minúsculas
    [InlineData("MX")]       // 2 letras
    [InlineData("USDT")]     // 4 letras
    [InlineData("12X")]      // dígitos
    public void Should_FailValidation_When_MonedaFormatIsInvalid(string moneda)
    {
        var result = _validator.Validate(Valid(moneda: moneda));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "MONEDA_FORMATO_INVALIDO");
    }

    [Fact]
    public void Should_FailValidation_When_ProyectoTooLong()
    {
        var result = _validator.Validate(Valid(proyecto: new string('x', 201)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "PROYECTO_DEMASIADO_LARGO");
    }

    [Fact]
    public void Should_FailValidation_When_NotasTooLong()
    {
        var result = _validator.Validate(Valid(notas: new string('x', 501)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "NOTAS_DEMASIADO_LARGAS");
    }
}
