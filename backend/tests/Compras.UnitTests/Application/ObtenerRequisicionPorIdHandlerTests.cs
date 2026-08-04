using Millet.CentrosCosto.Application.PublicPorts;
using Millet.Compras.Application.ObtenerRequisicionPorId;
using Millet.Compras.Domain.Ports.DatosMaestros;

namespace Millet.Compras.UnitTests.Application;

/// <summary>
/// Tests de la lógica pura de enriquecimiento de etiqueta de artículo en el
/// detalle de requisición (ADR-0042 addendum). Sin DB:
/// <list type="bullet">
///   <item><see cref="ObtenerRequisicionPorIdHandler.ExtraerArticuloIdsDistintos"/>:
///   colecta los ArticuloId distintos (varias líneas del mismo artículo → un
///   solo id ⇒ query batch, no N+1).</item>
///   <item><see cref="ObtenerRequisicionPorIdHandler.AplicarArticulos"/>: puebla
///   clave/nombre por línea; cae a null cuando el artículo no resuelve (el
///   front cae al id).</item>
/// </list>
/// El wiring end-to-end (incl. proveedor de cabecera) vive en el test de
/// integración del detalle de requisición.
/// </summary>
public class ObtenerRequisicionPorIdHandlerTests
{
    [Fact]
    public void ExtraerArticuloIdsDistintos_dedupe()
    {
        var a1 = Guid.CreateVersion7();
        var a2 = Guid.CreateVersion7();
        var lineas = new[] { Linea(a1), Linea(a1), Linea(a2) };

        ObtenerRequisicionPorIdHandler.ExtraerArticuloIdsDistintos(lineas)
            .Should().BeEquivalentTo(new[] { a1, a2 });
    }

    [Fact]
    public void AplicarArticulos_resuelve_por_linea_y_cae_a_null_si_no_resuelve()
    {
        // REGRESIÓN PRE-EXISTENTE: a2 simula un artículo fuera del tope de la
        // lista capada; con el read-port batch resuelve igual. El no resuelto
        // conserva null (el front cae al id), nunca crashea.
        var a1 = Guid.CreateVersion7();
        var a2 = Guid.CreateVersion7();
        var lineas = new[] { Linea(a1), Linea(a2) };
        var dict = new Dictionary<Guid, ArticuloLectura>
        {
            [a1] = new(a1, "PAP-OF-001", "Papel bond carta"),
        };

        var resultado = ObtenerRequisicionPorIdHandler.AplicarArticulos(lineas, dict);

        resultado[0].ArticuloClave.Should().Be("PAP-OF-001");
        resultado[0].ArticuloNombre.Should().Be("Papel bond carta");
        resultado[1].ArticuloClave.Should().BeNull();
        resultado[1].ArticuloNombre.Should().BeNull();
        resultado[1].ArticuloId.Should().Be(a2);
    }

    [Fact]
    public void ExtraerCentroCostoIdsDistintos_dedupe_y_omite_null()
    {
        var m1 = Guid.CreateVersion7();
        var m2 = Guid.CreateVersion7();
        var lineas = new[]
        {
            Linea(Guid.CreateVersion7(), m1),
            Linea(Guid.CreateVersion7(), m1), // dup
            Linea(Guid.CreateVersion7(), m2),
            Linea(Guid.CreateVersion7()),     // centroCostoId null → se omite
        };

        ObtenerRequisicionPorIdHandler.ExtraerCentroCostoIdsDistintos(lineas)
            .Should().BeEquivalentTo(new[] { m1, m2 });
    }

    [Fact]
    public void AplicarCentrosCosto_resuelve_incluye_inactivas_y_cae_a_null()
    {
        // Los 4 casos del plan en una sola prueba: resoluble activa, inactiva
        // (resuelve nombre igual, ADR-0049), irresoluble (null → "No catalogado"),
        // y línea sin CC (null).
        var activa = Guid.CreateVersion7();
        var inactiva = Guid.CreateVersion7();
        var irresoluble = Guid.CreateVersion7();
        var lineas = new[]
        {
            Linea(Guid.CreateVersion7(), activa),
            Linea(Guid.CreateVersion7(), inactiva),
            Linea(Guid.CreateVersion7(), irresoluble),
            Linea(Guid.CreateVersion7()), // sin CC
        };
        var dict = new Dictionary<Guid, Dim3Lectura>
        {
            [activa] = new(activa, "MCLC101", "Gantry", Activa: true),
            [inactiva] = new(inactiva, "MCLC900", "Prensa vieja", Activa: false),
            // irresoluble ausente del diccionario a propósito
        };

        var r = ObtenerRequisicionPorIdHandler.AplicarCentrosCosto(lineas, dict);

        // (1) resoluble activa → clave/nombre
        r[0].CentroCostoClave.Should().Be("MCLC101");
        r[0].CentroCostoNombre.Should().Be("Gantry");
        // (3) inactiva → resuelve nombre igual (el read-port incluye inactivas)
        r[1].CentroCostoClave.Should().Be("MCLC900");
        r[1].CentroCostoNombre.Should().Be("Prensa vieja");
        // (2) id irresoluble → ambos null (el FE cae a "No catalogado")
        r[2].CentroCostoClave.Should().BeNull();
        r[2].CentroCostoNombre.Should().BeNull();
        // (4) centroCostoId null → null/null
        r[3].CentroCostoClave.Should().BeNull();
        r[3].CentroCostoNombre.Should().BeNull();
    }

    private static LineaResponse Linea(Guid articuloId, Guid? centroCostoId = null) => new(
        Id: Guid.CreateVersion7(),
        Posicion: 1,
        ArticuloId: articuloId,
        ArticuloClave: null,
        ArticuloNombre: null,
        Cantidad: 5m,
        UnidadMedida: "PZA",
        PrecioEstimadoMonto: 10m,
        PrecioEstimadoMoneda: "MXN",
        CuentaContableId: null,
        CentroCostoId: centroCostoId,
        CentroCostoClave: null,
        CentroCostoNombre: null,
        Proyecto: null,
        FechaRequerida: null,
        Notas: null,
        CantDeAlmacen: 0m,
        CantDeCompra: 0m,
        CantRecibida: 0m,
        CantPendiente: 0m,
        CantEntregadoDeAlmacen: 0m,
        CantPendienteEntregar: 0m);
}
