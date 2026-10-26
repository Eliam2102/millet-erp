using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports.Externos;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Articulos;

/// <summary>F1-MP-01 S1 (E07 D5/D3): vínculo artículo ↔ producto A+W y puerto de variante.</summary>
public sealed class ArticuloProductoAwTests
{
    private static CompartidoDbContext CrearDb() => new(
        new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeEmpresa());

    private static ProductoAw Producto(string referencia, params (string Clave, decimal Alto, decimal Ancho)[] variantes)
    {
        var p = new ProductoAw(Guid.NewGuid(), referencia, "VIDRIO " + referencia, "m²");
        p.AplicarVariantes(variantes.Select(v => new ProductoAwVarianteDato(v.Clave, v.Alto, v.Ancho, 6m, null)));
        return p;
    }

    private static Articulo Articulo(string clave) => new(Guid.NewGuid(), clave, "Art " + clave, "M2");

    private static ActualizarArticuloCommand Liga(Guid articuloId, Guid? productoAwId, bool limpiar = false) =>
        new(articuloId, null, null, null, null, null, null, null, false, false, false, productoAwId, limpiar);

    [Fact]
    public async Task Liga_Producto_Activo_Y_Rechaza_Segundo_Articulo()
    {
        await using var db = CrearDb();
        var producto = Producto("P1", ("2440x3660", 3660m, 2440m));
        var a1 = Articulo("VID-1");
        var a2 = Articulo("VID-2");
        db.AddRange(producto, a1, a2);
        await db.SaveChangesAsync();
        var handler = new ActualizarArticuloHandler(db, new FakeSaldos(false));

        await handler.Handle(Liga(a1.Id, producto.Id), CancellationToken.None);
        Assert.Equal(producto.Id, a1.ProductoAwId);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => handler.Handle(Liga(a2.Id, producto.Id), CancellationToken.None));
        Assert.Equal("ARTICULO_PRODUCTO_AW_YA_LIGADO", ex.Code);
        Assert.Contains("ya está ligado", ex.Message);
    }

    [Fact]
    public async Task Rechaza_Ligar_Un_Articulo_Que_No_Esta_En_M2()
    {
        await using var db = CrearDb();
        var producto = Producto("P1", ("2440x3660", 3660m, 2440m));
        var pieza = new Articulo(Guid.NewGuid(), "VID-PZA", "Vidrio por pieza", "PZA");
        db.AddRange(producto, pieza);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new ActualizarArticuloHandler(db, new FakeSaldos(false)).Handle(Liga(pieza.Id, producto.Id), CancellationToken.None));
        Assert.Equal("ARTICULO_PRODUCTO_AW_UNIDAD", ex.Code);
        Assert.Null(pieza.ProductoAwId);
    }

    [Fact]
    public async Task Rechaza_Producto_Inactivo()
    {
        await using var db = CrearDb();
        var producto = Producto("P1");
        producto.DarDeBaja(DateTime.UtcNow);
        var a = Articulo("VID-1");
        db.AddRange(producto, a);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => new ActualizarArticuloHandler(db, new FakeSaldos(false)).Handle(Liga(a.Id, producto.Id), CancellationToken.None));
        Assert.Equal("ARTICULO_PRODUCTO_AW_INACTIVO", ex.Code);
    }

    [Fact]
    public async Task Con_Existencia_No_Se_Liga_Ni_Se_Desliga()
    {
        await using var db = CrearDb();
        var producto = Producto("P1");
        var otro = Producto("P2");
        var sinVinculo = Articulo("VID-1");
        var ligado = Articulo("VID-2");
        ligado.AsignarProductoAw(producto.Id);
        db.AddRange(producto, otro, sinVinculo, ligado);
        await db.SaveChangesAsync();
        var handler = new ActualizarArticuloHandler(db, new FakeSaldos(true));

        var liga = await Assert.ThrowsAsync<BusinessRuleException>(
            () => handler.Handle(Liga(sinVinculo.Id, otro.Id), CancellationToken.None));
        Assert.Equal("ARTICULO_CON_SALDO_SIN_VARIANTE", liga.Code);

        var desliga = await Assert.ThrowsAsync<BusinessRuleException>(
            () => handler.Handle(Liga(ligado.Id, null, limpiar: true), CancellationToken.None));
        Assert.Equal("ARTICULO_PRODUCTO_AW_EN_USO", desliga.Code);
        Assert.Equal(producto.Id, ligado.ProductoAwId);
    }

    [Fact]
    public async Task Sin_Existencia_Se_Desliga()
    {
        await using var db = CrearDb();
        var producto = Producto("P1");
        var a = Articulo("VID-1");
        a.AsignarProductoAw(producto.Id);
        db.AddRange(producto, a);
        await db.SaveChangesAsync();

        await new ActualizarArticuloHandler(db, new FakeSaldos(false)).Handle(Liga(a.Id, null, limpiar: true), CancellationToken.None);
        Assert.Null(a.ProductoAwId);
    }

    [Fact]
    public async Task Puerto_Variante_Acepta_Propia_Y_Rechaza_Ajena_Y_Producto_Inactivo()
    {
        await using var db = CrearDb();
        var propio = Producto("P1", ("2440x3660", 3660m, 2440m), ("2140x3300", 3300m, 2140m));
        var ajeno = Producto("P2", ("1830x2440", 2440m, 1830m));
        var inactivo = Producto("P3", ("2440x3660", 3660m, 2440m));
        inactivo.DarDeBaja(DateTime.UtcNow);
        var a = Articulo("VID-1");
        a.AsignarProductoAw(propio.Id);
        var b = Articulo("VID-3");
        b.AsignarProductoAw(inactivo.Id);
        var sinVinculo = Articulo("INS-1");
        db.AddRange(propio, ajeno, inactivo, a, b, sinVinculo);
        await db.SaveChangesAsync();
        var puerto = new VarianteVidrioReadAdapter(db);
        var v2140 = propio.Variantes.Single(v => v.ClaveVariante == "2140x3300");

        var ok = await puerto.ObtenerAsync(a.Id, v2140.Id, CancellationToken.None);
        Assert.Equal("2140x3300", ok.ClaveVariante);
        Assert.Equal(7.0620m, ok.AreaHojaM2);
        Assert.Equal(2, (await puerto.ListarPorArticuloAsync(a.Id, CancellationToken.None)).Count);

        foreach (var (articulo, variante) in new[] { (a.Id, ajeno.Variantes[0].Id), (sinVinculo.Id, v2140.Id), (a.Id, Guid.NewGuid()) })
        {
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => puerto.ObtenerAsync(articulo, variante, CancellationToken.None));
            Assert.Equal("VARIANTE_NO_PERTENECE_AL_ARTICULO", ex.Code);
        }

        var inact = await Assert.ThrowsAsync<BusinessRuleException>(
            () => puerto.ObtenerAsync(b.Id, inactivo.Variantes[0].Id, CancellationToken.None));
        Assert.Equal("VARIANTE_INACTIVA", inact.Code);
        Assert.Empty(await puerto.ListarPorArticuloAsync(b.Id, CancellationToken.None));
    }

    private sealed class FakeSaldos(bool conExistencia) : IAlmacenSaldoQueryPort
    {
        public Task<bool> ArticuloTieneExistenciaAsync(Guid articuloId, CancellationToken cancellationToken) => Task.FromResult(conExistencia);
        public Task<SaldoArticulo?> ConsultarAsync(Guid articuloId, Guid subAlmacenId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SaldoArticulo> ConsultarTotalArticuloAsync(Guid articuloId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DisponibilidadArticuloAlmacen> ConsultarDisponibilidadPorAlmacenAsync(Guid almacenId, Guid articuloId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DisponibilidadArticuloSucursal> ConsultarDisponibilidadPorSucursalAsync(Guid sucursalId, Guid articuloId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<Guid>> FiltrarSubAlmacenesEnRevisionAsync(IReadOnlyCollection<Guid> subAlmacenIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, DisponibilidadArticuloSucursal>> ConsultarDisponibilidadPorSucursalLoteAsync(Guid sucursalId, IReadOnlyCollection<Guid> articuloIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
