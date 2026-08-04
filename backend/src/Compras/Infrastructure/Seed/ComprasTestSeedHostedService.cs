using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

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
    private readonly ILogger<ComprasTestSeedHostedService> _logger;

    public ComprasTestSeedHostedService(
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        ILogger<ComprasTestSeedHostedService> logger)
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
                "ComprasTestSeed: environment=Production, seed omitido. " +
                "Las secuencias arrancan desde 1 en producción.");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();

        using var bypass = empresaContext.Bypass();

        await AdelantarFolioSecuenciaAsync(db, cancellationToken);
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
