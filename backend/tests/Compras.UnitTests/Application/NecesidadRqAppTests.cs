using Millet.Compras.Application.NecesidadRqApp;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.UnitTests.Application;

/// <summary>M-332: necesidad / disponible / convertido / pendiente de una línea de RQ de la app de Almacenes.</summary>
public class NecesidadRqAppTests
{
    private static readonly Guid Art = Guid.CreateVersion7();
    private static readonly ArticuloLectura Lectura = new(Art, "VID-6", "Vidrio flotado 6 mm");

    private static LineaRequisicion Linea(string folioExterno = "APP-1")
    {
        var rq = Requisicion.CrearDesdeAlmacenesApp(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Folio.Parse("MID2026-000001"), 2026,
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, Guid.CreateVersion7(), Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, folioExterno, Guid.CreateVersion7(),
            [new LineaAppRequisicion(Art, 10m, "PZA", Money.Of(1m, "MXN"), 10, null, null, Posicion: 1)]);
        return rq.Lineas.Single();
    }

    [Fact]
    public void Necesidad_10_disponible_4_OC_10_deja_pendiente_0_y_no_avisa_existencia()
    {
        var f = NecesidadRqAppHandler.Construir(Linea(), Lectura, disponible: 4m, convertido: 10m);

        (f.Necesidad, f.Disponible, f.ConvertidoACompra, f.Pendiente, f.DisponibleCubre).Should().Be((10m, 4m, 10m, 0m, false));
        f.ArticuloClave.Should().Be("VID-6");
    }

    [Fact]
    public void OC_por_6_deja_pendiente_4()
    {
        var f = NecesidadRqAppHandler.Construir(Linea(), Lectura, disponible: 4m, convertido: 6m);

        f.Pendiente.Should().Be(4m);
    }

    [Fact]
    public void Disponible_igual_a_la_necesidad_avisa_que_cubre()
    {
        NecesidadRqAppHandler.Construir(Linea(), Lectura, disponible: 10m, convertido: 0m).DisponibleCubre.Should().BeTrue();
    }

    [Fact]
    public void OC_mayor_a_la_necesidad_no_deja_pendiente_negativo()
    {
        NecesidadRqAppHandler.Construir(Linea(), null, disponible: 0m, convertido: 12m).Pendiente.Should().Be(0m);
    }
}
