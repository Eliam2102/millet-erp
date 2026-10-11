using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Trazabilidad;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Trazabilidad;
using Millet.SharedKernel.Application;

namespace Millet.Compras.UnitTests.PublicAdapters;

public sealed class ArbolDocumentosPrubTests
{
    [Fact]
    public async Task Orden_compartida_por_dos_facturas_aparece_una_sola_vez()
    {
        var oc = Nodo(TipoDocumentoTrazabilidad.OrdenCompra, "OC-MID-1");
        var f1 = Nodo(TipoDocumentoTrazabilidad.FacturaProveedor, "F-1", [oc]);
        var f2 = Nodo(TipoDocumentoTrazabilidad.FacturaProveedor, "F-2", [oc]);
        var pago = Nodo(TipoDocumentoTrazabilidad.PagoProveedor, "P-1", [f1, f2]);
        await using var db = new ComprasDbContext(new DbContextOptionsBuilder<ComprasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
        var proveedor = new Grafo([oc, f1, f2, pago]);
        var arbol = await new ObtenerArbolDocumentosService(db, [proveedor])
            .ObtenerAsync(pago.TipoDocumento, pago.Id, default);
        Assert.NotNull(arbol);
        Assert.Equal(2, arbol.Ascendentes.Count);
        Assert.Single(arbol.Ascendentes.SelectMany(n => n.Ascendentes), n => n.Id == oc.Id);
    }

    private static NodoArbolDocumento Nodo(TipoDocumentoTrazabilidad tipo, string folio, IReadOnlyList<NodoArbolDocumento>? padres = null) =>
        new(tipo, Guid.NewGuid(), folio, "Autorizada", DateTimeOffset.UtcNow, padres ?? [], []);

    private sealed class Grafo(IReadOnlyList<NodoArbolDocumento> nodos) : IProveedorNodosTrazabilidad
    {
        public Task<NodoArbolDocumento?> ObtenerNodoAsync(TipoDocumentoTrazabilidad tipo, Guid id, CancellationToken ct) =>
            Task.FromResult(nodos.SingleOrDefault(n => n.TipoDocumento == tipo && n.Id == id));
        public Task<IReadOnlyList<NodoArbolDocumento>> ObtenerDescendentesAsync(TipoDocumentoTrazabilidad tipoOrigen, Guid idOrigen, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NodoArbolDocumento>>([]);
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
