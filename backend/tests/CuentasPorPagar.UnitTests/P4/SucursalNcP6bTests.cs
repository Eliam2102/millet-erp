using Millet.CuentasPorPagar.Infrastructure;
using Millet.CuentasPorPagar.UnitTests.P8;
using Millet.SharedKernel.Application;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Anticipo = Millet.CuentasPorPagar.Domain.AnticipoProveedor.AnticipoProveedor;
using Nc = Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor;

namespace Millet.CuentasPorPagar.UnitTests.P4;

public sealed class SucursalNcP6bTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task NC_07_hereda_sucursal_solo_de_anticipo_con_OC_verificable(bool vinculada, bool conOc)
    {
        await using var a = new P8Fixture();
        var orden = Guid.NewGuid();
        var anticipo = Anticipo.Capturar(a.Current!.Value, null, Guid.NewGuid().ToString(), a.Proveedor,
            "FANT", "Prueba ficticia P6b", a.UtcNow, "MXN", null, 100m, conOc ? orden : null, null, a.UtcNow);
        var nc = NuevaNc(a, TipoRelacionCfdi.AmortizacionAnticipo, anticipo.UuidCfdi, null);
        if (vinculada) nc.VincularAnticipoOrigen(anticipo.Id, a.UtcNow);
        a.Db.AddRange(anticipo, nc); await a.Db.SaveChangesAsync();
        var lector = new CxpSucursalReadAdapter(a.Db, new Ordenes(orden, a.Sucursal));
        var documento = (await lector.ListarAsync("nota_credito_proveedor", default)).Single(x => x.Id == nc.Id);
        documento.Sucursales.Should().BeEquivalentTo(vinculada && conOc ? new[] { a.Sucursal } : Array.Empty<Guid>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NC_conserva_sucursal_de_factura_y_sin_origen_no_infiere_por_proveedor(bool conFactura)
    {
        await using var a = new P8Fixture();
        var factura = a.Nueva();
        var nc = NuevaNc(a, TipoRelacionCfdi.NotaCredito, Guid.NewGuid().ToString(), conFactura ? factura.Id : null);
        a.Db.AddRange(factura, nc); await a.Db.SaveChangesAsync();
        var lector = new CxpSucursalReadAdapter(a.Db, new Ordenes(Guid.NewGuid(), a.Sucursal));
        var documento = (await lector.ListarAsync("nota_credito_proveedor", default)).Single(x => x.Id == nc.Id);
        documento.Sucursales.Should().BeEquivalentTo(conFactura ? new[] { a.Sucursal } : Array.Empty<Guid>());
    }

    private static Nc NuevaNc(P8Fixture a, TipoRelacionCfdi relacion, string uuid, Guid? factura) => Nc.Capturar(
        a.Current!.Value, null, Guid.NewGuid().ToString(), a.Proveedor, "Prueba ficticia P6b", null, a.UtcNow,
        "MXN", null, 10m, 0m, 0m, 10m, TipoNotaCredito.Descuento, relacion, uuid, factura, null, a.UtcNow);

    private sealed class Ordenes(Guid id, Guid sucursal) : IComprasSucursalReadPort
    {
        public Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DocumentoSucursales>>([new(id, [sucursal])]);
    }
}
