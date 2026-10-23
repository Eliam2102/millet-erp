using Millet.Catalogos.Domain;
using Millet.Compras.Domain;
using Millet.DatosMaestros.Domain;

namespace Millet.Compras.UnitTests.Domain;

public class ArticulosPorClasificacionTests
{
    [Theory]
    // Categoría mapeada: sólo su clasificación, sin importar la naturaleza.
    [InlineData(Clasificacion.MateriaPrima, "INSUMOS PRODUCCION", Naturaleza.Estandar, true)]
    [InlineData(Clasificacion.OrdenCompra, "INSUMOS PRODUCCION", Naturaleza.Estandar, false)]
    [InlineData(Clasificacion.Servicio, "Servicios técnicos", Naturaleza.Estandar, true)]
    [InlineData(Clasificacion.OrdenCompra, "Servicios técnicos", Naturaleza.Servicio, false)]
    [InlineData(Clasificacion.Pinturas, "Pinturas", Naturaleza.Estandar, true)]
    [InlineData(Clasificacion.MateriaPrima, "Pinturas", Naturaleza.Estandar, false)]
    [InlineData(Clasificacion.OrdenCompra, "MATER. DE OFICINA", Naturaleza.Critico, true)]
    // Sin categoría (o no mapeada): servicio ↔ Servicio; lo demás ↔ OrdenCompra.
    [InlineData(Clasificacion.Servicio, null, Naturaleza.Servicio, true)]
    [InlineData(Clasificacion.Servicio, null, Naturaleza.Estandar, false)]
    [InlineData(Clasificacion.OrdenCompra, null, Naturaleza.Estandar, true)]
    [InlineData(Clasificacion.OrdenCompra, "Otra", Naturaleza.Riesgo, true)]
    [InlineData(Clasificacion.OrdenCompra, null, Naturaleza.Servicio, false)]
    [InlineData(Clasificacion.MateriaPrima, null, Naturaleza.Estandar, false)]
    [InlineData(Clasificacion.Pinturas, "Otra", Naturaleza.Estandar, false)]
    public void Aplica_y_Filtro_coinciden(
        Clasificacion clasificacion, string? categoria, Naturaleza naturaleza, bool esperado)
    {
        var articulo = new Articulo(
            Guid.NewGuid(), "T-1", "Prueba", "PZA", naturaleza: naturaleza, categoria: categoria);

        Assert.Equal(esperado, ArticulosPorClasificacion.Aplica(clasificacion, categoria, naturaleza));
        Assert.Equal(esperado, ArticulosPorClasificacion.Filtro(clasificacion).Compile()(articulo));
    }
}
