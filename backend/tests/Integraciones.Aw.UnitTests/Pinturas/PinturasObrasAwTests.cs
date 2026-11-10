using System.Data;
using Millet.Integraciones.Aw.Application.Pinturas.Catalogos;
using Millet.Integraciones.Aw.Infrastructure.Pinturas;

namespace Millet.Integraciones.Aw.UnitTests.Pinturas;

/// <summary>Obras de A+W (KA_OBJEKT) hacia Pinturas.</summary>
public sealed class PinturasObrasAwTests
{
    [Fact]
    public async Task Obras_mapea_filas_de_KA_OBJEKT_con_origen_AW_y_bloqueadas_inactivas()
    {
        var t = new DataTable();
        t.Columns.Add("ID", typeof(int));
        t.Columns.Add("BEZ", typeof(string));
        t.Columns.Add("KZ_GESPERRT", typeof(int));
        t.Rows.Add(7, "  Torre Norte ", 0);
        t.Rows.Add(8, "Obra cerrada", 1);

        var obras = await AwPinturasObrasSqlSource.Leer(t.CreateDataReader(), CancellationToken.None);

        obras.Should().Equal(
            new FilaPinturasObra("7", "Torre Norte", FilaPinturasObra.OrigenAw, "7", true),
            new FilaPinturasObra("8", "Obra cerrada", FilaPinturasObra.OrigenAw, "8", false));
    }
}
