using Millet.Compras.Application.PreviewCubrimiento;
using Millet.Compras.Domain.Ports.DatosMaestros;

namespace Millet.Compras.UnitTests.Application;

/// <summary>
/// Tests de la lógica pura de enriquecimiento de etiqueta de artículo en el
/// preview de cubrimiento ("Disponibilidad estimada"), ADR-0042 addendum. Sin
/// DB:
/// <list type="bullet">
///   <item><see cref="PreviewCubrimientoHandler.ExtraerArticuloIdsDistintos"/>:
///   colecta los ArticuloId distintos (varias líneas del mismo artículo → un
///   solo id ⇒ query batch, no N+1).</item>
///   <item><see cref="PreviewCubrimientoHandler.AplicarArticulos"/>: puebla
///   clave/nombre por línea; cae a null cuando el artículo no resuelve (el
///   front cae al id).</item>
/// </list>
/// El bug: el panel resolvía la etiqueta vía el catálogo capado a
/// <c>LimitMax=200</c>, así que artículos con clave fuera del top-200 caían al
/// UUID. El enriquecimiento server-side en batch los resuelve igual.
/// </summary>
public class PreviewCubrimientoHandlerTests
{
    [Fact]
    public void ExtraerArticuloIdsDistintos_dedupe()
    {
        var a1 = Guid.CreateVersion7();
        var a2 = Guid.CreateVersion7();
        var lineas = new[] { Linea(a1), Linea(a1), Linea(a2) };

        PreviewCubrimientoHandler.ExtraerArticuloIdsDistintos(lineas)
            .Should().BeEquivalentTo(new[] { a1, a2 });
    }

    [Fact]
    public void AplicarArticulos_resuelve_por_linea_y_cae_a_null_si_no_resuelve()
    {
        // a2 simula un artículo con clave fuera del top-200 del catálogo capado
        // (el bug medido: MUO00003 rank ~1153). Con el read-port batch resuelve
        // igual. El no resuelto conserva null → el front cae al id, nunca crashea.
        var a1 = Guid.CreateVersion7();
        var a2 = Guid.CreateVersion7();
        var lineas = new[] { Linea(a1), Linea(a2) };
        var dict = new Dictionary<Guid, ArticuloLectura>
        {
            [a1] = new(a1, "MUO00003", "ALMOADILLA O COJIN PARA SELLO No.1"),
        };

        var resultado = PreviewCubrimientoHandler.AplicarArticulos(lineas, dict);

        resultado[0].ArticuloClave.Should().Be("MUO00003");
        resultado[0].ArticuloNombre.Should().Be("ALMOADILLA O COJIN PARA SELLO No.1");
        resultado[1].ArticuloClave.Should().BeNull();
        resultado[1].ArticuloNombre.Should().BeNull();
        resultado[1].ArticuloId.Should().Be(a2);
    }

    private static PreviewCubrimientoLinea Linea(Guid articuloId) => new(
        LineaId: Guid.CreateVersion7(),
        ArticuloId: articuloId,
        ArticuloClave: null,
        ArticuloNombre: null,
        Cantidad: 5m,
        EstimadoDeAlmacen: 0m,
        EstimadoDeCompra: 5m,
        Disponible: 0m);
}
