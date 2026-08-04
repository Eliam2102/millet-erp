using FluentAssertions;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Ports;

namespace Millet.Almacen.UnitTests.Salidas;

/// <summary>
/// Fase E PR5 — display del CC-Máquina en el detalle de salida. Cubre las dos
/// fases puras de <see cref="ObtenerSalidaPorIdHandler"/> (sin DB): extraer los
/// ids distintos (batch, anti-N+1) y poblar clave/nombre por línea vía el
/// read-port (sin filtro, incluye inactivas — ADR-0049/0050). Molde PR3/PR4.
/// </summary>
public class ObtenerSalidaPorIdCcTests
{
    private static readonly Guid M1 = Guid.CreateVersion7();
    private static readonly Guid M2 = Guid.CreateVersion7();

    [Fact]
    public void ExtraerCentroCostoIdsDistintos_dedupe_y_omite_null()
    {
        var lineas = new[]
        {
            Linea(M1), Linea(M1), Linea(M2), Linea(centroCostoId: null),
        };

        ObtenerSalidaPorIdHandler.ExtraerCentroCostoIdsDistintos(lineas)
            .Should().BeEquivalentTo(new[] { M1, M2 });
    }

    [Fact]
    public void AplicarCentrosCosto_resuelve_incluye_inactivas_y_cae_a_null()
    {
        // 4 casos: resoluble activa, inactiva (resuelve igual, ADR-0049),
        // irresoluble (id sin fila → null → "No catalogado"), y línea sin CC.
        var activa = Guid.CreateVersion7();
        var inactiva = Guid.CreateVersion7();
        var irresoluble = Guid.CreateVersion7();
        var lineas = new[]
        {
            Linea(activa), Linea(inactiva), Linea(irresoluble), Linea(centroCostoId: null),
        };
        var dict = new Dictionary<Guid, Dim3Lectura>
        {
            [activa] = new(activa, "CNCBT01", "Canteadora", Activa: true),
            [inactiva] = new(inactiva, "MCLC900", "Prensa vieja", Activa: false),
        };

        var r = ObtenerSalidaPorIdHandler.AplicarCentrosCosto(lineas, dict);

        r[0].CentroCostoClave.Should().Be("CNCBT01");
        r[0].CentroCostoNombre.Should().Be("Canteadora");
        r[1].CentroCostoClave.Should().Be("MCLC900"); // inactiva resuelve igual
        r[1].CentroCostoNombre.Should().Be("Prensa vieja");
        r[2].CentroCostoClave.Should().BeNull(); // irresoluble
        r[2].CentroCostoNombre.Should().BeNull();
        r[3].CentroCostoClave.Should().BeNull(); // sin CC
        r[3].CentroCostoNombre.Should().BeNull();
    }

    private static SalidaLineaItem Linea(Guid? centroCostoId) => new(
        Id: Guid.CreateVersion7(),
        Posicion: 1,
        ArticuloId: Guid.CreateVersion7(),
        ArticuloClave: null,
        ArticuloDescripcion: null,
        Cantidad: 5m,
        UnidadMedida: "PZA",
        CostoUnitarioMxn: 10m,
        MontoTotalMxn: 50m,
        CentroCostoId: centroCostoId,
        CentroCostoClave: null,
        CentroCostoNombre: null,
        ProyectoId: null);
}
