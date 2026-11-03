using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.PublicAdapters;
using Millet.ProductoTerminado.UnitTests.Application;

namespace Millet.ProductoTerminado.UnitTests.Infrastructure;

public sealed class SucursalReadAdapterTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static EscaneoPt Escaneo(Guid? sucursal) =>
        EscaneoPt.Registrar(EntornoPt.Empresa, "AW", "E-1", "P-1", 1, 1, "1", 1, null, Ahora.DateTime, Ahora, "01", sucursal, Ahora);

    [Fact]
    public async Task Pieza_devuelve_su_sucursal_fisica()
    {
        using var e = new EntornoPt();
        var pedido = PedidoPt.Crear(EntornoPt.Empresa, Guid.NewGuid(), "P-1", Guid.NewGuid(), null, null);
        var pieza = PiezaPt.Alta(EntornoPt.Empresa, pedido, 1, 1, "E-1", EntornoPt.Sucursal, Ahora, Guid.NewGuid());
        e.Db.Piezas.Add(pieza);
        await e.Db.SaveChangesAsync();
        var r = await new ProductoTerminadoSucursalReadAdapter(e.Db).ObtenerAsync("pieza_pt", pieza.Id, default);
        r!.Sucursales.Should().Equal(EntornoPt.Sucursal);
    }

    [Fact]
    public async Task Escaneo_con_sucursal_la_devuelve_y_sin_sucursal_lista_vacia()
    {
        using var e = new EntornoPt();
        var con = Escaneo(EntornoPt.Sucursal);
        var sin = Escaneo(null);
        e.Db.Escaneos.AddRange(con, sin);
        await e.Db.SaveChangesAsync();
        var adapter = new ProductoTerminadoSucursalReadAdapter(e.Db);
        (await adapter.ObtenerAsync("escaneo_pt", con.Id, default))!.Sucursales.Should().Equal(EntornoPt.Sucursal);
        (await adapter.ObtenerAsync("escaneo_pt", sin.Id, default))!.Sucursales.Should().BeEmpty();
    }

    [Fact]
    public async Task Id_o_tipo_desconocido_devuelven_null()
    {
        using var e = new EntornoPt();
        var adapter = new ProductoTerminadoSucursalReadAdapter(e.Db);
        (await adapter.ObtenerAsync("pieza_pt", Guid.NewGuid(), default)).Should().BeNull();
        (await adapter.ObtenerAsync("otro", Guid.NewGuid(), default)).Should().BeNull();
        (await adapter.ListarAsync("otro", default)).Should().BeEmpty();
    }
}
