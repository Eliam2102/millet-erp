using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;
using AlmacenAggregate = Millet.Almacen.Domain.Catalogo.Almacen;

namespace Millet.Almacen.Infrastructure.Seed;

/// <summary>
/// Hosted service que carga el seed inicial del catálogo de almacenes
/// y sub-almacenes (F1-PR3). Idempotente — si los Ids deterministas ya
/// existen, omite el INSERT.
///
/// <para>
/// <b>Scope:</b> dev/staging. En Production se autoexcluye — el catálogo
/// productivo se popula vía importer SAP cuando el cliente entregue el
/// export con la lista exhaustiva de sucursales/almacenes/sub-almacenes
/// (deferred, ver 02-plan-implementacion).
/// </para>
/// <para>
/// <b>Coordinación con CatalogosTestSeed:</b> las sucursales que estos
/// almacenes referencian se siembran por
/// <c>CatalogosTestSeedHostedService.TestSucursales</c> (ids
/// 00000005-0003-*). El orden de arranque importa: este servicio NO
/// crea sucursales, solo lee.
/// </para>
/// <para>
/// <b>A15 — MATERIAL_EN_REVISION:</b> cada almacén lleva un sub-almacén
/// con clave <c>MAT-REV</c> y tipo <see cref="TipoSubAlmacen.MaterialEnRevision"/>,
/// destino del sub-flujo 8.A (devolución interna por daño) — sigue
/// asentado a inventario hasta que Calidad decida destrucción o
/// reincorporación.
/// </para>
/// </summary>
public sealed class AlmacenSeedHostedService : IHostedService
{
    private const long SeedLockId = 6_672_000_002;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AlmacenSeedHostedService> _logger;

    public AlmacenSeedHostedService(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        ILogger<AlmacenSeedHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_environment.IsProduction())
        {
            _logger.LogInformation(
                "AlmacenSeed: environment=Production, seed omitido. " +
                "El catálogo se popula vía importer SAP (deferred).");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();

        var originContext = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(AlmacenSeedHostedService));
        using var bypass = empresaContext.Bypass();

        await PostgresAdvisoryLock.ExecuteAsync(
            db,
            SeedLockId,
            async ct =>
            {
                await SeedAlmacenesAsync(db, ct);
                await SeedSubAlmacenesAsync(db, ct);
                await SeedUbicacionesDefaultAsync(db, ct);
            },
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedAlmacenesAsync(AlmacenDbContext db, CancellationToken ct)
    {
        var claves = SeedAlmacenes.Select(a => a.Clave).ToArray();
        var existentes = await db.Almacenes.AsNoTracking()
            .Where(a => claves.Contains(a.Clave))
            .Select(a => a.Clave)
            .ToListAsync(ct);
        var faltantes = SeedAlmacenes.Where(a => !existentes.Contains(a.Clave)).ToList();
        if (faltantes.Count == 0)
        {
            _logger.LogDebug("AlmacenSeed almacenes: ya existían los {N} del seed.", SeedAlmacenes.Length);
            return;
        }

        db.Almacenes.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("AlmacenSeed almacenes: insertados {N} nuevos.", faltantes.Count);
    }

    private async Task SeedSubAlmacenesAsync(AlmacenDbContext db, CancellationToken ct)
    {
        // Unicidad de SubAlmacen: (almacen_id, clave). Filtramos por (id)
        // determinista igual — todos los seeds llevan id estable.
        var ids = SeedSubAlmacenes.Select(s => s.Id).ToArray();
        var existentes = await db.SubAlmacenes.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(ct);
        var faltantes = SeedSubAlmacenes.Where(s => !existentes.Contains(s.Id)).ToList();
        if (faltantes.Count == 0)
        {
            _logger.LogDebug("AlmacenSeed sub-almacenes: ya existían los {N} del seed.", SeedSubAlmacenes.Length);
            return;
        }

        db.SubAlmacenes.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("AlmacenSeed sub-almacenes: insertados {N} nuevos.", faltantes.Count);
    }

    private async Task SeedUbicacionesDefaultAsync(AlmacenDbContext db, CancellationToken ct)
    {
        var ids = SeedUbicacionesDefault.Select(u => u.Id).ToArray();
        var existentes = await db.Ubicaciones.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct);
        var faltantes = SeedUbicacionesDefault.Where(u => !existentes.Contains(u.Id)).ToList();
        if (faltantes.Count == 0)
        {
            _logger.LogDebug(
                "AlmacenSeed ubicaciones default: ya existían las {N} del seed.",
                SeedUbicacionesDefault.Length);
            return;
        }

        db.Ubicaciones.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "AlmacenSeed ubicaciones default: insertadas {N} nuevas.",
            faltantes.Count);
    }

    // ─── Datos del seed ────────────────────────────────────────────────────────
    //
    // IDs deterministas:
    //   00000005-0005-* (almacenes — namespace heredado del seed previo
    //                    en compartido.almacenes para preservar
    //                    continuidad con dev/UAT existentes).
    //   00000008-0001-* (sub-almacenes — namespace del módulo Almacén,
    //                    00000008-* del 00000008-* del PermisosCanonicos).
    //
    // El seed cubre 4 almacenes × 3 sub-almacenes = 12 filas:
    //   - INSUMOS  (variante A — recepción con factura)
    //   - MAT-DIR  (variante B — recepción con packing list)
    //   - MAT-REV  (A15 — destino de devolución interna por daño)

    public static readonly Guid AlmMidGeneralId = Guid.Parse("00000005-0005-0000-0000-000000000001");
    public static readonly Guid AlmMidMpId = Guid.Parse("00000005-0005-0000-0000-000000000002");
    public static readonly Guid AlmMtyGeneralId = Guid.Parse("00000005-0005-0000-0000-000000000003");
    public static readonly Guid AlmQroGeneralId = Guid.Parse("00000005-0005-0000-0000-000000000004");

    public static readonly AlmacenAggregate[] SeedAlmacenes =
    [
        new AlmacenAggregate(
            id: AlmMidGeneralId,
            clave: "ALM-MID-G",
            nombre: "Almacén General MID",
            sucursalId: Guid.Parse("00000005-0003-0000-0000-000000000001")),
        new AlmacenAggregate(
            id: AlmMidMpId,
            clave: "ALM-MID-MP",
            nombre: "Almacén Materia Prima MID",
            sucursalId: Guid.Parse("00000005-0003-0000-0000-000000000001")),
        new AlmacenAggregate(
            id: AlmMtyGeneralId,
            clave: "ALM-MTY-G",
            nombre: "Almacén General MTY",
            sucursalId: Guid.Parse("00000005-0003-0000-0000-000000000002")),
        new AlmacenAggregate(
            id: AlmQroGeneralId,
            clave: "ALM-QRO-G",
            nombre: "Almacén General QRO",
            sucursalId: Guid.Parse("00000005-0003-0000-0000-000000000003")),
    ];

    public static readonly SubAlmacen[] SeedSubAlmacenes =
    [
        // ─── ALM-MID-G ───
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-000000000001"),
            almacenId: AlmMidGeneralId,
            clave: "INSUMOS",
            nombre: "Insumos y refacciones",
            tipo: TipoSubAlmacen.Insumos),
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-000000000002"),
            almacenId: AlmMidGeneralId,
            clave: "MAT-DIR",
            nombre: "Materiales directos no-vidrio",
            tipo: TipoSubAlmacen.MaterialesDirectos),
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-000000000003"),
            almacenId: AlmMidGeneralId,
            clave: "MAT-REV",
            nombre: "Material en revisión (Calidad)",
            tipo: TipoSubAlmacen.MaterialEnRevision),

        // ─── ALM-MID-MP ───
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-000000000004"),
            almacenId: AlmMidMpId,
            clave: "INSUMOS",
            nombre: "Insumos materia prima MID",
            tipo: TipoSubAlmacen.Insumos),
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-000000000005"),
            almacenId: AlmMidMpId,
            clave: "MAT-DIR",
            nombre: "Materiales directos MID-MP",
            tipo: TipoSubAlmacen.MaterialesDirectos),
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-000000000006"),
            almacenId: AlmMidMpId,
            clave: "MAT-REV",
            nombre: "Material en revisión MID-MP",
            tipo: TipoSubAlmacen.MaterialEnRevision),

        // ─── ALM-MTY-G ───
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-000000000007"),
            almacenId: AlmMtyGeneralId,
            clave: "INSUMOS",
            nombre: "Insumos y refacciones MTY",
            tipo: TipoSubAlmacen.Insumos),
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-000000000008"),
            almacenId: AlmMtyGeneralId,
            clave: "MAT-DIR",
            nombre: "Materiales directos MTY",
            tipo: TipoSubAlmacen.MaterialesDirectos),
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-000000000009"),
            almacenId: AlmMtyGeneralId,
            clave: "MAT-REV",
            nombre: "Material en revisión MTY",
            tipo: TipoSubAlmacen.MaterialEnRevision),

        // ─── ALM-QRO-G ───
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-00000000000a"),
            almacenId: AlmQroGeneralId,
            clave: "INSUMOS",
            nombre: "Insumos y refacciones QRO",
            tipo: TipoSubAlmacen.Insumos),
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-00000000000b"),
            almacenId: AlmQroGeneralId,
            clave: "MAT-DIR",
            nombre: "Materiales directos QRO",
            tipo: TipoSubAlmacen.MaterialesDirectos),
        new SubAlmacen(
            id: Guid.Parse("00000008-0001-0000-0000-00000000000c"),
            almacenId: AlmQroGeneralId,
            clave: "MAT-REV",
            nombre: "Material en revisión QRO",
            tipo: TipoSubAlmacen.MaterialEnRevision),
    ];

    public static readonly Ubicacion[] SeedUbicacionesDefault =
        SeedSubAlmacenes
            .Select((subAlmacen, index) => new Ubicacion(
                id: Guid.Parse($"00000008-0002-0000-0000-{index + 1:X12}"),
                subAlmacenId: subAlmacen.Id,
                clave: "ÚNICA",
                nombre: "Ubicación única (default del sub-almacén)",
                esDefault: true))
            .ToArray();
}
