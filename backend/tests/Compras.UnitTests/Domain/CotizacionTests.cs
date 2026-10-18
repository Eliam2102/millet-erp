using Millet.Compras.Domain.Cotizaciones;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Domain;

public class CotizacionTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 17);
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static LineaCotizacionDatos Linea(Guid? id = null, string unidadRq = "PZA", string unidadCot = "CAJA", decimal factor = 12m)
        => new(id ?? Guid.CreateVersion7(), unidadRq, unidadCot, factor, 4980m, 5);

    private static Cotizacion Registrar(DateOnly? vigente = null, params LineaCotizacionDatos[] lineas)
        => Cotizacion.Registrar(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), "F-1", "mxn", Hoy.AddDays(-5), vigente ?? Hoy.AddDays(10), null,
            lineas.Length == 0 ? [Linea()] : lineas, Guid.CreateVersion7(), Ahora, Hoy);

    private static void AssertCodigo(string codigo, Action accion)
        => Assert.Equal(codigo, Assert.Throws<BusinessRuleException>(accion).Code);

    [Fact]
    public void Should_Normalize_Price_To_RequisitionUnit_And_Mxn()
    {
        Assert.Equal(415.0000m, NormalizadorCotizacion.PrecioPorUnidadRequisicion(4980m, 12m));
        Assert.Equal(25.0000m, NormalizadorCotizacion.PrecioPorUnidadRequisicion(10m, 0.4m));
        Assert.Equal(446.25m, NormalizadorCotizacion.PrecioMxn(25m, "USD", 17.85m));
        Assert.Null(NormalizadorCotizacion.PrecioMxn(25m, "USD", null));
        Assert.Equal(415.00m, NormalizadorCotizacion.PrecioMxn(415m, "MXN", null));
    }

    [Fact]
    public void Should_Throw_When_SameUnit_And_FactorNotOne()
        => AssertCodigo("COT_FACTOR_MISMA_UNIDAD", () => Registrar(null, Linea(unidadRq: "PZA", unidadCot: "pza", factor: 12m)));

    [Fact]
    public void Should_Throw_When_LineaDuplicada()
    {
        var id = Guid.CreateVersion7();
        AssertCodigo("COT_PARTIDA_DUPLICADA", () => Registrar(null, Linea(id), Linea(id)));
    }

    [Fact]
    public void Should_RegisterExpired_But_NotElegir()
    {
        var c = Registrar(Hoy.AddDays(-1));
        AssertCodigo("COT_VENCIDA", () => c.Elegir(Guid.CreateVersion7(), "mejor precio", Guid.CreateVersion7(), Ahora, Hoy));
    }

    [Fact]
    public void Should_Throw_When_MotivoCorto()
    {
        var c = Registrar();
        AssertCodigo("COT_MOTIVO_REQUERIDO", () => c.Elegir(Guid.CreateVersion7(), "ok", Guid.CreateVersion7(), Ahora, Hoy));
    }

    [Fact]
    public void Should_Throw_When_ElegirDosVeces_Or_EditarElegida()
    {
        var c = Registrar();
        c.Elegir(Guid.CreateVersion7(), "mejor precio", Guid.CreateVersion7(), Ahora, Hoy);
        Assert.True(c.Elegida);
        AssertCodigo("COT_YA_ELEGIDA", () => c.Elegir(Guid.CreateVersion7(), "mejor precio", Guid.CreateVersion7(), Ahora, Hoy));
        AssertCodigo("COT_COMPARATIVO_CERRADO", () => c.Editar(Guid.CreateVersion7(), null, "MXN", Hoy, Hoy, null, [Linea()], Hoy));
    }

    [Fact]
    public void Should_Throw_When_PartidaNoCotizada()
        => AssertCodigo("COT_PARTIDAS_INCOMPLETAS", () => Registrar().PrecioPorUnidadRequisicion(Guid.CreateVersion7()));

    [Fact]
    public void Should_ReleaseElection_KeepHistory_And_AllowReelection()
    {
        var c = Registrar();
        var oc = Guid.CreateVersion7();
        var usuario = Guid.CreateVersion7();
        c.Elegir(oc, "mejor precio", usuario, Ahora, Hoy);

        c.LiberarEleccion(usuario, Ahora.AddHours(1), "OC cancelada: error de captura");

        Assert.False(c.Elegida);
        Assert.Null(c.MotivoEleccion);
        var previa = Assert.Single(c.EleccionesAnteriores);
        Assert.Equal(oc, previa.OrdenCompraId);
        Assert.Equal("mejor precio", previa.Motivo);
        Assert.Equal("OC cancelada: error de captura", previa.MotivoLiberacion);

        c.Elegir(Guid.CreateVersion7(), "segunda elección", usuario, Ahora, Hoy);
        Assert.True(c.Elegida);
        c.LiberarEleccion(usuario, Ahora, "otra vez");
        Assert.Equal(2, c.EleccionesAnteriores.Count);
    }

    [Fact]
    public void Should_Throw_When_OtraElegidaVigente()
        => AssertCodigo("COT_YA_HAY_ELEGIDA", () => Registrar().Elegir(
            Guid.CreateVersion7(), "mejor precio", Guid.CreateVersion7(), Ahora, Hoy, hayOtraElegidaVigente: true));
}
