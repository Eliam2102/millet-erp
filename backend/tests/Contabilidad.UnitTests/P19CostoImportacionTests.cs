using System.Text.Json;
using Millet.Contabilidad.Application.Motor;
namespace Millet.Contabilidad.UnitTests;
public sealed class P19CostoImportacionTests
{
    [Theory]
    [InlineData(2000, 1000)]
    [InlineData(0, 3000)]
    [InlineData(-2000, -1000)]
    public void Usa_importes_confirmados_y_conserva_clave_al_reentregar(decimal inventario, decimal gasto)
    {
        var expediente = Guid.NewGuid(); var articulo = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new { EmpresaId = Guid.NewGuid(), OcurridoEn = DateTimeOffset.UtcNow,
            ExpedienteId = expediente, SucursalId = Guid.NewGuid(), NumeroVersion = 2,
            Partidas = new[] { new { PartidaId = Guid.NewGuid(), ArticuloId = articulo,
                DiferenciaMxn = inventario + gasto, InventarioMxn = inventario, GastoImportacionMxn = gasto } } });
        var a = NormalizadorEventos.Normalizar("almacen.expediente.costo-aplicado.v1", Guid.NewGuid(), payload);
        var b = NormalizadorEventos.Normalizar("almacen.expediente.costo-aplicado.v1", Guid.NewGuid(), payload);
        Assert.Equal(inventario + gasto, a.Importes["Total"]);
        Assert.Equal(gasto, a.Importes["GastoImportacion"]);
        Assert.Equal(inventario, a.Lineas.Single().Importe);
        Assert.Equal(articulo, a.Lineas.Single().ArticuloId);
        Assert.Equal(a.Llave, b.Llave);
        Assert.Equal(expediente, a.Documento.Id);
    }
    [Fact]
    public void Rechaza_importes_que_no_cuadran()
    {
        var payload = JsonSerializer.Serialize(new { EmpresaId = Guid.NewGuid(), ExpedienteId = Guid.NewGuid(),
            SucursalId = Guid.NewGuid(), NumeroVersion = 1, Partidas = new[] { new { DiferenciaMxn = 10, InventarioMxn = 5, GastoImportacionMxn = 4 } } });
        Assert.Throws<EventoIlegibleException>(() => NormalizadorEventos.Normalizar("almacen.expediente.costo-aplicado.v1", Guid.NewGuid(), payload));
    }
}
