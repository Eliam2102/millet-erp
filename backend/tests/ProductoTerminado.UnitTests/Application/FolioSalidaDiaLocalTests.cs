using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Domain;

namespace Millet.ProductoTerminado.UnitTests.Application;

/// <summary>M11 (prueba manual S3-4): el folio HS lleva el día local, no el de UTC.</summary>
public sealed class FolioSalidaDiaLocalTests
{
    [Fact]
    public void M11_hoja_armada_a_las_23_34_del_8_nov_lleva_folio_del_8_nov()
    {
        // 8-nov 23:34 en México = 9-nov 05:34 UTC.
        var ahora = new DateTimeOffset(2026, 11, 9, 5, 34, 0, TimeSpan.Zero);

        var salida = SalidaPt.Crear(Guid.NewGuid(), Guid.NewGuid(), "Destino DEMO", false, null, null, null, null,
            null, null, null, null, ahora, HoraAw.FechaLocal(ahora));

        salida.Folio.Should().StartWith("HS-20261108-");
        salida.CreadaAt.Should().Be(ahora);
    }
}
