using Millet.CentrosCosto.Application.PublicPorts;
using Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;
using Millet.Compras.Domain.Ports.DatosMaestros;

namespace Millet.Compras.UnitTests.Oc.Application;

/// <summary>
/// Tests de la lógica de enriquecimiento de folio de RQ en el detalle de OC
/// (fix A, ADR-0042 matiz intra-módulo). Cubre las dos fases puras del
/// handler — sin DB:
///
/// <list type="bullet">
///   <item><see cref="ObtenerOrdenCompraPorIdHandler.ExtraerRequisicionIdsDistintos"/>:
///   colecta los RequisicionId distintos (varias líneas de la misma RQ →
///   un solo id ⇒ una sola query batch, no N+1).</item>
///   <item><see cref="ObtenerOrdenCompraPorIdHandler.AplicarFoliosRequisicion"/>:
///   puebla RequisicionFolio por línea, cae a null cuando la RQ no resuelve
///   o la línea es manual (el front cae al id en ese caso).</item>
/// </list>
///
/// La resolución real contra <c>Requisiciones</c> (query directa, state-agnostic)
/// y su wiring end-to-end viven en el test de integración
/// <c>OrdenesCompraEndpointsTests.Obtener_Detalle_Resuelve_Folio_De_RQ_*</c>.
/// </summary>
public class ObtenerOrdenCompraPorIdHandlerTests
{
    private static readonly Guid RqA = Guid.CreateVersion7();
    private static readonly Guid RqB = Guid.CreateVersion7();

    [Fact]
    public void ExtraerRequisicionIdsDistintos_dedupe_y_omite_manuales()
    {
        // 4 líneas: 2 de RqA (consolidación N:1), 1 de RqB, 1 manual (null).
        var lineas = new[]
        {
            Linea(rqId: RqA),
            Linea(rqId: RqA),
            Linea(rqId: RqB),
            Linea(rqId: null),
        };

        var ids = ObtenerOrdenCompraPorIdHandler.ExtraerRequisicionIdsDistintos(lineas);

        // Una sola entrada por RQ distinta → la query de folios es batch, no por línea.
        ids.Should().BeEquivalentTo(new[] { RqA, RqB });
    }

    [Fact]
    public void ExtraerRequisicionIdsDistintos_sin_lineas_de_rq_devuelve_vacio()
    {
        var lineas = new[] { Linea(rqId: null), Linea(rqId: null) };

        ObtenerOrdenCompraPorIdHandler.ExtraerRequisicionIdsDistintos(lineas)
            .Should().BeEmpty();
    }

    [Fact]
    public void AplicarFoliosRequisicion_resuelve_folio_por_linea()
    {
        var lineas = new[] { Linea(rqId: RqA), Linea(rqId: RqB) };
        var folios = new Dictionary<Guid, string>
        {
            [RqA] = "MID2026-000111",
            [RqB] = "MID2026-000222",
        };

        var resultado = ObtenerOrdenCompraPorIdHandler.AplicarFoliosRequisicion(lineas, folios);

        resultado[0].RequisicionFolio.Should().Be("MID2026-000111");
        resultado[1].RequisicionFolio.Should().Be("MID2026-000222");
    }

    [Fact]
    public void AplicarFoliosRequisicion_cae_a_null_cuando_la_RQ_no_resuelve()
    {
        // La RQ existe en la línea pero no en el diccionario (borrada / no
        // resuelta): folio null pero el id se conserva para que el front
        // caiga a él.
        var lineas = new[] { Linea(rqId: RqA) };

        var resultado = ObtenerOrdenCompraPorIdHandler.AplicarFoliosRequisicion(
            lineas, new Dictionary<Guid, string>());

        resultado[0].RequisicionFolio.Should().BeNull();
        resultado[0].RequisicionId.Should().Be(RqA);
    }

    [Fact]
    public void AplicarFoliosRequisicion_linea_manual_queda_con_folio_null()
    {
        var lineas = new[] { Linea(rqId: null) };
        var folios = new Dictionary<Guid, string> { [RqA] = "MID2026-000111" };

        var resultado = ObtenerOrdenCompraPorIdHandler.AplicarFoliosRequisicion(lineas, folios);

        resultado[0].RequisicionId.Should().BeNull();
        resultado[0].RequisicionFolio.Should().BeNull();
    }

    // ── ADR-0042 addendum: enriquecimiento de etiqueta de artículo ──────────

    [Fact]
    public void ExtraerArticuloIdsDistintos_dedupe()
    {
        var a1 = Guid.CreateVersion7();
        var a2 = Guid.CreateVersion7();
        // 2 líneas con el mismo artículo → un solo id ⇒ query batch, no N+1.
        var lineas = new[] { LineaArt(a1), LineaArt(a1), LineaArt(a2) };

        ObtenerOrdenCompraPorIdHandler.ExtraerArticuloIdsDistintos(lineas)
            .Should().BeEquivalentTo(new[] { a1, a2 });
    }

    [Fact]
    public void AplicarArticulos_resuelve_por_linea_y_cae_a_null_si_no_resuelve()
    {
        // REGRESIÓN PRE-EXISTENTE: el artículo a2 simula uno fuera del tope de
        // la lista capada; con el read-port batch resuelve igual. El que no
        // está en el diccionario conserva null (el front cae al id).
        var a1 = Guid.CreateVersion7();
        var a2 = Guid.CreateVersion7();
        var lineas = new[] { LineaArt(a1), LineaArt(a2) };
        var dict = new Dictionary<Guid, ArticuloLectura>
        {
            [a1] = new(a1, "FER-58295", "Backer rod 5/8"),
        };

        var resultado = ObtenerOrdenCompraPorIdHandler.AplicarArticulos(lineas, dict);

        resultado[0].ArticuloClave.Should().Be("FER-58295");
        resultado[0].ArticuloNombre.Should().Be("Backer rod 5/8");
        resultado[1].ArticuloClave.Should().BeNull();
        resultado[1].ArticuloNombre.Should().BeNull();
        resultado[1].ArticuloId.Should().Be(a2);
    }

    // ── Fase E PR3: enriquecimiento del CC-Máquina (molde de artículo) ──────

    [Fact]
    public void ExtraerCentroCostoIdsDistintos_dedupe_y_omite_null()
    {
        var m1 = Guid.CreateVersion7();
        var m2 = Guid.CreateVersion7();
        var lineas = new[]
        {
            Linea(rqId: null, centroCostoId: m1),
            Linea(rqId: null, centroCostoId: m1), // dup
            Linea(rqId: null, centroCostoId: m2),
            Linea(rqId: null), // sin CC → se omite
        };

        ObtenerOrdenCompraPorIdHandler.ExtraerCentroCostoIdsDistintos(lineas)
            .Should().BeEquivalentTo(new[] { m1, m2 });
    }

    [Fact]
    public void AplicarCentrosCosto_resuelve_incluye_inactivas_y_cae_a_null()
    {
        // Los 4 casos: resoluble activa, inactiva (resuelve nombre igual,
        // ADR-0049), irresoluble (null → "No catalogado"), y línea sin CC.
        var activa = Guid.CreateVersion7();
        var inactiva = Guid.CreateVersion7();
        var irresoluble = Guid.CreateVersion7();
        var lineas = new[]
        {
            Linea(rqId: null, centroCostoId: activa),
            Linea(rqId: null, centroCostoId: inactiva),
            Linea(rqId: null, centroCostoId: irresoluble),
            Linea(rqId: null), // sin CC
        };
        var dict = new Dictionary<Guid, Dim3Lectura>
        {
            [activa] = new(activa, "CNCBT01", "Canteadora", Activa: true),
            [inactiva] = new(inactiva, "MCLC900", "Prensa vieja", Activa: false),
        };

        var r = ObtenerOrdenCompraPorIdHandler.AplicarCentrosCosto(lineas, dict);

        r[0].CentroCostoClave.Should().Be("CNCBT01");
        r[0].CentroCostoNombre.Should().Be("Canteadora");
        r[1].CentroCostoClave.Should().Be("MCLC900"); // inactiva resuelve igual
        r[1].CentroCostoNombre.Should().Be("Prensa vieja");
        r[2].CentroCostoClave.Should().BeNull(); // irresoluble → "No catalogado"
        r[2].CentroCostoNombre.Should().BeNull();
        r[3].CentroCostoClave.Should().BeNull(); // sin CC
        r[3].CentroCostoNombre.Should().BeNull();
    }

    private static LineaOrdenCompraResponse LineaArt(Guid articuloId) =>
        Linea(rqId: null) with { ArticuloId = articuloId };

    private static LineaOrdenCompraResponse Linea(Guid? rqId, Guid? centroCostoId = null) => new(
        Id: Guid.CreateVersion7(),
        Posicion: 1,
        ArticuloId: Guid.CreateVersion7(),
        ArticuloClave: null,
        ArticuloNombre: null,
        DescripcionExtendida: null,
        Cantidad: 10m,
        UnidadMedida: "PZA",
        PrecioUnitario: 50m,
        IvaImporte: 80m,
        RetencionIsr: null,
        SubtotalLinea: 500m,
        DepartamentoSolicitanteId: Guid.CreateVersion7(),
        CentroCostoId: centroCostoId,
        CentroCostoClave: null,
        CentroCostoNombre: null,
        RequisicionId: rqId,
        LineaRequisicionId: rqId is null ? null : Guid.CreateVersion7(),
        RequisicionFolio: null,
        FechaEntregaLinea: null,
        CantidadRecibida: 0m,
        CantidadFacturada: 0m,
        TextoAdicional: null);
}
