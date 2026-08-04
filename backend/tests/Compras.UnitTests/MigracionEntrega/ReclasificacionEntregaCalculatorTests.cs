using Millet.Compras.Application.MigracionEntrega;
using Millet.Compras.Domain.Ports.Almacen;

namespace Millet.Compras.UnitTests.MigracionEntrega;

/// <summary>
/// Tests del clasificador puro de reclasificación de históricas (ADR-0043
/// PR #4). Foco: separación EXACTO/AMBIGUO (riesgo del punto 2) y el CAP al
/// techo (que evita pendiente negativo en la fórmula de #401).
/// </summary>
public class ReclasificacionEntregaCalculatorTests
{
    private static Guid L(int n) => Guid.Parse($"00000000-0000-0000-0000-0000000000{n:d2}");
    private static Guid A(int n) => Guid.Parse($"00000000-0000-0000-0000-0000000001{n:d2}");

    private static EntregaReclasificacion Entrega(
        (Guid lineaId, decimal cant)[]? porLinea = null,
        (Guid articuloId, decimal cant)[]? porArticuloSinLinea = null)
        => new(
            (porLinea ?? []).ToDictionary(x => x.lineaId, x => x.cant),
            (porArticuloSinLinea ?? []).ToDictionary(x => x.articuloId, x => x.cant));

    // ─── Atribución exacta por LineaRqId ────────────────────────────────────

    [Fact]
    public void ArticuloUnico_EntregaParcial_PorLinea_Exacto_NoTotalmente()
    {
        var lineas = new[] { new LineaParaReclasificar(L(1), A(1), 10m) };
        var r = ReclasificacionEntregaCalculator.Clasificar(
            lineas, Entrega(porLinea: [(L(1), 4m)]));

        Assert.False(r.EsAmbigua);
        Assert.Equal(4m, r.EntregadoPorLinea[L(1)]);
        Assert.False(r.TotalmenteEntregada);
    }

    [Fact]
    public void ArticuloUnico_EntregaCompleta_Totalmente()
    {
        var lineas = new[] { new LineaParaReclasificar(L(1), A(1), 10m) };
        var r = ReclasificacionEntregaCalculator.Clasificar(
            lineas, Entrega(porLinea: [(L(1), 10m)]));

        Assert.False(r.EsAmbigua);
        Assert.Equal(10m, r.EntregadoPorLinea[L(1)]);
        Assert.True(r.TotalmenteEntregada);
    }

    [Fact]
    public void SobreEntrega_PorLinea_CapaAlTecho_PendienteCero()
    {
        // Línea sobre-entregada (12 > 10): cant_entregada = Cantidad (10),
        // no el crudo. Pendiente (almacén+recibida−entregada) no negativo.
        var lineas = new[] { new LineaParaReclasificar(L(1), A(1), 10m) };
        var r = ReclasificacionEntregaCalculator.Clasificar(
            lineas, Entrega(porLinea: [(L(1), 12m)]));

        Assert.False(r.EsAmbigua);
        Assert.Equal(10m, r.EntregadoPorLinea[L(1)]); // capado, no 12
        Assert.True(r.TotalmenteEntregada);
    }

    // ─── Atribución por artículo sin LineaRqId (salidas pre-#399) ───────────

    [Fact]
    public void ArticuloUnico_SalidaSinLineaRqId_AtribuyeExacto()
    {
        // Salida pre-#399 (sin LineaRqId). El artículo está en UNA línea →
        // todo lo del artículo es de esa línea, exacto sin ambigüedad.
        var lineas = new[] { new LineaParaReclasificar(L(1), A(1), 10m) };
        var r = ReclasificacionEntregaCalculator.Clasificar(
            lineas, Entrega(porArticuloSinLinea: [(A(1), 6m)]));

        Assert.False(r.EsAmbigua);
        Assert.Equal(6m, r.EntregadoPorLinea[L(1)]);
        Assert.False(r.TotalmenteEntregada);
    }

    [Fact]
    public void ArticuloUnico_SinLinea_MasPorLinea_Suma_YCapa()
    {
        // Mezcla: parte pre-#399 (sin línea) + parte post-#399 (con línea),
        // mismo artículo en una sola línea. Suma 7+5=12, capa a 10.
        var lineas = new[] { new LineaParaReclasificar(L(1), A(1), 10m) };
        var r = ReclasificacionEntregaCalculator.Clasificar(
            lineas, Entrega(porLinea: [(L(1), 7m)], porArticuloSinLinea: [(A(1), 5m)]));

        Assert.False(r.EsAmbigua);
        Assert.Equal(10m, r.EntregadoPorLinea[L(1)]); // 12 capado a 10
        Assert.True(r.TotalmenteEntregada);
    }

    // ─── Artículo repetido ──────────────────────────────────────────────────

    [Fact]
    public void ArticuloRepetido_TodoConLineaRqId_Exacto()
    {
        // Mismo artículo en 2 líneas, pero TODAS las salidas traen LineaRqId
        // (post-#399) → se reparte por línea, exacto, sin ambigüedad.
        var lineas = new[]
        {
            new LineaParaReclasificar(L(1), A(1), 10m),
            new LineaParaReclasificar(L(2), A(1), 5m),
        };
        var r = ReclasificacionEntregaCalculator.Clasificar(
            lineas, Entrega(porLinea: [(L(1), 10m), (L(2), 2m)]));

        Assert.False(r.EsAmbigua);
        Assert.Equal(10m, r.EntregadoPorLinea[L(1)]);
        Assert.Equal(2m, r.EntregadoPorLinea[L(2)]);
        Assert.False(r.TotalmenteEntregada); // L2: 2 < 5
    }

    [Fact]
    public void ArticuloRepetido_ConSalidaSinLineaRqId_EsAmbigua()
    {
        // Mismo artículo en 2 líneas Y hay entrega sin LineaRqId → reparto
        // indecidible → AMBIGUA, no se toca.
        var lineas = new[]
        {
            new LineaParaReclasificar(L(1), A(1), 10m),
            new LineaParaReclasificar(L(2), A(1), 5m),
        };
        var r = ReclasificacionEntregaCalculator.Clasificar(
            lineas, Entrega(porLinea: [(L(1), 3m)], porArticuloSinLinea: [(A(1), 4m)]));

        Assert.True(r.EsAmbigua);
        Assert.Empty(r.EntregadoPorLinea);
        Assert.False(r.TotalmenteEntregada);
    }

    [Fact]
    public void MixArticuloAmbiguoYExacto_RqEnteraAmbigua()
    {
        // A(1) repetido con salida sin línea (ambiguo); A(2) único (exacto).
        // La RQ entera se marca ambigua (no se migra parcial).
        var lineas = new[]
        {
            new LineaParaReclasificar(L(1), A(1), 10m),
            new LineaParaReclasificar(L(2), A(1), 5m),
            new LineaParaReclasificar(L(3), A(2), 8m),
        };
        var r = ReclasificacionEntregaCalculator.Clasificar(
            lineas, Entrega(
                porLinea: [(L(3), 8m)],
                porArticuloSinLinea: [(A(1), 4m)]));

        Assert.True(r.EsAmbigua);
    }

    // ─── Casos de cubo (mover / quedar) ─────────────────────────────────────

    [Fact]
    public void NadaEntregado_NoTotalmente_TodasCero()
    {
        var lineas = new[]
        {
            new LineaParaReclasificar(L(1), A(1), 10m),
            new LineaParaReclasificar(L(2), A(2), 5m),
        };
        var r = ReclasificacionEntregaCalculator.Clasificar(lineas, Entrega());

        Assert.False(r.EsAmbigua);
        Assert.Equal(0m, r.EntregadoPorLinea[L(1)]);
        Assert.Equal(0m, r.EntregadoPorLinea[L(2)]);
        Assert.False(r.TotalmenteEntregada); // → mover a EnSurtido
    }

    [Fact]
    public void MultiArticuloDistinto_TodasCompletas_Totalmente()
    {
        // Caso MID2026-000012: 3 artículos distintos, todas entregadas → queda.
        var lineas = new[]
        {
            new LineaParaReclasificar(L(1), A(1), 10m),
            new LineaParaReclasificar(L(2), A(2), 5m),
            new LineaParaReclasificar(L(3), A(3), 8m),
        };
        var r = ReclasificacionEntregaCalculator.Clasificar(
            lineas, Entrega(porLinea: [(L(1), 10m), (L(2), 5m), (L(3), 8m)]));

        Assert.False(r.EsAmbigua);
        Assert.True(r.TotalmenteEntregada);
    }

    [Fact]
    public void MultiArticuloDistinto_UnaParcial_NoTotalmente_CasoMID()
    {
        // Caso MID2026-000012 real: L1 entregada de stock, L2/L3 recibidas
        // por OC pero NO entregadas → no totalmente → mueve a EnSurtido.
        // 3 artículos distintos ⇒ exacto aunque las salidas sean pre-#399.
        var lineas = new[]
        {
            new LineaParaReclasificar(L(1), A(1), 10m),
            new LineaParaReclasificar(L(2), A(2), 5m),
            new LineaParaReclasificar(L(3), A(3), 8m),
        };
        var r = ReclasificacionEntregaCalculator.Clasificar(
            lineas, Entrega(porArticuloSinLinea: [(A(1), 10m)]));

        Assert.False(r.EsAmbigua);
        Assert.Equal(10m, r.EntregadoPorLinea[L(1)]); // L1 completa
        Assert.Equal(0m, r.EntregadoPorLinea[L(2)]);  // L2 sin entregar
        Assert.Equal(0m, r.EntregadoPorLinea[L(3)]);  // L3 sin entregar
        Assert.False(r.TotalmenteEntregada);          // → mover a EnSurtido
    }
}
