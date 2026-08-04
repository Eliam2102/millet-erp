using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compras.Application.MigracionEntrega;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Workers;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Migracion;

/// <summary>
/// Tests de integración del job de migración de históricas (ADR-0043 PR #4)
/// contra Postgres real. Siembra escenarios (Cerrada exacta-parcial/-completa/
/// -capada/-ambigua, EnSurtido backfill, terminales excluidos), corre el job
/// acotado a esas RQ (override) en DRY-RUN y luego REAL, y verifica los focos:
/// separación exacto/ambiguo, dry-run que no escribe, cap al techo, exclusiones
/// de estado (#400) e idempotencia.
///
/// <para>Las salidas se siembran vía SQL crudo: movimiento en Borrador →
/// líneas (el trigger de saldo es no-op si el movimiento no está Registrado) →
/// UPDATE a Registrado (el trigger no está en la tabla de movimientos). Así no
/// se necesita saldo ni deshabilitar triggers globalmente (BD compartida).</para>
/// </summary>
public class MigracionEntregaHistoricaJobTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private static readonly Guid Articulo = Guid.Parse("00000000-0000-0000-0000-000000000bbb"); // sin stock → EnSurtido

    private readonly StubsWebApplicationFactory _factory;

    public MigracionEntregaHistoricaJobTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Migracion_DryRunNoEscribe_Real_Reclasifica_RespetaExclusiones_Idempotente()
    {
        var client = await CreateSuperAdminClientAsync();

        // ── Seed de 7 escenarios ────────────────────────────────────────────
        // 1) MOVER: Cerrada, 1 línea(10), entregado 4 con LineaRqId → exacta parcial.
        var rqMover = await CrearRqEnSurtidoAsync(client, (Articulo, 10m));
        var lMover = (await GetLineasAsync(rqMover))[0];
        var empresaId = await GetEmpresaIdAsync(rqMover);
        await SeedSalidaAsync(rqMover, empresaId, Articulo, 4m, lineaRqId: lMover.Id);
        await SetEstadoAsync(rqMover, EstadoRequisicion.Cerrada);

        // 2) QUEDA: Cerrada, 1 línea(10), entregado 10 con LineaRqId → exacta completa.
        var rqQueda = await CrearRqEnSurtidoAsync(client, (Articulo, 10m));
        var lQueda = (await GetLineasAsync(rqQueda))[0];
        await SeedSalidaAsync(rqQueda, empresaId, Articulo, 10m, lineaRqId: lQueda.Id);
        await SetEstadoAsync(rqQueda, EstadoRequisicion.Cerrada);

        // 3) CAPADA: Cerrada, 1 línea(10), entregado 15 (sobre-entrega) → capa a 10.
        var rqCapada = await CrearRqEnSurtidoAsync(client, (Articulo, 10m));
        var lCapada = (await GetLineasAsync(rqCapada))[0];
        await SeedSalidaAsync(rqCapada, empresaId, Articulo, 15m, lineaRqId: lCapada.Id);
        await SetEstadoAsync(rqCapada, EstadoRequisicion.Cerrada);

        // 4) AMBIGUA: Cerrada, 2 líneas mismo artículo, entregado 7 SIN LineaRqId.
        var rqAmbigua = await CrearRqEnSurtidoAsync(client, (Articulo, 10m), (Articulo, 5m));
        var lAmbiguas = await GetLineasAsync(rqAmbigua);
        await SeedSalidaAsync(rqAmbigua, empresaId, Articulo, 7m, lineaRqId: null);
        await SetEstadoAsync(rqAmbigua, EstadoRequisicion.Cerrada);

        // 5) BACKFILL EnSurtido: queda EnSurtido, entregado 6 SIN LineaRqId (pre-#399, artículo único).
        var rqBackfill = await CrearRqEnSurtidoAsync(client, (Articulo, 10m));
        var lBackfill = (await GetLineasAsync(rqBackfill))[0];
        await SeedSalidaAsync(rqBackfill, empresaId, Articulo, 6m, lineaRqId: null);

        // 6) EXCLUIDO: CerradaSinSurtir (#400) — el job no la toca.
        var rqSinSurtir = await CrearRqEnSurtidoAsync(client, (Articulo, 10m));
        await SetEstadoAsync(rqSinSurtir, EstadoRequisicion.CerradaSinSurtir);

        // 7) EXCLUIDO: Cancelada — el job no la toca.
        var rqCancelada = await CrearRqEnSurtidoAsync(client, (Articulo, 10m));
        await SetEstadoAsync(rqCancelada, EstadoRequisicion.Cancelada);

        var ids = new[] { rqMover, rqQueda, rqCapada, rqAmbigua, rqBackfill, rqSinSurtir, rqCancelada };
        var job = BuildJob();

        // ── DRY-RUN: reporta pero NO escribe ────────────────────────────────
        var dry = await job.EjecutarAsync(dryRun: true, rqIdsOverride: ids, CancellationToken.None);

        Assert.Equal(1, dry.Movidas);
        Assert.Equal(2, dry.QuedanCerradas);       // queda + capada
        Assert.Equal(1, dry.BackfillEnSurtido);
        Assert.Single(dry.Ambiguas);
        Assert.Equal(rqAmbigua, dry.Ambiguas[0].RqId);

        // Nada escrito: estados intactos y cant_entregada en 0.
        Assert.Equal(EstadoRequisicion.Cerrada, await GetEstadoAsync(rqMover));
        Assert.Equal(EstadoRequisicion.EnSurtido, await GetEstadoAsync(rqBackfill));
        Assert.Equal(0m, await GetCantEntregadaAsync(lMover.Id));
        Assert.Equal(0m, await GetCantEntregadaAsync(lQueda.Id));
        Assert.Equal(0m, await GetCantEntregadaAsync(lBackfill.Id));

        // ── REAL: aplica ────────────────────────────────────────────────────
        var real = await job.EjecutarAsync(dryRun: false, rqIdsOverride: ids, CancellationToken.None);

        Assert.Equal(1, real.Movidas);
        Assert.Equal(2, real.QuedanCerradas);
        Assert.Equal(1, real.BackfillEnSurtido);
        Assert.Single(real.Ambiguas);

        // 1) MOVER → EnSurtido, entregado 4.
        Assert.Equal(EstadoRequisicion.EnSurtido, await GetEstadoAsync(rqMover));
        Assert.Equal(4m, await GetCantEntregadaAsync(lMover.Id));
        // 2) QUEDA Cerrada, entregado 10.
        Assert.Equal(EstadoRequisicion.Cerrada, await GetEstadoAsync(rqQueda));
        Assert.Equal(10m, await GetCantEntregadaAsync(lQueda.Id));
        // 3) CAPADA: queda Cerrada (completa), entregado capado a 10 (no 15).
        Assert.Equal(EstadoRequisicion.Cerrada, await GetEstadoAsync(rqCapada));
        Assert.Equal(10m, await GetCantEntregadaAsync(lCapada.Id));
        // 4) AMBIGUA: intacta (Cerrada, ambas líneas en 0).
        Assert.Equal(EstadoRequisicion.Cerrada, await GetEstadoAsync(rqAmbigua));
        Assert.Equal(0m, await GetCantEntregadaAsync(lAmbiguas[0].Id));
        Assert.Equal(0m, await GetCantEntregadaAsync(lAmbiguas[1].Id));
        // 5) BACKFILL: EnSurtido (sin cambio), entregado 6.
        Assert.Equal(EstadoRequisicion.EnSurtido, await GetEstadoAsync(rqBackfill));
        Assert.Equal(6m, await GetCantEntregadaAsync(lBackfill.Id));
        // 6) y 7) EXCLUIDOS: estados intactos.
        Assert.Equal(EstadoRequisicion.CerradaSinSurtir, await GetEstadoAsync(rqSinSurtir));
        Assert.Equal(EstadoRequisicion.Cancelada, await GetEstadoAsync(rqCancelada));

        // ── IDEMPOTENCIA: 2ª corrida real no re-mueve ───────────────────────
        var otra = await job.EjecutarAsync(dryRun: false, rqIdsOverride: ids, CancellationToken.None);
        Assert.Equal(0, otra.Movidas);                       // rqMover ya está EnSurtido
        Assert.Equal(2, otra.BackfillEnSurtido);             // rqMover + rqBackfill como backfill
        Assert.Single(otra.Ambiguas);
        Assert.Equal(EstadoRequisicion.EnSurtido, await GetEstadoAsync(rqMover));
        Assert.Equal(4m, await GetCantEntregadaAsync(lMover.Id)); // SET estable
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private MigracionEntregaHistoricaJob BuildJob() => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new MigracionEntregaHistoricaOptions()),
        _factory.Services.GetRequiredService<ILogger<MigracionEntregaHistoricaJob>>());

    private async Task SeedSalidaAsync(
        Guid rqId, Guid empresaId, Guid articuloId, decimal cantidad, Guid? lineaRqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var movId = Guid.CreateVersion7();
        var lineId = Guid.CreateVersion7();
        // FK movimientos.sub_almacen_id → almacen.sub_almacenes: usar uno
        // sembrado (AlmacenSeedHostedService) que tenga ubicación es_default
        // (ÚNICA). PR6a: lineas_movimiento.ubicacion_id es NOT NULL, así que la
        // línea necesita un bin real — se usa esa ÚNICA.
        var subAlmacen = await db.Database
            .SqlQueryRaw<Guid>(
                "SELECT s.id AS \"Value\" FROM almacen.sub_almacenes s " +
                "JOIN almacen.ubicaciones u ON u.sub_almacen_id = s.id AND u.es_default LIMIT 1")
            .FirstAsync();
        var ubicacion = await db.Database
            .SqlQueryRaw<Guid>(
                "SELECT id AS \"Value\" FROM almacen.ubicaciones WHERE sub_almacen_id = {0} AND es_default LIMIT 1",
                subAlmacen)
            .FirstAsync();
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow);

        // 1) movimiento en Borrador (estado 0): al insertar líneas el trigger
        //    de saldo es no-op (solo actúa en Registrado) → no requiere saldo.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO almacen.movimientos_inventario
 (id, tipo, estado, fecha_movimiento, fecha_registro, empresa_id, rq_id, pendiente_regularizacion, version, created_at, updated_at)
 VALUES ({movId}, 1, 0, {fecha}, now(), {empresaId}, {rqId}, false, 0, now(), now())");

        // 2) línea de salida (tipo SalidaConsumo).
        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO almacen.lineas_movimiento
 (id, movimiento_id, posicion, articulo_id, cantidad, unidad_medida, costo_unitario_mxn, monto_total_mxn, moneda_original, linea_rq_id, ubicacion_id, version, created_at, updated_at)
 VALUES ({lineId}, {movId}, 1, {articuloId}, {cantidad}, 'PZA', 0, 0, 'MXN', {lineaRqId}, {ubicacion}, 0, now(), now())");

        // 3) movimiento → Registrado (sin trigger en la tabla de movimientos).
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE almacen.movimientos_inventario SET estado = 2 WHERE id = {movId}");
    }

    private async Task SetEstadoAsync(Guid rqId, EstadoRequisicion estado)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE compras.requisiciones SET estado = {(short)estado} WHERE id = {rqId}");
    }

    private async Task<EstadoRequisicion> GetEstadoAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        return await db.Requisiciones.AsNoTracking()
            .Where(r => r.Id == rqId).Select(r => r.Estado).FirstAsync();
    }

    private async Task<decimal> GetCantEntregadaAsync(Guid lineaId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        return await db.LineaRequisiciones.AsNoTracking()
            .Where(l => l.Id == lineaId).Select(l => l.CantidadEntregada).FirstAsync();
    }

    private async Task<List<(Guid Id, Guid Art, decimal Cant)>> GetLineasAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        var rows = await db.LineaRequisiciones.AsNoTracking()
            .Where(l => l.RequisicionId == rqId)
            .OrderBy(l => l.Posicion)
            .Select(l => new { l.Id, l.ArticuloId, l.Cantidad })
            .ToListAsync();
        return rows.Select(r => (r.Id, r.ArticuloId, r.Cantidad)).ToList();
    }

    private async Task<Guid> GetEmpresaIdAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();
        return await db.Requisiciones.AsNoTracking()
            .Where(r => r.Id == rqId).Select(r => r.EmpresaId).FirstAsync();
    }

    private static async Task<Guid> CrearRqEnSurtidoAsync(
        HttpClient client, params (Guid Art, decimal Cant)[] lineas)
    {
        var rqBody = TestComprasFixtures.BuildCrearRqValidBody(descripcion: "Test ADR-0043 #4 migración");
        var crear = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", rqBody);
        crear.EnsureSuccessStatusCode();
        var rqId = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();

        foreach (var (art, cant) in lineas)
        {
            var lineaBody = new
            {
                ArticuloId = art,
                Cantidad = cant,
                UnidadMedida = "PZA",
                PrecioEstimadoMonto = 15m,
                PrecioEstimadoMoneda = "MXN",
                CuentaContableId = (Guid?)null,
                CentroCostoId = (Guid?)TestComprasFixtures.CentroCostoMaquinaSeed,
                Proyecto = (string?)null,
                FechaRequerida = (DateOnly?)null,
                Notas = (string?)null,
            };
            var linea = await client.PostAsJsonAsync(
                $"/api/v1/compras/requisiciones/{rqId}/lineas", lineaBody);
            linea.EnsureSuccessStatusCode();
        }

        var transmit = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
        transmit.EnsureSuccessStatusCode();

        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();

        return rqId;
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> FakeLoginAsync(HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.Clone();
    }
}
