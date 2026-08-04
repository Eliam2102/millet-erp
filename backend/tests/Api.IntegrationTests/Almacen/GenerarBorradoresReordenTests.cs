using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Almacen.Application.Reorden;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Tests del orquestador del motor de reorden (ADR-0047 PR5.D):
/// <c>GenerarBorradoresReordenCommand</c>. Verifica el agrupamiento (1 RQ por almacén
/// con N líneas), los filtros (AutoRequisicion && Faltante>0), el mapeo N1/N2 (único
/// almacén de la sucursal; falla ruidoso si 2+ sin tumbar el ciclo) y el dedup
/// cross-ciclo (el Borrador creado baja el faltante vía "lo vivo").
///
/// <para>Los tests de orquestación usan un write-port que REGISTRA llamadas (no escribe
/// BD) y aseveran por el almacén sembrado (GUID único) → aislados de otros tests aunque
/// el command evalúe todas las configs activas. El test cross-ciclo usa el write-port
/// REAL para ejercitar el feedback de "lo vivo".</para>
/// </summary>
public class GenerarBorradoresReordenTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _base;

    public GenerarBorradoresReordenTests(WebApplicationFactory<Program> factory)
    {
        _base = factory;
    }

    [Fact]
    public async Task Agrupa_por_almacen_y_filtra_autoreq_y_faltante_cero()
    {
        var recorder = new RecordingCrearRqPort();
        await using var factory = _base.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IComprasCrearRqSistemaPort>();
                s.AddSingleton<IComprasCrearRqSistemaPort>(recorder);
            }));

        using var scope = factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var suc = Guid.NewGuid();
        var alm = Guid.NewGuid(); var sub = Guid.NewGuid(); var ub = Guid.NewGuid();
        var artA = Guid.NewGuid(); var artB = Guid.NewGuid(); var artC = Guid.NewGuid(); var artD = Guid.NewGuid();
        string K() => $"T{Guid.NewGuid():N}".Substring(0, 8);

        try
        {
            await SeedAlmacen(db, alm, suc, K());
            await SeedSubUbic(db, sub, alm, ub, K());
            // artA: max100, saldo30 → faltante 70; artB: max50, saldo10 → faltante 40 (ambos auto).
            await SeedConfig(db, Guid.NewGuid(), artA, nivel: 1, entidad: alm, max: 100, auto: true);
            await SeedSaldo(db, ub, sub, artA, 30);
            await SeedConfig(db, Guid.NewGuid(), artB, nivel: 1, entidad: alm, max: 50, auto: true);
            await SeedSaldo(db, ub, sub, artB, 10);
            // artC: auto=FALSE → no genera. artD: saldo20=max20 → faltante 0 → no genera.
            await SeedConfig(db, Guid.NewGuid(), artC, nivel: 1, entidad: alm, max: 100, auto: false);
            await SeedSaldo(db, ub, sub, artC, 0);
            await SeedConfig(db, Guid.NewGuid(), artD, nivel: 1, entidad: alm, max: 20, auto: true);
            await SeedSaldo(db, ub, sub, artD, 20);

            await mediator.Send(new GenerarBorradoresReordenCommand());

            // 1 sola RQ para mi almacén, con 2 líneas (artA, artB); artC/artD ausentes.
            var callsMias = recorder.Calls.Where(c => c.AlmacenDestinoId == alm).ToList();
            callsMias.Should().ContainSingle();
            var solicitud = callsMias[0];
            solicitud.SucursalId.Should().Be(suc);
            solicitud.Lineas.Select(l => l.ArticuloId).Should().BeEquivalentTo([artA, artB]);
            solicitud.Lineas.Single(l => l.ArticuloId == artA).Cantidad.Should().Be(70m);
            solicitud.Lineas.Single(l => l.ArticuloId == artB).Cantidad.Should().Be(40m);
        }
        finally
        {
            await Limpiar(db, new[] { artA, artB, artC, artD }, new[] { ub }, new[] { sub }, new[] { alm });
        }
    }

    [Fact]
    public async Task N1_unico_resuelve_y_N1_multi_falla_ruidoso_sin_tumbar_el_ciclo()
    {
        var recorder = new RecordingCrearRqPort();
        await using var factory = _base.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IComprasCrearRqSistemaPort>();
                s.AddSingleton<IComprasCrearRqSistemaPort>(recorder);
            }));

        using var scope = factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        // Sucursal A: 1 almacén (N1 resuelve). Sucursal B: 2 almacenes (N1 falla ruidoso).
        var sucA = Guid.NewGuid(); var almA = Guid.NewGuid(); var subA = Guid.NewGuid(); var ubA = Guid.NewGuid();
        var sucB = Guid.NewGuid(); var almB1 = Guid.NewGuid(); var subB1 = Guid.NewGuid(); var ubB1 = Guid.NewGuid();
        var almB2 = Guid.NewGuid();
        var artOk = Guid.NewGuid(); var artMulti = Guid.NewGuid();
        var configMulti = Guid.NewGuid();
        string K() => $"T{Guid.NewGuid():N}".Substring(0, 8);

        try
        {
            await SeedAlmacen(db, almA, sucA, K());
            await SeedSubUbic(db, subA, almA, ubA, K());
            await SeedAlmacen(db, almB1, sucB, K());
            await SeedSubUbic(db, subB1, almB1, ubB1, K());
            await SeedAlmacen(db, almB2, sucB, K());   // 2º almacén de sucB → N1 ambiguo

            // N1 ok (sucA, 1 almacén): max80, saldo10 → faltante 70.
            await SeedConfig(db, Guid.NewGuid(), artOk, nivel: 0, entidad: sucA, max: 80, auto: true);
            await SeedSaldo(db, ubA, subA, artOk, 10);
            // N1 multi (sucB, 2 almacenes): max50, saldo0 → faltante 50, pero falla el mapeo.
            await SeedConfig(db, configMulti, artMulti, nivel: 0, entidad: sucB, max: 50, auto: true);
            await SeedSaldo(db, ubB1, subB1, artMulti, 0);

            var resultado = await mediator.Send(new GenerarBorradoresReordenCommand());

            // N1 único resolvió: RQ para almA con artOk.
            var callsA = recorder.Calls.Where(c => c.AlmacenDestinoId == almA).ToList();
            callsA.Should().ContainSingle();
            callsA[0].Lineas.Should().ContainSingle(l => l.ArticuloId == artOk);
            // N1 multi NO generó para ningún almacén de sucB.
            recorder.Calls.Should().NotContain(c => c.AlmacenDestinoId == almB1 || c.AlmacenDestinoId == almB2);
            // ...y reportó el error ruidoso para esa config, sin tumbar el ciclo (artOk sí se creó).
            resultado.Errores.Should().Contain(e =>
                e.ConfiguracionId == configMulti && e.Codigo == "REORDEN_SUCURSAL_MULTI_ALMACEN");
        }
        finally
        {
            await Limpiar(db, new[] { artOk, artMulti }, new[] { ubA, ubB1 },
                new[] { subA, subB1 }, new[] { almA, almB1, almB2 });
        }
    }

    [Fact]
    public async Task Cross_ciclo_el_borrador_creado_suprime_la_regeneracion()
    {
        var empresa = Guid.NewGuid();
        var spId = Guid.NewGuid();
        var suc = Guid.NewGuid();
        var alm = Guid.NewGuid(); var sub = Guid.NewGuid(); var ub = Guid.NewGuid();
        var art = Guid.NewGuid();
        string K() => $"T{Guid.NewGuid():N}".Substring(0, 8);

        // Write-port REAL (para el feedback de "lo vivo"); fakes de resolución.
        await using var factory = _base.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IArticuloReadPort>();
                s.AddScoped<IArticuloReadPort>(_ => new FakeArticulo());
                s.RemoveAll<ISucursalReadPort>();
                s.AddScoped<ISucursalReadPort>(_ => new FakeSucursal("RSD"));
                s.RemoveAll<IUsuarioServicioReadPort>();
                s.AddScoped<IUsuarioServicioReadPort>(_ => new FakeSp(spId, empresa));
            }));

        using var scope = factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var comprasDb = scope.ServiceProvider.GetRequiredService<Millet.Compras.Infrastructure.ComprasDbContext>();

        try
        {
            await SeedAlmacen(db, alm, suc, K());
            await SeedSubUbic(db, sub, alm, ub, K());
            await SeedConfig(db, Guid.NewGuid(), art, nivel: 1, entidad: alm, max: 100, auto: true);
            await SeedSaldo(db, ub, sub, art, 30);   // faltante 70

            // Ciclo 1: crea la RQ (Borrador).
            await mediator.Send(new GenerarBorradoresReordenCommand());
            // Ciclo 2: el Borrador ya cuenta como "vivo" → faltante = 100−30−70 = 0 → no re-crea.
            await mediator.Send(new GenerarBorradoresReordenCommand());

            var rqs = await comprasDb.Requisiciones
                .IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.AlmacenDestinoId == alm && r.Origen == Millet.Compras.Domain.OrigenRequisicion.Sistema)
                .ToListAsync();
            rqs.Should().ContainSingle("el borrador creado en el ciclo 1 suprime la regeneración en el ciclo 2");
        }
        finally
        {
            await comprasDb.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM compras.requisicion_lineas WHERE requisicion_id IN (SELECT id FROM compras.requisiciones WHERE empresa_id = {empresa})");
            await comprasDb.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.requisiciones WHERE empresa_id = {empresa}");
            await comprasDb.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.folio_secuencias WHERE empresa_id = {empresa}");
            await Limpiar(db, new[] { art }, new[] { ub }, new[] { sub }, new[] { alm });
        }
    }

    // ── Seed helpers (raw SQL) ──

    private static async Task SeedAlmacen(AlmacenDbContext db, Guid id, Guid suc, string clave) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({id}, {clave}, 'RA', {suc}, 0, 0, NOW(), NOW())");

    private static async Task SeedSubUbic(AlmacenDbContext db, Guid subId, Guid alm, Guid ubic, string clave)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({subId}, {alm}, {clave}, 'Sub', 0, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({ubic}, {subId}, {clave}, 'Ubic', 0, false, 0, NOW(), NOW())");
    }

    private static async Task SeedSaldo(AlmacenDbContext db, Guid ubic, Guid sub, Guid art, decimal cantidad) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.saldos_inventario
                (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
            VALUES ({ubic}, {sub}, {art}, {cantidad}, 0, NOW())");

    private static async Task SeedConfig(
        AlmacenDbContext db, Guid id, Guid art, short nivel, Guid entidad, decimal max, bool auto) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.configuraciones_reorden
                (id, articulo_id, nivel, entidad_id, minimo, maximo, punto_reorden, auto_requisicion, objetivo, estatus, version, created_at, updated_at)
            VALUES ({id}, {art}, {nivel}, {entidad}, 0, {max}, 0, {auto}, 1, 0, 0, NOW(), NOW())");

    private static async Task Limpiar(
        AlmacenDbContext db, Guid[] articulos, Guid[] ubics, Guid[] subs, Guid[] alms)
    {
        foreach (var art in articulos)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.configuraciones_reorden WHERE articulo_id = {art}");
        foreach (var ub in ubics)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE ubicacion_id = {ub}");
        foreach (var ub in ubics)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id = {ub}");
        foreach (var sub in subs)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {sub}");
        foreach (var alm in alms)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {alm}");
    }

    private sealed class RecordingCrearRqPort : IComprasCrearRqSistemaPort
    {
        public List<CrearRqSistemaSolicitud> Calls { get; } = [];

        public Task<Guid> CrearBorradorSistemaAsync(CrearRqSistemaSolicitud solicitud, CancellationToken ct)
        {
            Calls.Add(solicitud);
            return Task.FromResult(Guid.CreateVersion7());
        }
    }

    private sealed class FakeArticulo : IArticuloReadPort
    {
        private static ArticuloLectura Art(Guid id) =>
            new(id, "ACC1", "Artículo", "PZA", null, null, EsActivo: true,
                PrecioReferenciaMonto: 15m, PrecioReferenciaMoneda: "MXN");
        public Task<ArticuloLectura?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ArticuloLectura?>(Art(id));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(ids.ToDictionary(x => x, Art));
    }

    private sealed class FakeSucursal(string clave) : ISucursalReadPort
    {
        public Task<SucursalLectura?> ObtenerAsync(Guid sucursalId, CancellationToken ct) =>
            Task.FromResult<SucursalLectura?>(new SucursalLectura(sucursalId, clave, "Suc", Guid.NewGuid(), EsActiva: true));
    }

    private sealed class FakeSp(Guid id, Guid empresaId) : IUsuarioServicioReadPort
    {
        public Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken ct) =>
            Task.FromResult<UsuarioServicioLectura?>(new UsuarioServicioLectura(id, empresaId, Activo: true));
    }
}
