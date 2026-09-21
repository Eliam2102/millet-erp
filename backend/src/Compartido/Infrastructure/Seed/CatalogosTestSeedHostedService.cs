using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

// F1-PR3: el seed de almacenes y sub-almacenes se movió a
// `Millet.Almacen.Infrastructure.Seed.AlmacenSeedHostedService` — el
// catálogo es propiedad del módulo Almacén. Este hosted service ya no
// los gestiona.

namespace Millet.Compartido.Infrastructure.Seed;

/// <summary>
/// Hosted service que carga datos de prueba en
/// <c>compartido.proveedores</c> y <c>compartido.articulos</c> al
/// arranque (F7-PR1, MVP).
///
/// <para>
/// <b>Solo en non-Production</b>: si el <see cref="IHostEnvironment"/>
/// es Production, el servicio se autoexcluye. En Production las
/// tablas arrancan vacías y se populan vía importer SAP cuando el
/// cliente entregue el export (F7-PR2 deferred).
/// </para>
/// <para>
/// <b>Idempotente</b>: si ya hay filas con clave del seed, omite la
/// inserción. Permite re-arranques sin duplicar.
/// </para>
/// <para>
/// Bypass del filtro de empresa: las tablas son cross-empresa (no
/// implementan <see cref="IPerteneceAEmpresa"/>), pero el interceptor
/// requiere bypass cuando no hay HttpContext.
/// </para>
/// </summary>
public sealed class CatalogosTestSeedHostedService : IHostedService
{
    private const long SeedLockId = 6_672_000_001;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CatalogosTestSeedHostedService> _logger;

    public CatalogosTestSeedHostedService(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        ILogger<CatalogosTestSeedHostedService> logger)
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
                "CatalogosTestSeed: environment=Production, seed test data omitido. Las tablas se populan vía importer (F7-PR2 deferred).");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();

        var originContext = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(CatalogosTestSeedHostedService));
        using var bypass = empresaContext.Bypass();

        await PostgresAdvisoryLock.ExecuteAsync(
            db,
            SeedLockId,
            async ct =>
            {
                await SeedProveedoresAsync(db, ct);
                await SeedArticulosAsync(db, ct);
                await SeedSucursalesAsync(db, ct);
                await SeedDepartamentosAsync(db, ct);
                await SeedAsignacionesSucursalDepartamentoAsync(db, ct);
                // F1-PR3: almacenes y sub-almacenes ahora se siembran por
                // Millet.Almacen.Infrastructure.Seed.AlmacenSeedHostedService.
            },
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedProveedoresAsync(CompartidoDbContext db, CancellationToken ct)
    {
        var seedClaves = TestProveedores.Select(p => p.Clave).ToArray();
        var existentes = await db.Proveedores
            .AsNoTracking()
            .Where(p => seedClaves.Contains(p.Clave))
            .Select(p => p.Clave)
            .ToListAsync(ct);

        var faltantes = TestProveedores.Where(p => !existentes.Contains(p.Clave)).ToList();
        if (faltantes.Count == 0)
        {
            _logger.LogDebug("CatalogosTestSeed proveedores: ya existían los {N} del seed.", TestProveedores.Length);
            return;
        }

        db.Proveedores.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("CatalogosTestSeed proveedores: insertados {N} nuevos.", faltantes.Count);
    }

    private async Task SeedArticulosAsync(CompartidoDbContext db, CancellationToken ct)
    {
        var seedClaves = TestArticulos.Select(a => a.Clave).ToArray();
        var existentes = await db.Articulos
            .AsNoTracking()
            .Where(a => seedClaves.Contains(a.Clave))
            .Select(a => a.Clave)
            .ToListAsync(ct);

        var faltantes = TestArticulos.Where(a => !existentes.Contains(a.Clave)).ToList();
        if (faltantes.Count == 0)
        {
            _logger.LogDebug("CatalogosTestSeed articulos: ya existían los {N} del seed.", TestArticulos.Length);
            return;
        }

        db.Articulos.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("CatalogosTestSeed articulos: insertados {N} nuevos.", faltantes.Count);
    }

    // --- B.1: catálogos organizacionales ---

    private async Task SeedSucursalesAsync(CompartidoDbContext db, CancellationToken ct)
    {
        var claves = TestSucursales.Select(s => s.Clave).ToArray();
        var existentes = await db.Sucursales.AsNoTracking()
            .Where(s => claves.Contains(s.Clave))
            .Select(s => s.Clave)
            .ToListAsync(ct);
        var faltantes = TestSucursales.Where(s => !existentes.Contains(s.Clave)).ToList();
        if (faltantes.Count == 0) return;
        db.Sucursales.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("CatalogosTestSeed sucursales: insertados {N} nuevos.", faltantes.Count);
    }

    private async Task SeedDepartamentosAsync(CompartidoDbContext db, CancellationToken ct)
    {
        var claves = TestDepartamentos.Select(d => d.Clave).ToArray();
        var existentes = await db.Departamentos.AsNoTracking()
            .Where(d => claves.Contains(d.Clave))
            .Select(d => d.Clave)
            .ToListAsync(ct);
        var faltantes = TestDepartamentos.Where(d => !existentes.Contains(d.Clave)).ToList();
        if (faltantes.Count == 0) return;
        db.Departamentos.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("CatalogosTestSeed departamentos: insertados {N} nuevos.", faltantes.Count);
    }

    /// <summary>
    /// PR-A1: combinatoria completa Sucursal × Departamento — cada depto
    /// del seed opera en cada sucursal del seed, todos en estado Activo.
    /// Mantiene retro-compatibilidad con tests existentes que asumen
    /// disponibilidad global. Cuando la realidad operativa requiera
    /// asimetrías (Sistemas en Conkal pero no en Cancún), el panel de
    /// admin permite desactivar combinaciones individuales — el seed
    /// no se toca.
    ///
    /// <para>GUIDs deterministas: <c>00000005-0007-0000-{sucIdx:0000}-{deptoIdx:000000000000}</c>
    /// con índices 1-based para idempotencia entre arranques.</para>
    /// </summary>
    private async Task SeedAsignacionesSucursalDepartamentoAsync(
        CompartidoDbContext db, CancellationToken ct)
    {
        var pares = (
            from s in TestSucursales.Select((suc, i) => (Sucursal: suc, Idx: i + 1))
            from d in TestDepartamentos.Select((dep, i) => (Departamento: dep, Idx: i + 1))
            select new
            {
                Id = Guid.Parse($"00000005-0007-0000-{s.Idx:X4}-{d.Idx:X12}"),
                SucursalId = s.Sucursal.Id,
                DepartamentoId = d.Departamento.Id,
            }).ToList();

        var existentes = await db.SucursalDepartamentos.AsNoTracking()
            .Select(a => new { a.SucursalId, a.DepartamentoId })
            .ToListAsync(ct);
        var existentesSet = existentes
            .Select(x => (x.SucursalId, x.DepartamentoId))
            .ToHashSet();

        var faltantes = pares
            .Where(p => !existentesSet.Contains((p.SucursalId, p.DepartamentoId)))
            .Select(p => new SucursalDepartamento(p.Id, p.SucursalId, p.DepartamentoId))
            .ToList();

        if (faltantes.Count == 0) return;
        db.SucursalDepartamentos.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "CatalogosTestSeed sucursal_departamentos: insertados {N} nuevos.",
            faltantes.Count);
    }

    // F1-PR3: SeedAlmacenesAsync se movió a
    // Millet.Almacen.Infrastructure.Seed.AlmacenSeedHostedService.

    /// <summary>
    /// 5 proveedores de prueba con Guids deterministas (00000005-0001-...).
    /// Cubren los principales casos: persona moral nacional, persona
    /// física, mayorista, internacional (USD), inactivo (para tests
    /// negativos).
    /// </summary>
    public static readonly Proveedor[] TestProveedores =
    [
        new Proveedor(
            id: Guid.Parse("00000005-0001-0000-0000-000000000001"),
            clave: "PROV-EST",
            razonSocial: "Suministros Estándar SA de CV",
            rfc: "SES010101AAA",
            tipoPersona: TipoPersonaProveedor.Moral,
            condicionesPagoDias: 30),
        new Proveedor(
            id: Guid.Parse("00000005-0001-0000-0000-000000000002"),
            clave: "PROV-SVC",
            razonSocial: "Consultoría Técnica del Norte SC",
            rfc: "CTN020202BBB",
            tipoPersona: TipoPersonaProveedor.Moral,
            condicionesPagoDias: 60),
        new Proveedor(
            id: Guid.Parse("00000005-0001-0000-0000-000000000003"),
            clave: "PROV-MAY",
            razonSocial: "Distribuidora Mayorista de Insumos SA",
            rfc: "DMI030303CCC",
            tipoPersona: TipoPersonaProveedor.Moral,
            condicionesPagoDias: 45,
            nombreComercial: "DiMa Insumos"),
        new Proveedor(
            id: Guid.Parse("00000005-0001-0000-0000-000000000004"),
            clave: "PROV-INT",
            razonSocial: "Global Supplies Corp",
            rfc: "GSC040404DDD",
            tipoPersona: TipoPersonaProveedor.Moral,
            condicionesPagoDias: 90,
            monedaPreferidaId: Guid.Parse("00000001-0000-0000-0000-000000000002")), // USD
        new Proveedor(
            id: Guid.Parse("00000005-0001-0000-0000-000000000005"),
            clave: "PROV-INA",
            razonSocial: "Proveedor Inactivo SA",
            rfc: "PIN050505EEE",
            tipoPersona: TipoPersonaProveedor.Moral,
            estatus: EstatusCatalogo.Inactivo),
    ];

    /// <summary>
    /// 8 artículos de prueba (00000005-0002-...). Cubren las 4
    /// naturalezas (2 cada una) + diferentes UMs + diferentes estatus.
    /// </summary>
    public static readonly Articulo[] TestArticulos =
    [
        new Articulo(
            id: Guid.Parse("00000005-0002-0000-0000-000000000001"),
            clave: "ART-EST-01",
            nombre: "Papelería de oficina genérica",
            unidadMedidaDefault: "PZA",
            naturaleza: Naturaleza.Estandar,
            categoria: "Papelería"),
        new Articulo(
            id: Guid.Parse("00000005-0002-0000-0000-000000000002"),
            clave: "ART-EST-02",
            nombre: "Material de limpieza",
            unidadMedidaDefault: "L",
            naturaleza: Naturaleza.Estandar,
            categoria: "Limpieza"),
        new Articulo(
            id: Guid.Parse("00000005-0002-0000-0000-000000000003"),
            clave: "ART-SVC-01",
            nombre: "Mantenimiento preventivo de equipo",
            unidadMedidaDefault: "HR",
            naturaleza: Naturaleza.Servicio,
            categoria: "Servicios técnicos"),
        new Articulo(
            id: Guid.Parse("00000005-0002-0000-0000-000000000004"),
            clave: "ART-SVC-02",
            nombre: "Consultoría especializada",
            unidadMedidaDefault: "HR",
            naturaleza: Naturaleza.Servicio,
            categoria: "Servicios profesionales"),
        new Articulo(
            id: Guid.Parse("00000005-0002-0000-0000-000000000005"),
            clave: "ART-CRT-01",
            nombre: "Refacciones críticas para línea de producción",
            unidadMedidaDefault: "PZA",
            naturaleza: Naturaleza.Critico,
            categoria: "Refacciones",
            precioReferenciaMonto: 15000m,
            precioReferenciaMoneda: "MXN"),
        new Articulo(
            id: Guid.Parse("00000005-0002-0000-0000-000000000006"),
            clave: "ART-CRT-02",
            nombre: "Reactivos químicos críticos",
            unidadMedidaDefault: "KG",
            naturaleza: Naturaleza.Critico,
            categoria: "Químicos"),
        new Articulo(
            id: Guid.Parse("00000005-0002-0000-0000-000000000007"),
            clave: "ART-RGO-01",
            nombre: "Sustancias peligrosas controladas",
            unidadMedidaDefault: "L",
            naturaleza: Naturaleza.Riesgo,
            categoria: "Materiales peligrosos"),
        new Articulo(
            id: Guid.Parse("00000005-0002-0000-0000-000000000008"),
            clave: "ART-RGO-02",
            nombre: "Equipo eléctrico de alto voltaje",
            unidadMedidaDefault: "PZA",
            naturaleza: Naturaleza.Riesgo,
            categoria: "Equipo eléctrico",
            estatus: EstatusCatalogo.EnRevision),
        // GUIDs especiales que el stub de stock (StubsWebApplicationFactory)
        // usa para pruebas de bifurcación stock-aware. Permanecen en el
        // seed para que la validación cross-table del articuloId pase y
        // los tests de F4-PR1+ sigan funcionando sin cambios.
        new Articulo(
            id: Guid.Parse("00000000-0000-0000-0000-000000000aaa"),
            clave: "ART-TEST-AAA",
            nombre: "Articulo de prueba — stock medio (50%)",
            unidadMedidaDefault: "PZA",
            naturaleza: Naturaleza.Estandar,
            categoria: "Test"),
        new Articulo(
            id: Guid.Parse("00000000-0000-0000-0000-000000000bbb"),
            clave: "ART-TEST-BBB",
            nombre: "Articulo de prueba — sin stock (0%)",
            unidadMedidaDefault: "PZA",
            naturaleza: Naturaleza.Estandar,
            categoria: "Test"),
    ];

    // --- B.1: catálogos organizacionales ---

    /// <summary>
    /// 3 sucursales de prueba (00000005-0003-...). Geográficas tipo
    /// Millet: México Centro, Monterrey, Querétaro.
    /// </summary>
    public static readonly Sucursal[] TestSucursales =
    [
        new Sucursal(
            id: Guid.Parse("00000005-0003-0000-0000-000000000001"),
            clave: "MID",
            nombre: "Planta México Centro"),
        new Sucursal(
            id: Guid.Parse("00000005-0003-0000-0000-000000000002"),
            clave: "MTY",
            nombre: "Planta Monterrey"),
        new Sucursal(
            id: Guid.Parse("00000005-0003-0000-0000-000000000003"),
            clave: "QRO",
            nombre: "Planta Querétaro"),
    ];

    /// <summary>
    /// 5 departamentos funcionales seedeados (00000005-0004-...).
    /// COMPRAS = el "depto primario" del dev-superadmin (asignado en
    /// el bootstrap para que /api/auth/me retorne un departamentoId
    /// no-null en dev/UAT).
    /// </summary>
    public static readonly Departamento[] TestDepartamentos =
    [
        new Departamento(
            id: Guid.Parse("00000005-0004-0000-0000-000000000001"),
            clave: "COMPRAS",
            nombre: "Compras y Adquisiciones"),
        new Departamento(
            id: Guid.Parse("00000005-0004-0000-0000-000000000002"),
            clave: "ALMACEN",
            nombre: "Almacén No-Producción"),
        new Departamento(
            id: Guid.Parse("00000005-0004-0000-0000-000000000003"),
            clave: "MTTO",
            nombre: "Mantenimiento"),
        new Departamento(
            id: Guid.Parse("00000005-0004-0000-0000-000000000004"),
            clave: "ING",
            nombre: "Ingeniería"),
        new Departamento(
            id: Guid.Parse("00000005-0004-0000-0000-000000000005"),
            clave: "CAL",
            nombre: "Calidad"),
    ];

    // F1-PR3: las constantes TestAlmacenes se movieron a
    // Millet.Almacen.Infrastructure.Seed.AlmacenSeedHostedService.SeedAlmacenes
    // (con ids deterministas preservados — 00000005-0005-*).

    /// <summary>
    /// Id deterministic del departamento COMPRAS, expuesto para que el
    /// bootstrap de superadmin (Identidad) le asigne este departamento
    /// como "primario" en dev/UAT.
    /// </summary>
    public static readonly Guid DepartamentoComprasId =
        Guid.Parse("00000005-0004-0000-0000-000000000001");
}
