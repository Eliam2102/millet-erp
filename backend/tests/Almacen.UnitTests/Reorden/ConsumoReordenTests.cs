using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Almacen.Application.Reorden;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Ports.Externos;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Almacen.UnitTests.Reorden;

/// <summary>F1-ALM-10: consumo promedio, cobertura y sugerencia de mínimo/máximo (CA1, CA2, CA4).</summary>
public class ConsumoReordenTests
{
    private static readonly DateTimeOffset Hoy = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Empresa = Guid.NewGuid(), Suc = Guid.NewGuid(), Alm = Guid.NewGuid(),
        Sub = Guid.NewGuid(), Ubic = Guid.NewGuid(), Art = Guid.NewGuid();

    [Fact]
    public void Consumo_cobertura_y_sugerencia_son_aritmetica_simple()
    {
        ConsumoReorden.Diario(90m, 90).Should().Be(1.0m);
        ConsumoReorden.Cobertura(8m, 1.0m).Should().Be(8.0m);
        ConsumoReorden.Cobertura(8m, 0m).Should().BeNull();
        ConsumoReorden.Sugerencia(2m, 7, 30).Should().Be((14m, 74m));   // 2×7 ; 14 + 2×30
        ConsumoReorden.Sugerencia(0m, 7, 30).Should().BeNull();
    }

    [Fact]
    public async Task Handler_calcula_consumo_de_salidas_registradas_dentro_de_la_ventana_y_no_cambia_la_config()
    {
        await using var db = await NuevaDbAsync();
        Salida(db, TipoMovimiento.SalidaConsumo, EstadoMovimiento.Registrado, Hoy.AddDays(-10), 60m);
        Salida(db, TipoMovimiento.SalidaPorVale, EstadoMovimiento.Registrado, Hoy.AddDays(-80), 30m);
        Salida(db, TipoMovimiento.SalidaConsumo, EstadoMovimiento.Registrado, Hoy.AddDays(-100), 500m);   // fuera de la ventana
        Salida(db, TipoMovimiento.SalidaConsumo, EstadoMovimiento.Borrador, Hoy.AddDays(-5), 500m);       // no registrada
        Salida(db, TipoMovimiento.EntradaCompra, EstadoMovimiento.Registrado, Hoy.AddDays(-5), 500m);     // no es salida
        await db.SaveChangesAsync();

        var handler = new EvaluarReordenHandler(db, new Saldos(8m), new Vivo(), new TestClock(Hoy), Options.Create(new ReordenConsumoOptions()));
        var f = Assert.Single(await handler.Handle(new EvaluarReordenQuery(), default));

        f.ConsumoDiario.Should().Be(1.0m);              // 90 salidas / 90 días
        f.CoberturaDias.Should().Be(8.0m);              // existencia 8 ÷ 1.0
        f.MinimoSugerido.Should().Be(7m);
        f.MaximoSugerido.Should().Be(37m);
        f.Faltante.Should().Be(32m);                    // existencia 8, vivo 0, punto 10, máximo 40
        var cfg = await db.ConfiguracionesReorden.AsNoTracking().SingleAsync();
        (cfg.Minimo, cfg.Maximo, cfg.PuntoReorden).Should().Be((5m, 40m, 10m));   // la sugerencia no cambia parámetros
    }

    private static async Task<InMemoryAlmacenDbContext> NuevaDbAsync()
    {
        var opts = new DbContextOptionsBuilder<InMemoryAlmacenDbContext>().UseInMemoryDatabase($"reorden-consumo-{Guid.NewGuid():N}").Options;
        var db = new InMemoryAlmacenDbContext(opts, new EmpresaBypass());
        await db.Database.EnsureCreatedAsync();
        db.Almacenes.Add(new Almacen.Domain.Catalogo.Almacen(Alm, "ALM", "Almacén", Suc));
        db.SubAlmacenes.Add(new SubAlmacen(Sub, Alm, "INS", "Insumos", TipoSubAlmacen.Insumos));
        db.Ubicaciones.Add(new Ubicacion(Ubic, Sub, "U1", "Ubicación 1"));
        db.ConfiguracionesReorden.Add(new ConfiguracionReorden(Guid.NewGuid(), Art, NivelReorden.Almacen, Alm, 5m, 40m, 10m, true, ObjetivoReposicion.Maximo));
        await db.SaveChangesAsync();
        return db;
    }

    private static void Salida(InMemoryAlmacenDbContext db, TipoMovimiento tipo, EstadoMovimiento estado, DateTimeOffset fecha, decimal cantidad)
    {
        var mov = new MovimientoInventario(Guid.NewGuid(), tipo, Empresa, DateOnly.FromDateTime(fecha.UtcDateTime));
        mov.AgregarLinea(new LineaMovimiento(Guid.NewGuid(), mov.Id, 1, Art, cantidad, "PZA", 1m, ubicacionId: Ubic));
        if (estado == EstadoMovimiento.Registrado) mov.Registrar(new FolioMovimiento($"M-X-{Guid.NewGuid():N}"[..20]), Guid.NewGuid());
        db.Movimientos.Add(mov);
    }

    private sealed class EmpresaBypass : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Nada();
        private sealed class Nada : IDisposable { public void Dispose() { } }
    }

    private sealed class Vivo : IComprasPedidoVivoReadPort
    {
        public Task<IReadOnlyDictionary<PedidoVivoClave, decimal>> ObtenerVivoDeSistemaAsync(IReadOnlyCollection<PedidoVivoClave> pares, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<PedidoVivoClave, decimal>>(new Dictionary<PedidoVivoClave, decimal>());
    }

    private sealed class Saldos(decimal existencia) : IAlmacenSaldoQueryPort
    {
        public Task<DisponibilidadArticuloAlmacen> ConsultarDisponibilidadPorAlmacenAsync(Guid almacenId, Guid articuloId, CancellationToken ct)
            => Task.FromResult(new DisponibilidadArticuloAlmacen(almacenId, articuloId, existencia, existencia));
        public Task<SaldoArticulo?> ConsultarAsync(Guid articuloId, Guid subAlmacenId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SaldoArticulo> ConsultarTotalArticuloAsync(Guid articuloId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ArticuloTieneExistenciaAsync(Guid articuloId, CancellationToken ct) => throw new NotSupportedException();
        public Task<DisponibilidadArticuloSucursal> ConsultarDisponibilidadPorSucursalAsync(Guid sucursalId, Guid articuloId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlySet<Guid>> FiltrarSubAlmacenesEnRevisionAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, DisponibilidadArticuloSucursal>> ConsultarDisponibilidadPorSucursalLoteAsync(Guid sucursalId, IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
    }
}
