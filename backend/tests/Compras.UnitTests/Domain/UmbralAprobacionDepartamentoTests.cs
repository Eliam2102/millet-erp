using Millet.Compras.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Domain;

public class UmbralAprobacionDepartamentoTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 15);

    private static UmbralAprobacionDepartamento Nuevo(decimal? n1, decimal n2) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Hoy, n2, "MXN", null, n1);

    [Fact]
    public void Constructor_Sin_N1_Es_Valido()
    {
        var u = Nuevo(null, 10_000m);

        Assert.Null(u.UmbralMenorCuantia);
        Assert.Equal(10_000m, u.UmbralMonto);
    }

    [Fact]
    public void Constructor_Con_N1_Menor_Que_N2_Es_Valido()
    {
        var u = Nuevo(1_000m, 10_000m);

        Assert.Equal(1_000m, u.UmbralMenorCuantia);
    }

    [Theory]
    [InlineData(10_000, 10_000)]
    [InlineData(10_001, 10_000)]
    public void Constructor_Con_N1_Mayor_O_Igual_Que_N2_Lanza(decimal n1, decimal n2)
    {
        var ex = Assert.Throws<ValidationException>(() => Nuevo(n1, n2));

        Assert.Equal("UMBRAL_MENOR_CUANTIA_NO_MENOR", ex.Errors.Single().Code);
    }

    [Fact]
    public void Constructor_Con_Monto_Negativo_Lanza()
    {
        Assert.Throws<ValidationException>(() => Nuevo(null, -1m));
        Assert.Throws<ValidationException>(() => Nuevo(-1m, 10_000m));
    }

    [Fact]
    public void ActualizarMontos_Valida_La_Misma_Regla()
    {
        var u = Nuevo(1_000m, 10_000m);

        Assert.Throws<ValidationException>(() => u.ActualizarMontos(5_000m, 4_000m, "MXN"));

        u.ActualizarMontos(2_000m, 20_000m, "MXN");
        Assert.Equal(2_000m, u.UmbralMenorCuantia);
        Assert.Equal(20_000m, u.UmbralMonto);
    }

    [Fact]
    public void CerrarVigencia_Antes_De_VigenteDesde_Lanza()
    {
        var u = Nuevo(null, 10_000m);

        Assert.Throws<BusinessRuleException>(() => u.CerrarVigencia(Hoy.AddDays(-1)));
    }

    [Fact]
    public void CerrarVigencia_Asigna_VigenteHasta()
    {
        var u = new UmbralAprobacionDepartamento(Guid.CreateVersion7(), Guid.CreateVersion7(), Hoy.AddDays(-5), 10_000m);

        u.CerrarVigencia(Hoy.AddDays(-1));

        Assert.Equal(Hoy.AddDays(-1), u.VigenteHasta);
    }

    [Fact]
    public void Constructor_Sin_Modo_Deja_Ambos_Y_Sin_Minimo()
    {
        var u = Nuevo(null, 10_000m);

        Assert.Equal(ModoNivel2.Ambos, u.ModoNivel2);
        Assert.Null(u.MontoMinimoDisponibilidad);
    }

    [Fact]
    public void Constructor_Con_Minimo_Negativo_Lanza()
    {
        var ex = Assert.Throws<ValidationException>(() =>
            new UmbralAprobacionDepartamento(
                Guid.CreateVersion7(), Guid.CreateVersion7(), Hoy, 10_000m,
                montoMinimoDisponibilidad: -1m));

        Assert.Equal("UMBRAL_MONTO_MINIMO_NEGATIVO", ex.Errors.Single().Code);
    }

    [Fact]
    public void ActualizarMontos_Con_Modo_Invalido_Lanza()
    {
        var u = Nuevo(null, 10_000m);

        var ex = Assert.Throws<ValidationException>(() =>
            u.ActualizarMontos(null, 10_000m, "MXN", (ModoNivel2)99, null));

        Assert.Equal("UMBRAL_MODO_NIVEL2_INVALIDO", ex.Errors.Single().Code);
    }
}
