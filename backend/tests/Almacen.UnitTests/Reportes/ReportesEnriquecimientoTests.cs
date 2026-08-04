using System;
using System.Collections.Generic;
using System.Linq;
using Millet.Almacen.Application.Reportes;
using Millet.Almacen.Domain.Ports;
using Xunit;

namespace Millet.Almacen.UnitTests.Reportes;

/// <summary>
/// Unit de los helpers puros de enriquecimiento (ADR-0042) de MP CNK y Alfak:
/// <c>ExtraerArticuloIdsDistintos</c> + <c>AplicarArticulos</c>. Sin BD ni
/// read-port real — <c>AplicarArticulos</c> recibe el diccionario ya resuelto,
/// así que la lógica (enriquecer cuando hay master, fallback a null cuando no)
/// se prueba determinista, igual que el patrón de #424.
/// </summary>
public class ReportesEnriquecimientoTests
{
    private static ArticuloLectura Art(Guid id, string clave, string desc)
        => new(id, clave, desc, "PZA", null, null, true);

    [Fact]
    public void MpCnk_AplicarArticulos_Enriquece_Y_FallbackNull()
    {
        var idResuelto = Guid.NewGuid();
        var idSinMaster = Guid.NewGuid(); // no está en el diccionario → fallback
        var filas = new List<ExistenciaMpCnkFila>
        {
            new(Guid.NewGuid(), idResuelto, 10, 10, 5, 50, "Sub MID", null, null),
            new(Guid.NewGuid(), idSinMaster, 3, 3, 2, 6, "Sub MID", null, null),
        };
        var dict = new Dictionary<Guid, ArticuloLectura>
        {
            [idResuelto] = Art(idResuelto, "ACC1", "Artículo resuelto"),
        };

        // Ids distintos = 2 (uno por artículo).
        Assert.Equal(2, ExistenciaMpCnkHandler.ExtraerArticuloIdsDistintos(filas).Count);

        var res = ExistenciaMpCnkHandler.AplicarArticulos(filas, dict);
        var resuelta = res.Single(f => f.ArticuloId == idResuelto);
        var sinMaster = res.Single(f => f.ArticuloId == idSinMaster);

        Assert.Equal("ACC1", resuelta.ArticuloClave);
        Assert.Equal("Artículo resuelto", resuelta.ArticuloDescripcion);
        Assert.Equal("Sub MID", resuelta.SubAlmacenNombre); // sub-almacén (JOIN) intacto
        Assert.Null(sinMaster.ArticuloClave);               // fallback → FE cae al id
        Assert.Null(sinMaster.ArticuloDescripcion);
    }

    [Fact]
    public void Alfak_AplicarArticulos_Enriquece_Y_FallbackNull()
    {
        var idResuelto = Guid.NewGuid();
        var idSinMaster = Guid.NewGuid();
        var filas = new List<AlfakHistorialFila>
        {
            new(Guid.NewGuid(), idResuelto, 0, 40, 12, 0, 0, 28, 1250, 35000, "Sub MID", null, null),
            new(Guid.NewGuid(), idSinMaster, 0, 5, 1, 0, 0, 4, 10, 40, "Sub MID", null, null),
        };
        var dict = new Dictionary<Guid, ArticuloLectura>
        {
            [idResuelto] = Art(idResuelto, "ACC1", "Artículo resuelto"),
        };

        var res = AlfakHistorialAlmacenHandler.AplicarArticulos(filas, dict);

        Assert.Equal("ACC1", res.Single(f => f.ArticuloId == idResuelto).ArticuloClave);
        Assert.Null(res.Single(f => f.ArticuloId == idSinMaster).ArticuloClave);
    }

    [Fact]
    public void ExtraerArticuloIdsDistintos_Dedup_PorArticulo()
    {
        var id = Guid.NewGuid();
        var filas = new List<ExistenciaMpCnkFila>
        {
            new(Guid.NewGuid(), id, 1, 1, 1, 1, "S", null, null),
            new(Guid.NewGuid(), id, 2, 2, 1, 2, "S", null, null),
        };
        Assert.Single(ExistenciaMpCnkHandler.ExtraerArticuloIdsDistintos(filas));
    }
}
