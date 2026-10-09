using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Reorden;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.UnitTests.P7;

public sealed class SucursalP6bTests
{
    [Theory]
    [InlineData("recepcion", false, false)]
    [InlineData("recepcion", true, false)]
    [InlineData("recepcion", false, true)]
    [InlineData("salida_almacen", false, false)]
    [InlineData("salida_almacen", true, false)]
    [InlineData("salida_almacen", false, true)]
    public async Task Alcance_incluye_bin_y_origen_y_no_infiere_origen_roto(string tipo, bool ajeno, bool roto)
    {
        await using var f = new P1Fixture();
        var propia = (await f.Db.Almacenes.SingleAsync()).SucursalId;
        var otra = Guid.NewGuid();
        var movimiento = new MovimientoInventario(Guid.NewGuid(), tipo == "recepcion" ? TipoMovimiento.EntradaCompra : TipoMovimiento.SalidaConsumo,
            P1Fixture.EmpresaId, P1Fixture.Fecha);
        if (tipo == "recepcion") movimiento.VincularRecepcionVarianteB(f.DocumentoId, null, "DEMO-P6b.pdf");
        else movimiento.VincularSalida(f.DocumentoId, null, null);
        movimiento.AgregarLinea(new(Guid.NewGuid(), movimiento.Id, 1, f.ArticuloId, 1, "PZA", 1, ubicacionId: f.BinId));
        f.Db.Add(movimiento); await f.Db.SaveChangesAsync();
        var puerto = new Origenes(roto ? [] : [new(f.DocumentoId, [ajeno ? otra : propia])]);
        var lector = new AlmacenSucursalReadAdapter(f.Db, puerto, puerto);
        var documento = Assert.Single(await lector.ListarAsync(tipo, default));
        Assert.Equal(movimiento.Id, documento.Id);
        Assert.Equal(roto ? [] : ajeno ? new[] { propia, otra } : new[] { propia }, documento.Sucursales);
    }

    [Fact]
    public async Task Documento_sin_lineas_no_concede_alcance_por_OC_y_reorden_N2_deriva_de_almacen()
    {
        await using var f = new P1Fixture();
        var alm = await f.Db.Almacenes.SingleAsync();
        var movimiento = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.EntradaCompra, P1Fixture.EmpresaId, P1Fixture.Fecha);
        movimiento.VincularRecepcionVarianteB(f.DocumentoId, null, "DEMO-P6b.pdf");
        var config = new ConfiguracionReorden(Guid.NewGuid(), f.ArticuloId, NivelReorden.Almacen, alm.Id, 1, 10, 2, false, ObjetivoReposicion.Maximo);
        f.Db.AddRange(movimiento, config); await f.Db.SaveChangesAsync();
        var puerto = new Origenes([new(f.DocumentoId, [alm.SucursalId])]);
        var lector = new AlmacenSucursalReadAdapter(f.Db, puerto, puerto);
        Assert.Empty(Assert.Single(await lector.ListarAsync("recepcion", default)).Sucursales);
        Assert.Equal(new[] { alm.SucursalId }, Assert.Single(await lector.ListarAsync("reorden", default)).Sucursales);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recepcion_con_documento_fiscal_ajeno_no_se_autoriza_solo_por_bin_y_OC(bool cfdi)
    {
        await using var f = new P1Fixture();
        var propia = (await f.Db.Almacenes.SingleAsync()).SucursalId;
        var ajena = Guid.NewGuid(); var fiscal = Guid.NewGuid();
        var movimiento = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.EntradaCompra, P1Fixture.EmpresaId, P1Fixture.Fecha);
        if (cfdi) movimiento.VincularRecepcionVarianteA(f.DocumentoId, null, fiscal, null);
        else { movimiento.VincularRecepcionVarianteB(f.DocumentoId, null, "DEMO.pdf"); movimiento.ConciliarConFacturaProveedor(fiscal); }
        movimiento.AgregarLinea(new(Guid.NewGuid(), movimiento.Id, 1, f.ArticuloId, 1, "PZA", 1, ubicacionId: f.BinId));
        f.Db.Add(movimiento); await f.Db.SaveChangesAsync();
        var lector = new AlmacenSucursalReadAdapter(f.Db, new Origenes([new(f.DocumentoId, [propia])]), new Origenes([new(fiscal, [ajena])]));
        Assert.Equal(new[] { propia, ajena }, Assert.Single(await lector.ListarAsync("recepcion", default)).Sucursales);
    }

    [Fact]
    public async Task Bandejas_filtran_antes_de_totales_y_paginacion_y_vales_vencidos()
    {
        await using var f = new P1Fixture();
        var propia = await f.MovimientoAsync(TipoMovimiento.EntradaCompra);
        await f.MovimientoAsync(TipoMovimiento.EntradaCompra);
        var recepciones = await new ListarRecepcionesHandler(f.Db, f.Oc).Handle(
            new(null, null, null, null, null, 0, 1) { DocumentosPermitidos = [propia.Id] }, default);
        Assert.Equal(1, recepciones.Total); Assert.Equal(propia.Id, Assert.Single(recepciones.Items).Id);
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        var ajeno = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        vale.EstablecerPlazoRegularizacion(DateTimeOffset.UtcNow.AddHours(-1));
        ajeno.EstablecerPlazoRegularizacion(DateTimeOffset.UtcNow.AddHours(-1));
        await f.Db.SaveChangesAsync();
        var salidas = await new ListarSalidasHandler(f.Db, f.Rq).Handle(
            new(null, null, null, null, null, null, true, true, 0, 1, true) { DocumentosPermitidos = [vale.Id] }, default);
        Assert.Equal(1, salidas.Total); Assert.Equal(vale.Id, Assert.Single(salidas.Items).Id);
        var config = new ConfiguracionReorden(Guid.NewGuid(), f.ArticuloId, NivelReorden.Sucursal, Guid.NewGuid(), 1, 10, 2, false, ObjetivoReposicion.Maximo);
        f.Db.AddRange(config, new ConfiguracionReorden(Guid.NewGuid(), f.ArticuloId, NivelReorden.Sucursal, Guid.NewGuid(), 1, 10, 2, false, ObjetivoReposicion.Maximo));
        await f.Db.SaveChangesAsync();
        var configs = await new ListarConfiguracionesReordenHandler(f.Db, new P1Fixture.Articulos()).Handle(
            new(null, null, null, null, 0, 1) { DocumentosPermitidos = [config.Id] }, default);
        Assert.Equal(1, configs.Total); Assert.Equal(config.Id, Assert.Single(configs.Items).Id);
    }

    private sealed class Origenes(IReadOnlyList<DocumentoSucursales> documentos) : IComprasSucursalReadPort, ICxpSucursalReadPort
    {
        public Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct) => Task.FromResult(documentos);
    }
}
