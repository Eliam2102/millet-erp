using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Revision;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Almacen.UnitTests.TestSupport;

namespace Millet.Almacen.UnitTests;

public sealed class RemanentesCostoTests
{
    [Theory]
    [InlineData(100, 0, 0, 100)]
    [InlineData(60, 0, 40, 60)]
    [InlineData(60, 30, 40, 30)]
    public async Task Liberacion_sigue_la_partida_y_consumo_por_ubicacion(int liberadas, int consumo, int revision, int rack)
    {
        await using var db = CrearDb();
        var origen = Guid.NewGuid(); var destino = Guid.NewGuid(); var articulo = Guid.NewGuid();
        var entrada = Movimiento(db, TipoMovimiento.EntradaCompra, origen, articulo, 100);
        var salida = Movimiento(db, TipoMovimiento.SalidaPorLiberacionRevision, origen, articulo, liberadas);
        var rei = Movimiento(db, TipoMovimiento.ReincorporacionTrasRevision, destino, articulo, liberadas);
        rei.VincularSalidaLiberacion(salida.Id);
        if (consumo > 0) Movimiento(db, TipoMovimiento.SalidaConsumo, destino, articulo, consumo);
        await db.SaveChangesAsync();
        var r = await RecepcionesCostoReadAdapter.RemanentesAsync(db, P1Fixture.EmpresaId, entrada.Lineas.Single().Id, articulo, NullLogger.Instance, default);
        Assert.Equal(revision, r.GetValueOrDefault(origen)); Assert.Equal(rack, r.GetValueOrDefault(destino));
    }

    [Fact]
    public async Task Historico_dictamen_pool_destino_entrada_posterior_y_segundo_traslado()
    {
        await using var db = CrearDb();
        var origen = Guid.NewGuid(); var destino = Guid.NewGuid(); var tercero = Guid.NewGuid(); var articulo = Guid.NewGuid();
        var entrada = Movimiento(db, TipoMovimiento.EntradaCompra, origen, articulo, 100);
        Movimiento(db, TipoMovimiento.EntradaCompra, destino, articulo, 100);
        var salida = Movimiento(db, TipoMovimiento.SalidaPorLiberacionRevision, origen, articulo, 100);
        var rei = Movimiento(db, TipoMovimiento.ReincorporacionTrasRevision, destino, articulo, 100);
        db.DictamenesRevision.Add(new DictamenRevision(Guid.NewGuid(), P1Fixture.EmpresaId, entrada.Lineas.Single().Id, entrada.Id,
            ResultadoDictamen.Liberar, 100, "Liberación", Guid.NewGuid(), movimientoSalidaRevisionId: salida.Id, movimientoEntradaId: rei.Id));
        Movimiento(db, TipoMovimiento.SalidaConsumo, destino, articulo, 100); // quedan 50 de la partida
        Movimiento(db, TipoMovimiento.EntradaCompra, destino, articulo, 100); // no repone; pool = 200
        var segundo = Movimiento(db, TipoMovimiento.SalidaPorLiberacionRevision, destino, articulo, 100);
        Movimiento(db, TipoMovimiento.ReincorporacionTrasRevision, tercero, articulo, 100).VincularSalidaLiberacion(segundo.Id);
        await db.SaveChangesAsync();
        var r = await RecepcionesCostoReadAdapter.RemanentesAsync(db, P1Fixture.EmpresaId, entrada.Lineas.Single().Id, articulo, NullLogger.Instance, default);
        Assert.Equal(25, r[destino]); Assert.Equal(25, r[tercero]); Assert.Equal(50, r.Values.Sum());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Par_ausente_o_ambiguo_es_consumo_y_advierte(bool ambiguo)
    {
        await using var db = CrearDb();
        var origen = Guid.NewGuid(); var articulo = Guid.NewGuid();
        var entrada = Movimiento(db, TipoMovimiento.EntradaCompra, origen, articulo, 100);
        var salida = Movimiento(db, TipoMovimiento.SalidaPorLiberacionRevision, origen, articulo, 100);
        if (ambiguo)
            for (var i = 0; i < 2; i++) Movimiento(db, TipoMovimiento.ReincorporacionTrasRevision, Guid.NewGuid(), articulo, 100).VincularSalidaLiberacion(salida.Id);
        await db.SaveChangesAsync();
        var log = new CapturaLog();
        var r = await RecepcionesCostoReadAdapter.RemanentesAsync(db, P1Fixture.EmpresaId, entrada.Lineas.Single().Id, articulo, log, default);
        Assert.Empty(r); Assert.Equal(1, log.Advertencias);
    }

    [Theory]
    [InlineData(TipoMovimiento.SalidaConsumo)]
    [InlineData(TipoMovimiento.SalidaPorVale)]
    [InlineData(TipoMovimiento.SalidaPorDevolucionAProveedor)]
    [InlineData(TipoMovimiento.BajaPorDano)]
    [InlineData(TipoMovimiento.AjusteNegativo)]
    public async Task Salidas_reales_consumen_y_entrada_no_repone(TipoMovimiento tipo)
    {
        await using var db = CrearDb();
        var origen = Guid.NewGuid(); var articulo = Guid.NewGuid();
        var entrada = Movimiento(db, TipoMovimiento.EntradaCompra, origen, articulo, 1000);
        Movimiento(db, tipo, origen, articulo, 600);
        Movimiento(db, TipoMovimiento.EntradaCompra, origen, articulo, 500);
        await db.SaveChangesAsync();
        var r = await RecepcionesCostoReadAdapter.RemanentesAsync(db, P1Fixture.EmpresaId, entrada.Lineas.Single().Id, articulo, NullLogger.Instance, default);
        Assert.Equal(400, r[origen]); Assert.False(tipo.EsTraslado());
    }

    private static InMemoryAlmacenDbContext CrearDb() => new(
        new DbContextOptionsBuilder<InMemoryAlmacenDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new P1Fixture.Contexto());

    private static MovimientoInventario Movimiento(InMemoryAlmacenDbContext db, TipoMovimiento tipo, Guid ubicacion, Guid articulo, decimal cantidad)
    {
        var m = new MovimientoInventario(Guid.CreateVersion7(), tipo, P1Fixture.EmpresaId, new(2026, 10, 22));
        m.AgregarLinea(new(Guid.NewGuid(), m.Id, 1, articulo, cantidad, "PZA", 10, ubicacionId: ubicacion));
        m.Registrar(FolioMovimiento.Construir(tipo, 2026, db.Movimientos.Local.Count + 1), Guid.NewGuid());
        db.Movimientos.Add(m); return m;
    }

    private sealed class CapturaLog : ILogger
    {
        public int Advertencias { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (level == LogLevel.Warning) Advertencias++; }
    }
}
