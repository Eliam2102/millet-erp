using FluentAssertions;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Ports;

namespace Millet.Almacen.UnitTests.Salidas;

/// <summary>
/// Fase E PR5 — Camino 1: el CC-Máquina de la salida-con-RQ es AUTORITATIVO del
/// backend. <see cref="RegistrarSalidaConRequisicionHandler.ResolverCentroCostoHeredado"/>
/// lee el CC de la línea de RQ (por LineaRqId, fallback por artículo) e IGNORA
/// lo que mande el caller — el método no recibe input.CentroCostoId, así que la
/// autoridad es por construcción.
/// </summary>
public class RegistrarSalidaConRequisicionCcTests
{
    private static readonly Guid CcA = Guid.CreateVersion7();
    private static readonly Guid CcB = Guid.CreateVersion7();
    private static readonly Guid Art = Guid.CreateVersion7();

    private static RequisicionLectura Rq(params RequisicionLineaLectura[] lineas) =>
        new(
            Id: Guid.CreateVersion7(),
            Folio: "MID2026-000001",
            EmpresaId: Guid.CreateVersion7(),
            DepartamentoId: Guid.CreateVersion7(),
            AlmacenDestinoId: null,
            PersonaDestinatariaId: null,
            Estado: "EnSurtido",
            Lineas: lineas);

    private static RequisicionLineaLectura Linea(Guid lineaId, Guid articuloId, Guid? cc) =>
        new(lineaId, articuloId, "PZA", 10m, 0m, cc, ProyectoId: null);

    [Fact]
    public void Por_LineaRqId_hereda_el_CC_de_esa_linea_exacta()
    {
        // Dos líneas con el MISMO artículo pero CC distintos: LineaRqId
        // desambigua — hereda el CC de la línea señalada, no el de la otra.
        var l1 = Guid.CreateVersion7();
        var l2 = Guid.CreateVersion7();
        var rq = Rq(Linea(l1, Art, CcA), Linea(l2, Art, CcB));

        RegistrarSalidaConRequisicionHandler
            .ResolverCentroCostoHeredado(rq, lineaRqId: l2, articuloId: Art)
            .Should().Be(CcB);
    }

    [Fact]
    public void Sin_LineaRqId_cae_a_match_por_articulo()
    {
        var rq = Rq(Linea(Guid.CreateVersion7(), Art, CcA));

        RegistrarSalidaConRequisicionHandler
            .ResolverCentroCostoHeredado(rq, lineaRqId: null, articuloId: Art)
            .Should().Be(CcA);
    }

    [Fact]
    public void Rq_null_devuelve_null()
    {
        RegistrarSalidaConRequisicionHandler
            .ResolverCentroCostoHeredado(rq: null, lineaRqId: Guid.NewGuid(), articuloId: Art)
            .Should().BeNull();
    }

    [Fact]
    public void LineaRqId_sin_match_devuelve_null()
    {
        var rq = Rq(Linea(Guid.CreateVersion7(), Art, CcA));

        RegistrarSalidaConRequisicionHandler
            .ResolverCentroCostoHeredado(rq, lineaRqId: Guid.NewGuid(), articuloId: Art)
            .Should().BeNull();
    }

    [Fact]
    public void Linea_de_RQ_sin_CC_devuelve_null()
    {
        var l1 = Guid.CreateVersion7();
        var rq = Rq(Linea(l1, Art, cc: null));

        RegistrarSalidaConRequisicionHandler
            .ResolverCentroCostoHeredado(rq, lineaRqId: l1, articuloId: Art)
            .Should().BeNull();
    }
}
