using Millet.Almacen.Application.PublicPorts;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.UnitTests.Reportes;

public sealed class MovimientosCompraReadTests
{
    [Fact]
    public async Task DEMO_Convierte_unidad_y_conserva_documentos_de_devolucion_al_corte()
    {
        await using var f = new P1Fixture();
        var rec = await f.MovimientoAsync(TipoMovimiento.EntradaCompra, 20);
        var salida = await f.MovimientoAsync(TipoMovimiento.SalidaPorDevolucionAProveedor, 4);
        var dev = new DevolucionAProveedor(Guid.NewGuid(), P1Fixture.EmpresaId, Guid.NewGuid(), "DEMO ficticio", Guid.NewGuid(),
            recepcionOrigenId: rec.Id, ordenCompraOrigenId: f.DocumentoId);
        dev.AgregarLinea(new LineaDevolucionProveedor(Guid.NewGuid(), dev.Id, 1, f.ArticuloId, 4, "PZA", 25, rec.Lineas.Single().Id));
        dev.AgregarEvidencia(new EvidenciaDevolucionProveedor(Guid.NewGuid(), dev.Id, "foto", "DEMO.txt", "demo/ficticio.txt"));
            dev.SolicitarAutorizacion(); dev.Autorizar(Guid.NewGuid()); dev.MarcarRegistrada(salida.Id, salida.Folio!);
        f.Db.DevolucionesProveedor.Add(dev); await f.Db.SaveChangesAsync();
        var port = new RecepcionesCostoReadAdapter(f.Db, new Conversion());
        PartidaOcLectura[] partidas = [new(f.DocumentoId, f.LineaId, f.ArticuloId, "PAR")];
        var r = await port.LeerMovimientosAsync(P1Fixture.EmpresaId, partidas, P1Fixture.Fecha, default);
        Assert.Equal(2, r.Count);
        Assert.Equal(10m, Assert.Single(r, x => !x.EsDevolucion).Cantidad);
        Assert.Equal(2m, Assert.Single(r, x => x.EsDevolucion).Cantidad);
        Assert.Equal(dev.Id, Assert.Single(r, x => x.EsDevolucion).DocumentoId);
        Assert.Empty(await port.LeerMovimientosAsync(P1Fixture.EmpresaId, partidas, P1Fixture.Fecha.AddDays(-1), default));
        Assert.Empty(await port.LeerMovimientosAsync(Guid.NewGuid(), partidas, P1Fixture.Fecha, default));
    }
    private sealed class Conversion : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad, string? unidadCapturada,
            string unidadDocumento, CancellationToken ct) => Task.FromResult(new ConversionUnidad(2 * cantidad, "PZA", cantidad, 2, "PAR", cantidad));
    }
}
