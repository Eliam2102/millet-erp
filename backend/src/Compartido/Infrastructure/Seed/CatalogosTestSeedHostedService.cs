using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

    /// <summary>
    /// F2 (Parte F, 2026-09-24): fase de datos demo, separada del seed de
    /// prueba de arriba (del que dependen los tests de integración — no se
    /// toca). Solo corre si <c>Seed:DatosDemo:Habilitado=true</c>
    /// (<c>appsettings.Development.json</c> lo pone en <c>true</c>;
    /// <c>TestAssemblyInit</c> de los 3 proyectos de integración lo fuerza
    /// a <c>false</c> por variable de entorno). Nunca corre en Production
    /// (el guard de <see cref="StartAsync"/> ya excluye todo el hosted
    /// service).
    /// </summary>
    private readonly bool _datosDemoHabilitado;

    public CatalogosTestSeedHostedService(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<CatalogosTestSeedHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _datosDemoHabilitado = configuration.GetValue<bool>("Seed:DatosDemo:Habilitado");
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

                if (_datosDemoHabilitado)
                {
                    await SeedDepartamentosDemoAsync(db, ct);
                    await SeedAsignacionesSucursalDepartamentoDemoAsync(db, ct);
                    await SeedPuestosDemoAsync(db, ct);
                    await SeedAsignacionesSucursalPuestoDemoAsync(db, ct);
                    await SeedEmpleadosDemoAsync(db, ct);
                }
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
            .Select(p => new SucursalDepartamento(p.Id, CompartidoDbContext.EmpresaBootstrapId, p.SucursalId, p.DepartamentoId))
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

    // ─── F2 (Parte F): datos demo de desarrollo ─────────────────────────
    //
    // Solo corren si Seed:DatosDemo:Habilitado=true. Complementan (no
    // reemplazan) el seed de prueba de arriba: reusan las 3 plantas
    // (TestSucursales) y los 5 departamentos existentes (TestDepartamentos),
    // y agregan 2 departamentos, 9 puestos genéricos nuevos (el 10º del
    // pedido del owner, GER, ya existía — migración PuestosYEmpleados D6 —
    // y se reutiliza, ver PuestoGerenteId), sus asignaciones por
    // sucursal+departamento (muestran F1-ADM-01.4: un puesto en varios
    // departamentos de la sucursal) y ~15 empleados ficticios.
    //
    // GUIDs deterministas bajo el prefijo 00000005-0009-* (namespace nuevo,
    // no choca con 0001 proveedores, 0002 artículos, 0003 sucursales,
    // 0004 departamentos, 0005 almacenes, 0007 asignaciones sucursal-depto
    // ya usados en este archivo, ni con 0006/0008 usados por
    // PermisosCanonicos en Identidad — tablas distintas, pero se evita el
    // choque de todos modos):
    //   0009-0001-* departamentos demo
    //   0009-0002-* puestos demo
    //   0009-0003-* asignaciones sucursal-departamento demo
    //   0009-0004-* asignaciones sucursal-puesto demo
    //   0009-0005-* empleados demo
    //
    // Rol sugerido: los únicos roles con id estable en el bootstrap de
    // Identidad (BootstrapSuperAdminHostedService) son roles "admin-*" del
    // ERP (admin-identidad, admin-organizacional, admin-catálogos,
    // admin-datos-maestros, auditor, admin-compras) — no hay roles
    // operativos (Gerente, Supervisor, Comprador, etc.) con id estable.
    // Ninguno de los 7 roles existentes corresponde de forma razonable a
    // los puestos organizacionales de este seed, así que RolSugeridoId
    // queda en null en todos los puestos/asignaciones demo (reportado en
    // la entrega de F2; no se inventa un mapeo forzado).

    private async Task SeedDepartamentosDemoAsync(CompartidoDbContext db, CancellationToken ct)
    {
        var claves = DemoDepartamentos.Select(d => d.Clave).ToArray();
        var existentes = await db.Departamentos.AsNoTracking()
            .Where(d => claves.Contains(d.Clave))
            .Select(d => d.Clave)
            .ToListAsync(ct);
        var faltantes = DemoDepartamentos.Where(d => !existentes.Contains(d.Clave)).ToList();
        if (faltantes.Count == 0)
        {
            _logger.LogDebug("CatalogosTestSeed datos-demo departamentos: ya existían los {N}.", DemoDepartamentos.Length);
            return;
        }
        db.Departamentos.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("CatalogosTestSeed datos-demo departamentos: insertados {N} nuevos.", faltantes.Count);
    }

    /// <summary>
    /// Asigna DIR y ADMFIN a las 3 plantas (mismo patrón que
    /// <see cref="SeedAsignacionesSucursalDepartamentoAsync"/>, namespace
    /// de GUIDs propio para no chocar con las 0007 existentes).
    /// </summary>
    private async Task SeedAsignacionesSucursalDepartamentoDemoAsync(CompartidoDbContext db, CancellationToken ct)
    {
        var pares = (
            from s in TestSucursales.Select((suc, i) => (Sucursal: suc, Idx: i + 1))
            from d in DemoDepartamentos.Select((dep, i) => (Departamento: dep, Idx: i + 1))
            select new
            {
                Id = Guid.Parse($"00000005-0009-0003-{s.Idx:X4}-{d.Idx:X12}"),
                SucursalId = s.Sucursal.Id,
                DepartamentoId = d.Departamento.Id,
            }).ToList();

        var existentesSet = (await db.SucursalDepartamentos.AsNoTracking()
                .Select(a => new { a.SucursalId, a.DepartamentoId })
                .ToListAsync(ct))
            .Select(x => (x.SucursalId, x.DepartamentoId))
            .ToHashSet();

        var faltantes = pares
            .Where(p => !existentesSet.Contains((p.SucursalId, p.DepartamentoId)))
            .Select(p => new SucursalDepartamento(p.Id, CompartidoDbContext.EmpresaBootstrapId, p.SucursalId, p.DepartamentoId))
            .ToList();

        if (faltantes.Count == 0) return;
        db.SucursalDepartamentos.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "CatalogosTestSeed datos-demo sucursal_departamentos: insertados {N} nuevos.",
            faltantes.Count);
    }

    private async Task SeedPuestosDemoAsync(CompartidoDbContext db, CancellationToken ct)
    {
        var claves = DemoPuestos.Select(p => p.Clave).ToArray();
        var existentes = await db.Puestos.AsNoTracking()
            .Where(p => claves.Contains(p.Clave))
            .Select(p => p.Clave)
            .ToListAsync(ct);
        var faltantes = DemoPuestos.Where(p => !existentes.Contains(p.Clave)).ToList();
        if (faltantes.Count == 0)
        {
            _logger.LogDebug("CatalogosTestSeed datos-demo puestos: ya existían los {N}.", DemoPuestos.Length);
            return;
        }
        db.Puestos.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("CatalogosTestSeed datos-demo puestos: insertados {N} nuevos.", faltantes.Count);
    }

    /// <summary>
    /// Asignaciones sucursal+puesto+departamento del seed demo (F1-ADM-01.4
    /// reabierta): GER en 6 departamentos de cada planta, SUP en 3, AUX en
    /// 3, DIRGEN solo en DIR de MID, y el resto de los puestos en su
    /// departamento natural en las 3 plantas — ver <see cref="DemoAsignacionesPuesto"/>.
    /// Comprobación por clave de negocio (sucursal, puesto, departamento),
    /// no por Id: reordenar la lista no duplica filas.
    /// </summary>
    private async Task SeedAsignacionesSucursalPuestoDemoAsync(CompartidoDbContext db, CancellationToken ct)
    {
        var existentesSet = (await db.SucursalPuestos.AsNoTracking()
                .Select(a => new { a.SucursalId, a.PuestoId, a.DepartamentoId })
                .ToListAsync(ct))
            .Select(x => (x.SucursalId, x.PuestoId, x.DepartamentoId))
            .ToHashSet();

        var faltantes = DemoAsignacionesPuesto
            .Where(a => !existentesSet.Contains((a.SucursalId, a.PuestoId, a.DepartamentoId)))
            .Select(a => new SucursalPuesto(
                a.Id, CompartidoDbContext.EmpresaBootstrapId, a.SucursalId, a.PuestoId, a.DepartamentoId))
            .ToList();

        if (faltantes.Count == 0)
        {
            _logger.LogDebug(
                "CatalogosTestSeed datos-demo sucursal_puestos: ya existían las {N}.",
                DemoAsignacionesPuesto.Length);
            return;
        }
        db.SucursalPuestos.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "CatalogosTestSeed datos-demo sucursal_puestos: insertadas {N} nuevas.",
            faltantes.Count);
    }

    private async Task SeedEmpleadosDemoAsync(CompartidoDbContext db, CancellationToken ct)
    {
        var claves = DemoEmpleados.Select(e => e.Clave).ToArray();
        var existentes = await db.Empleados.AsNoTracking()
            .Where(e => claves.Contains(e.Clave))
            .Select(e => e.Clave)
            .ToListAsync(ct);
        var faltantes = DemoEmpleados.Where(e => !existentes.Contains(e.Clave)).ToList();
        if (faltantes.Count == 0)
        {
            _logger.LogDebug("CatalogosTestSeed datos-demo empleados: ya existían los {N}.", DemoEmpleados.Length);
            return;
        }
        db.Empleados.AddRange(faltantes);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("CatalogosTestSeed datos-demo empleados: insertados {N} nuevos.", faltantes.Count);
    }

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
    /// Millet: México Centro, Monterrey, Querétaro. F1-ADM-01: todas
    /// pertenecen a la empresa raíz de bootstrap
    /// (<see cref="CompartidoDbContext.EmpresaBootstrapId"/>, única
    /// empresa hoy); domicilio y demás campos nuevos son datos
    /// EVIDENTEMENTE FICTICIOS de dev (sin datos reales de Millet en git).
    /// </summary>
    public static readonly Sucursal[] TestSucursales =
    [
        new Sucursal(
            id: Guid.Parse("00000005-0003-0000-0000-000000000001"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "MID",
            nombre: "Planta México Centro",
            tipo: TipoSucursal.Matriz,
            calle: "Calle Ficticia 123",
            numeroExterior: "123",
            colonia: "Colonia de Prueba",
            ciudad: "Mérida",
            municipio: "Mérida",
            estado: "Yucatán",
            codigoPostal: "97000",
            pais: "México"),
        new Sucursal(
            id: Guid.Parse("00000005-0003-0000-0000-000000000002"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "MTY",
            nombre: "Planta Monterrey",
            tipo: TipoSucursal.Planta,
            calle: "Avenida Ficticia 456",
            numeroExterior: "456",
            colonia: "Colonia de Prueba Norte",
            ciudad: "Monterrey",
            municipio: "Monterrey",
            estado: "Nuevo León",
            codigoPostal: "64000",
            pais: "México"),
        new Sucursal(
            id: Guid.Parse("00000005-0003-0000-0000-000000000003"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "QRO",
            nombre: "Planta Querétaro",
            tipo: TipoSucursal.Sucursal,
            calle: "Boulevard Ficticio 789",
            numeroExterior: "789",
            colonia: "Colonia de Prueba Centro",
            ciudad: "Querétaro",
            municipio: "Querétaro",
            estado: "Querétaro",
            codigoPostal: "76000",
            pais: "México"),
    ];

    /// <summary>
    /// 5 departamentos funcionales seedeados (00000005-0004-...).
    /// COMPRAS = el "depto primario" del dev-superadmin (asignado en
    /// el bootstrap para que /api/auth/me retorne un departamentoId
    /// no-null en dev/UAT). F1-ADM-01: todos pertenecen a la empresa
    /// raíz de bootstrap (única empresa hoy).
    /// </summary>
    public static readonly Departamento[] TestDepartamentos =
    [
        new Departamento(
            id: Guid.Parse("00000005-0004-0000-0000-000000000001"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "COMPRAS",
            nombre: "Compras y Adquisiciones"),
        new Departamento(
            id: Guid.Parse("00000005-0004-0000-0000-000000000002"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "ALMACEN",
            nombre: "Almacén No-Producción"),
        new Departamento(
            id: Guid.Parse("00000005-0004-0000-0000-000000000003"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "MTTO",
            nombre: "Mantenimiento"),
        new Departamento(
            id: Guid.Parse("00000005-0004-0000-0000-000000000004"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "ING",
            nombre: "Ingeniería"),
        new Departamento(
            id: Guid.Parse("00000005-0004-0000-0000-000000000005"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
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

    // ─── F2 (Parte F): datos demo de desarrollo ─────────────────────────

    /// <summary>
    /// 2 departamentos extra (00000005-0009-0001-...), asignados a las 3
    /// plantas por <see cref="SeedAsignacionesSucursalDepartamentoDemoAsync"/>.
    /// </summary>
    public static readonly Departamento[] DemoDepartamentos =
    [
        new Departamento(
            id: Guid.Parse("00000005-0009-0001-0000-000000000001"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "DIR",
            nombre: "Dirección General"),
        new Departamento(
            id: Guid.Parse("00000005-0009-0001-0000-000000000002"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "ADMFIN",
            nombre: "Administración y Finanzas"),
    ];

    private static readonly Guid DepartamentoDireccionId = DemoDepartamentos[0].Id;
    private static readonly Guid DepartamentoAdminFinanzasId = DemoDepartamentos[1].Id;

    // IDs de los puestos demo. Constantes independientes (no derivadas por
    // índice de DemoPuestos) para no depender de la posición de cada uno
    // en el array — GER es un caso aparte, ver abajo.
    private static readonly Guid PuestoDirGenId = Guid.Parse("00000005-0009-0002-0000-000000000001");

    /// <summary>
    /// GER (Gerente) YA EXISTE: lo sembró la migración
    /// <c>20260716163841_PuestosYEmpleados</c> (decisión D6, herencia del
    /// antiguo <c>NoOpPuestoReadPort</c> de CxP) con id fijo
    /// <c>00000000-0000-0000-0000-000000000002</c> y clave "GER" — la
    /// migración <c>F1Adm01MultiempresaOrganizacional</c> le asignó
    /// <see cref="CompartidoDbContext.EmpresaBootstrapId"/> al hacer
    /// <c>empresa_id</c> NOT NULL. <c>Puesto.Clave</c> es única por
    /// empresa: crear otro "GER" aquí violaría esa unicidad. Este seed
    /// demo REUTILIZA ese id en vez de crear uno nuevo — no aparece en
    /// <see cref="DemoPuestos"/> (nada que insertar), pero sí en
    /// <see cref="DemoAsignacionesPuesto"/> y <see cref="DemoEmpleados"/>.
    /// </summary>
    private static readonly Guid PuestoGerenteId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static readonly Guid PuestoSupervisorId = Guid.Parse("00000005-0009-0002-0000-000000000003");
    private static readonly Guid PuestoJefeAlmacenId = Guid.Parse("00000005-0009-0002-0000-000000000004");
    private static readonly Guid PuestoCompradorId = Guid.Parse("00000005-0009-0002-0000-000000000005");
    private static readonly Guid PuestoAuxAdminId = Guid.Parse("00000005-0009-0002-0000-000000000006");
    private static readonly Guid PuestoTecMttoId = Guid.Parse("00000005-0009-0002-0000-000000000007");
    private static readonly Guid PuestoIngPlantaId = Guid.Parse("00000005-0009-0002-0000-000000000008");
    private static readonly Guid PuestoInspCalidadId = Guid.Parse("00000005-0009-0002-0000-000000000009");
    private static readonly Guid PuestoContadorId = Guid.Parse("00000005-0009-0002-0000-00000000000a");

    /// <summary>
    /// 9 puestos genéricos nuevos (00000005-0009-0002-...; GER es el 10º
    /// del pedido del owner pero ya existe — ver <see cref="PuestoGerenteId"/>,
    /// no se crea de nuevo). <see cref="Puesto.DepartamentoId"/> solo es
    /// dato de referencia (no fuente de verdad, ver <c>Puesto.cs</c>): se
    /// fija para los puestos con un único departamento natural y queda
    /// null para SUP/AUX (y GER), que operan en varios departamentos de
    /// la sucursal (esa es justo la asignación que muestra F1-ADM-01.4).
    /// <see cref="Puesto.RolSugeridoId"/> queda null — ver la nota arriba
    /// de "Rol sugerido" sobre por qué no se mapea a ninguno de los 7 roles
    /// del bootstrap.
    /// </summary>
    public static readonly Puesto[] DemoPuestos =
    [
        new Puesto(
            id: PuestoDirGenId,
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "DIRGEN", nombre: "Director General",
            departamentoId: DepartamentoDireccionId),
        new Puesto(
            id: PuestoSupervisorId,
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "SUP", nombre: "Supervisor"),
        new Puesto(
            id: PuestoJefeAlmacenId,
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "JEFALM", nombre: "Jefe de Almacén",
            departamentoId: TestDepartamentos[1].Id),
        new Puesto(
            id: PuestoCompradorId,
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "COMP", nombre: "Comprador",
            departamentoId: TestDepartamentos[0].Id),
        new Puesto(
            id: PuestoAuxAdminId,
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "AUX", nombre: "Auxiliar Administrativo"),
        new Puesto(
            id: PuestoTecMttoId,
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "TECMTTO", nombre: "Técnico de Mantenimiento",
            departamentoId: TestDepartamentos[2].Id),
        new Puesto(
            id: PuestoIngPlantaId,
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "INGPTA", nombre: "Ingeniero de Planta",
            departamentoId: TestDepartamentos[3].Id),
        new Puesto(
            id: PuestoInspCalidadId,
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "INSPCAL", nombre: "Inspector de Calidad",
            departamentoId: TestDepartamentos[4].Id),
        new Puesto(
            id: PuestoContadorId,
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "CONT", nombre: "Contador",
            departamentoId: DepartamentoAdminFinanzasId),
    ];

    /// <summary>
    /// 55 asignaciones sucursal+puesto+departamento (00000005-0009-0004-...):
    /// GER en los 6 departamentos de cada planta (COMPRAS/ALMACEN/MTTO/ING/CAL/ADMFIN),
    /// SUP en 3 (ALMACEN/MTTO/CAL), AUX en 3 (COMPRAS/ALMACEN/ADMFIN),
    /// DIRGEN solo en DIR de MID, y los 6 puestos restantes en su
    /// departamento natural en las 3 plantas. Muestra exactamente el caso
    /// que motivó F1-ADM-01.4 reabierta (un puesto genérico en varios
    /// departamentos de la sucursal).
    /// </summary>
    public static readonly (Guid Id, Guid SucursalId, Guid PuestoId, Guid DepartamentoId)[] DemoAsignacionesPuesto =
        BuildDemoAsignacionesPuesto();

    private static (Guid Id, Guid SucursalId, Guid PuestoId, Guid DepartamentoId)[] BuildDemoAsignacionesPuesto()
    {
        var gerDeptos = new[]
        {
            TestDepartamentos[0].Id, TestDepartamentos[1].Id, TestDepartamentos[2].Id,
            TestDepartamentos[3].Id, TestDepartamentos[4].Id, DepartamentoAdminFinanzasId,
        };
        var supDeptos = new[] { TestDepartamentos[1].Id, TestDepartamentos[2].Id, TestDepartamentos[4].Id };
        var auxDeptos = new[] { TestDepartamentos[0].Id, TestDepartamentos[1].Id, DepartamentoAdminFinanzasId };
        var naturales = new (Guid PuestoId, Guid DepartamentoId)[]
        {
            (PuestoJefeAlmacenId, TestDepartamentos[1].Id),  // JEFALM → ALMACEN
            (PuestoCompradorId, TestDepartamentos[0].Id),    // COMP → COMPRAS
            (PuestoTecMttoId, TestDepartamentos[2].Id),      // TECMTTO → MTTO
            (PuestoIngPlantaId, TestDepartamentos[3].Id),    // INGPTA → ING
            (PuestoInspCalidadId, TestDepartamentos[4].Id),  // INSPCAL → CAL
            (PuestoContadorId, DepartamentoAdminFinanzasId), // CONT → ADMFIN
        };

        var resultado = new List<(Guid Id, Guid SucursalId, Guid PuestoId, Guid DepartamentoId)>();
        var contador = 0;
        void Agregar(Guid sucursalId, Guid puestoId, Guid departamentoId)
        {
            contador++;
            resultado.Add((
                Guid.Parse($"00000005-0009-0004-0000-{contador:X12}"),
                sucursalId, puestoId, departamentoId));
        }

        foreach (var sucursal in TestSucursales)
        {
            foreach (var depto in gerDeptos) Agregar(sucursal.Id, PuestoGerenteId, depto);
            foreach (var depto in supDeptos) Agregar(sucursal.Id, PuestoSupervisorId, depto);
            foreach (var depto in auxDeptos) Agregar(sucursal.Id, PuestoAuxAdminId, depto);
            foreach (var (puestoId, departamentoId) in naturales) Agregar(sucursal.Id, puestoId, departamentoId);
        }

        // DIRGEN solo en Dirección General de MID (única planta con ese
        // departamento en el seed demo).
        Agregar(TestSucursales[0].Id, PuestoDirGenId, DepartamentoDireccionId);

        return resultado.ToArray();
    }

    /// <summary>
    /// 16 empleados ficticios (00000005-0009-0005-...), claves EMP-001..016,
    /// repartidos en las 3 plantas con puesto+departamento coherentes con
    /// <see cref="DemoAsignacionesPuesto"/>. EMP-015 y EMP-016 quedan dados
    /// de baja (<see cref="EstatusCatalogo.Inactivo"/>) para mostrar el
    /// ciclo de vida. Correos en dominio ficticio <c>@ejemplo.test</c>
    /// (pasan <c>EmailAddress()</c> de FluentValidation sin ser datos
    /// reales). Sin <c>JefeDirectoId</c>: evita depender del orden de
    /// inserción de EF Core para la FK self-referenciada.
    /// </summary>
    public static readonly Empleado[] DemoEmpleados =
    [
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-000000000001"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-001", nombre: "Ricardo Fernández Solís",
            email: "ricardo.fernandez@ejemplo.test",
            puestoId: PuestoDirGenId, sucursalId: TestSucursales[0].Id, departamentoId: DepartamentoDireccionId),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-000000000002"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-002", nombre: "Patricia Domínguez Vega",
            email: "patricia.dominguez@ejemplo.test",
            puestoId: PuestoGerenteId, sucursalId: TestSucursales[0].Id, departamentoId: TestDepartamentos[0].Id),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-000000000003"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-003", nombre: "Jorge Iván Salazar Mora",
            email: "jorge.salazar@ejemplo.test",
            puestoId: PuestoGerenteId, sucursalId: TestSucursales[1].Id, departamentoId: TestDepartamentos[1].Id),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-000000000004"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-004", nombre: "Claudia Beatriz Reyes Nuño",
            email: "claudia.reyes@ejemplo.test",
            puestoId: PuestoGerenteId, sucursalId: TestSucursales[2].Id, departamentoId: DepartamentoAdminFinanzasId),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-000000000005"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-005", nombre: "Miguel Ángel Cordero Luna",
            email: "miguel.cordero@ejemplo.test",
            puestoId: PuestoSupervisorId, sucursalId: TestSucursales[0].Id, departamentoId: TestDepartamentos[1].Id),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-000000000006"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-006", nombre: "Sandra Elizabeth Botello Cruz",
            email: "sandra.botello@ejemplo.test",
            puestoId: PuestoSupervisorId, sucursalId: TestSucursales[1].Id, departamentoId: TestDepartamentos[2].Id),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-000000000007"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-007", nombre: "Fernando Aguilar Peña",
            email: "fernando.aguilar@ejemplo.test",
            puestoId: PuestoSupervisorId, sucursalId: TestSucursales[2].Id, departamentoId: TestDepartamentos[4].Id),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-000000000008"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-008", nombre: "Laura Ximena Cabrera Ríos",
            email: "laura.cabrera@ejemplo.test",
            puestoId: PuestoJefeAlmacenId, sucursalId: TestSucursales[0].Id, departamentoId: TestDepartamentos[1].Id),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-000000000009"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-009", nombre: "Héctor Iván Barajas Montes",
            email: "hector.barajas@ejemplo.test",
            puestoId: PuestoCompradorId, sucursalId: TestSucursales[1].Id, departamentoId: TestDepartamentos[0].Id),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-00000000000a"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-010", nombre: "Daniela Fuentes Rangel",
            email: "daniela.fuentes@ejemplo.test",
            puestoId: PuestoAuxAdminId, sucursalId: TestSucursales[2].Id, departamentoId: TestDepartamentos[0].Id),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-00000000000b"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-011", nombre: "Alejandro Villaseñor Cano",
            email: "alejandro.villasenor@ejemplo.test",
            puestoId: PuestoTecMttoId, sucursalId: TestSucursales[0].Id, departamentoId: TestDepartamentos[2].Id),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-00000000000c"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-012", nombre: "Mónica Paulina Esparza Duarte",
            email: "monica.esparza@ejemplo.test",
            puestoId: PuestoIngPlantaId, sucursalId: TestSucursales[1].Id, departamentoId: TestDepartamentos[3].Id),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-00000000000d"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-013", nombre: "Rodrigo Nájera Ochoa",
            email: "rodrigo.najera@ejemplo.test",
            puestoId: PuestoInspCalidadId, sucursalId: TestSucursales[2].Id, departamentoId: TestDepartamentos[4].Id),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-00000000000e"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-014", nombre: "Verónica Islas Camacho",
            email: "veronica.islas@ejemplo.test",
            puestoId: PuestoContadorId, sucursalId: TestSucursales[0].Id, departamentoId: DepartamentoAdminFinanzasId),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-00000000000f"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-015", nombre: "Óscar Mendoza Trejo",
            email: "oscar.mendoza@ejemplo.test",
            puestoId: PuestoAuxAdminId, sucursalId: TestSucursales[0].Id, departamentoId: TestDepartamentos[1].Id,
            estatus: EstatusCatalogo.Inactivo),
        new Empleado(
            id: Guid.Parse("00000005-0009-0005-0000-000000000010"),
            empresaId: CompartidoDbContext.EmpresaBootstrapId,
            clave: "EMP-016", nombre: "Karla Ivette Solano Prieto",
            email: "karla.solano@ejemplo.test",
            puestoId: PuestoCompradorId, sucursalId: TestSucursales[2].Id, departamentoId: TestDepartamentos[0].Id,
            estatus: EstatusCatalogo.Inactivo),
    ];
}
