using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Compras.Infrastructure.Seed;

/// <summary>
/// Hosted service que adelanta <c>compras.folio_secuencias</c> en
/// dev/UAT para evitar colisión con folios <c>MID2026-XXXXXX</c>
/// legacy que dejaron las suites de integration tests del módulo
/// Compras (~140 RQs por DB local con la <c>SucursalIdFija</c>
/// deprecada <c>00000003-0001-*</c>).
///
/// <para>
/// PR-A2 migra los 14 archivos de fixtures a la sucursal canónica MID
/// (<c>00000005-0003-0000-0000-000000000001</c>). Sin este seed, el
/// primer test post-migración insertaría <c>MID2026-000001</c> y
/// chocaría contra el UNIQUE <c>(empresa_id, folio_anio, folio)</c> de
/// <c>compras.requisiciones</c>, que ya alberga ese folio bajo la
/// sucursal-fija legacy. Adelantamos <c>siguiente=10001</c> para dar
/// 999+ folios de colchón antes de cualquier colisión.
/// </para>
///
/// <para>
/// <b>Scope:</b> dev/UAT. Production se autoexcluye — la sucursal MID
/// productiva arranca sin folios y la secuencia comienza naturalmente
/// desde 1 en la primera RQ real.
/// </para>
///
/// <para>
/// <b>Orden de arranque:</b> registrado en <c>Program.cs</c>
/// <i>después</i> de <c>AddMilletAuth(...)</c>, que es donde se
/// registra <c>BootstrapSuperAdminHostedService</c>. ASP.NET arranca
/// hosted services secuencialmente, así que cuando este seed corre la
/// empresa-bootstrap (<c>EmpresaInicialId</c>) ya existe en
/// <c>compartido.empresas</c>. Si <c>Auth:InitialAdminEntraOid</c>
/// está vacío (entornos sin admin configurado), el bootstrap omite la
/// creación de empresa; este seed sigue ejecutando el upsert pero la
/// fila queda como referencia lógica huérfana — sin daño funcional
/// porque <c>folio_secuencias</c> no tiene FK física a empresa.
/// </para>
///
/// <para>
/// Patrón calcado de <c>AlmacenSeedHostedService</c>: GUIDs literales
/// (mirror del bootstrap), guardia Production, bypass de empresa,
/// idempotencia vía <c>AnyAsync</c> antes del <c>Add</c>.
/// </para>
/// </summary>
public sealed class ComprasTestSeedHostedService : IHostedService
{
    private const long SeedLockId = 6_672_000_003;

    // Mirror de BootstrapSuperAdminHostedService.EmpresaInicialId
    // (private allí; replicado aquí porque Compras.csproj no referencia
    // Identidad — agregar la ref sólo por esta constante sería
    // overengineering). Si el bootstrap cambia el GUID, este seed
    // queda apuntando a empresa inexistente y la fila queda huérfana
    // sin romper nada operativo.
    private static readonly Guid EmpresaBootstrapId =
        Guid.Parse("00000003-0000-0000-0000-000000000001");

    // Mirror de CatalogosTestSeedHostedService.TestSucursales[0].Id
    // (MID — Planta México Centro). Es la sucursal canónica donde se
    // sembraron los 14 fixtures migrados de PR-A2.
    private static readonly Guid SucursalMidId =
        Guid.Parse("00000005-0003-0000-0000-000000000001");

    private const short FolioAnio = 2026;
    private const int SiguienteInicial = 10001;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ComprasTestSeedHostedService> _logger;

    /// <summary>
    /// Vigencia fija del umbral de ensayo (determinista: dos arranques producen la misma fila).
    /// </summary>
    public static readonly DateOnly UmbralEnsayoVigenteDesde = new(2026, 11, 1);

    public ComprasTestSeedHostedService(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<ComprasTestSeedHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_environment.IsProduction())
        {
            _logger.LogInformation(
                "ComprasTestSeed: environment=Production, seed omitido. " +
                "Las secuencias arrancan desde 1 en producción.");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();

        var originContext = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(ComprasTestSeedHostedService));
        using var bypass = empresaContext.Bypass();

        await PostgresAdvisoryLock.ExecuteAsync(
            db,
            SeedLockId,
            async ct =>
            {
                await AdelantarFolioSecuenciaAsync(db, ct);
                if (_configuration.GetValue<bool>("Seed:UmbralEnsayo:Habilitado")
                    && await SembrarUmbralEnsayoAsync(db, EmpresaBootstrapId, ct))
                    _logger.LogWarning(
                        "ComprasTestSeed: sembrado el umbral general de ENSAYO (no es política de Millet). " +
                        "Reemplazarlo desde Configuración de Compras cuando Millet entregue montos y aprobadores.");
                var aprobadores = _configuration.GetSection("Seed:AprobadoresEnsayo");
                if (aprobadores.GetValue<bool>("Habilitado"))
                {
                    var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
                    var departamentos = await compartido.Departamentos.AsNoTracking()
                        .Where(d => d.EmpresaId == EmpresaBootstrapId && d.Estatus == EstatusCatalogo.Activo)
                        .Select(d => d.Id).ToListAsync(ct);
                    var sucursales = await compartido.Sucursales.AsNoTracking()
                        .Where(s => s.EmpresaId == EmpresaBootstrapId && s.Estatus == EstatusCatalogo.Activo)
                        .Select(s => s.Id).ToListAsync(ct);
                    var nuevos = await SembrarAprobadoresEnsayoAsync(
                        db, scope.ServiceProvider.GetRequiredService<IdentidadDbContext>(), EmpresaBootstrapId,
                        aprobadores["N1Correo"], aprobadores["N2Correo"], departamentos, sucursales,
                        scope.ServiceProvider.GetRequiredService<IClock>().UtcNow, ct);
                    if (nuevos < 0)
                        _logger.LogWarning(
                            "ComprasTestSeed: aprobadores de ENSAYO omitidos; N1Correo y N2Correo deben ser dos usuarios " +
                            "activos y distintos que ya iniciaron sesión (reiniciar el API después del primer acceso).");
                    else if (nuevos > 0)
                        _logger.LogWarning(
                            "ComprasTestSeed: {Nuevos} aprobadores N1/N2 de ENSAYO sembrados (no son designación de Millet). " +
                            "Reemplazarlos desde Configuración de Compras cuando Millet entregue aprobadores (HMONTOS).",
                            nuevos);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Umbral general de ENSAYO (fila <c>DepartamentoId = Guid.Empty</c>) para que una RQ
    /// de QA no quede en <c>SinPolitica</c> mientras Millet no entrega montos ni aprobadores
    /// (plano 02 §6 y §9 Regla 1: se ensaya con valores ficticios). Valores elegidos para no
    /// decidir nada pendiente: modo <c>SoloDisponibilidad</c> (default del modelo: la RQ pide
    /// N2 solo si hay que comprar) y monto 0, con lo que toda OC con importe sigue pidiendo
    /// la segunda firma, igual que sin umbral (Regla 4; definición de la firma 2 abierta).
    /// Idempotente: si ya existe cualquier fila general de la empresa (sembrada o capturada
    /// por un administrador, vigente o cerrada) no hace nada. Devuelve si insertó.
    /// </summary>
    public static async Task<bool> SembrarUmbralEnsayoAsync(
        ComprasDbContext db, Guid empresaId, CancellationToken cancellationToken)
    {
        if (await db.UmbralesAprobacionDepartamento.AnyAsync(
                u => u.EmpresaId == empresaId && u.DepartamentoId == Guid.Empty, cancellationToken))
            return false;

        db.UmbralesAprobacionDepartamento.Add(new UmbralAprobacionDepartamento(
            empresaId, Guid.Empty, UmbralEnsayoVigenteDesde, umbralMonto: 0m,
            modoNivel2: ModoNivel2.SoloDisponibilidad));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Marca de los aprobadores de ensayo: queda en <c>motivo</c> y se ve en el histórico de aprobadores.</summary>
    public const string MotivoAprobadorEnsayo =
        "ENSAYO QA: aprobador de prueba, no es designación de Millet. Reemplazar en Configuración de Compras (HMONTOS).";

    /// <summary>Actor técnico de auditoría (<c>designado_por</c>) del seed; no es usuario ni identidad de Entra.</summary>
    public static readonly Guid SembradorEnsayoId = Guid.Parse("00000003-5eed-0000-0000-000000000001");

    /// <summary>
    /// Aprobadores N1 (<c>JefeDpto</c> de cada departamento) y N2 (<c>JefeAlmacen</c> de cada
    /// sucursal) de ENSAYO con dos usuarios de prueba ya existentes, para ensayar en QA la
    /// segregación RQ → N1 → N2 mientras Millet no designa aprobadores (Verdad vigente
    /// Compras §4 punto 1). No pisa una designación vigente (real o de ensayo): solo llena
    /// los huecos, así que es idempotente. Devuelve -1 si los correos no resuelven a dos
    /// usuarios activos distintos (una misma persona no firma N1 y N2); si no, cuántos insertó.
    /// </summary>
    public static async Task<int> SembrarAprobadoresEnsayoAsync(
        ComprasDbContext db, IdentidadDbContext identidad, Guid empresaId, string? n1Correo, string? n2Correo,
        IReadOnlyCollection<Guid> departamentoIds, IReadOnlyCollection<Guid> sucursalIds,
        DateTimeOffset ahora, CancellationToken cancellationToken)
    {
        var n1 = await UsuarioActivoAsync(identidad, n1Correo, cancellationToken);
        var n2 = await UsuarioActivoAsync(identidad, n2Correo, cancellationToken);
        if (n1 is not Guid n1Id || n2 is not Guid n2Id || n1Id == n2Id) return -1;

        var objetivos = departamentoIds.Select(id => (Scope: id, Rol: RolAprobador.JefeDpto, Usuario: n1Id))
            .Concat(sucursalIds.Select(id => (Scope: id, Rol: RolAprobador.JefeAlmacen, Usuario: n2Id)))
            .ToList();
        var scopes = objetivos.Select(o => o.Scope).ToList();
        var ocupados = (await db.AprobadoresDepartamento.AsNoTracking()
                .Where(a => a.EmpresaId == empresaId && a.VigenteHasta == null && scopes.Contains(a.DepartamentoId))
                .Select(a => new { a.DepartamentoId, a.Rol })
                .ToListAsync(cancellationToken))
            .Select(a => (a.DepartamentoId, a.Rol)).ToHashSet();

        var nuevos = 0;
        foreach (var (scope, rol, usuario) in objetivos.Where(o => !ocupados.Contains((o.Scope, o.Rol))))
        {
            db.AprobadoresDepartamento.Add(new AprobadorDepartamento(
                Guid.CreateVersion7(), empresaId, scope, rol, usuario, ahora, SembradorEnsayoId, MotivoAprobadorEnsayo, ahora));
            nuevos++;
        }
        if (nuevos > 0) await db.SaveChangesAsync(cancellationToken);
        return nuevos;
    }

    private static async Task<Guid?> UsuarioActivoAsync(
        IdentidadDbContext identidad, string? correo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correo)) return null;
        var normalizado = correo.Trim().ToLowerInvariant();
        return await identidad.Usuarios.AsNoTracking()
            .Where(u => u.Activo && u.Email.ToLower() == normalizado)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task AdelantarFolioSecuenciaAsync(
        ComprasDbContext db, CancellationToken cancellationToken)
    {
        var existe = await db.FolioSecuencias.AsNoTracking()
            .AnyAsync(
                f => f.EmpresaId == EmpresaBootstrapId
                  && f.SucursalId == SucursalMidId
                  && f.Anio == FolioAnio,
                cancellationToken);

        if (existe)
        {
            _logger.LogDebug(
                "ComprasTestSeed: folio_secuencias ya tiene fila para " +
                "(empresa-bootstrap, MID, {Anio}). No-op.",
                FolioAnio);
            return;
        }

        db.FolioSecuencias.Add(new FolioSecuencia(
            empresaId: EmpresaBootstrapId,
            sucursalId: SucursalMidId,
            anio: FolioAnio,
            siguiente: SiguienteInicial));

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "ComprasTestSeed: adelantada folio_secuencias " +
            "(empresa-bootstrap, MID, {Anio}, siguiente={Siguiente}). " +
            "Colchón ante folios MID{Anio}-NNNNNN legacy en DBs locales.",
            FolioAnio, SiguienteInicial, FolioAnio);
    }
}
