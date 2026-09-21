using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.Compartido.Application.Ports;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure.Telemetry;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Identidad.Infrastructure;

/// <summary>
/// Hosted service que sincroniza el catálogo de
/// <see cref="UsuarioServicio"/> con el array configurado en
/// <c>Auth:ServicePrincipalsJson</c> (KV ref en QA/Prod, hardcoded en
/// dev). Idempotente: re-arranques verifican el estado y aplican deltas
/// sin duplicar.
///
/// <para>
/// Tolerancia a fallas (D-BOOTSTRAP del prompt PR A): cualquier error en
/// una entrada (empresa missing, AppId duplicado, permisos inválidos,
/// excepción persistiendo) se registra con <c>LogError</c> + counter
/// <c>auth.sp.bootstrap.skipped</c> y se hace skip; las demás entradas
/// continúan. El host NUNCA se cae por un SP mal configurado — el API
/// debe arrancar limpio incluso con todos los SPs rotos.
/// </para>
///
/// <para>
/// Cross-context: la resolución de RFC → EmpresaId va via
/// <see cref="IEmpresaResolverPort"/> (cuya impl vive en Compartido).
/// El bootstrap NO inyecta <c>CompartidoDbContext</c> directamente, lo
/// que mantiene el aislamiento de módulos.
/// </para>
/// </summary>
public sealed class BootstrapServicePrincipalsHostedService : IHostedService
{
    // CA1869: instancia reusable. Cualquier deserialization de la app
    // settings string usa estas opciones (case-insensitive porque los
    // operadores podrían escribir "appid" en lugar de "AppId").
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<BootstrapServicePrincipalsOptions> _options;
    private readonly ILogger<BootstrapServicePrincipalsHostedService> _logger;

    public BootstrapServicePrincipalsHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<BootstrapServicePrincipalsOptions> options,
        ILogger<BootstrapServicePrincipalsHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var json = _options.Value.Json?.Trim();
        if (string.IsNullOrEmpty(json) || json == "[]")
        {
            _logger.LogInformation(
                "Auth:ServicePrincipalsJson vacío. Bootstrap de UsuarioServicio omitido (sin SPs configurados).");
            return;
        }

        List<ServicePrincipalConfig> entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<ServicePrincipalConfig>>(json, JsonOptions)
                ?? new List<ServicePrincipalConfig>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Auth:ServicePrincipalsJson no es JSON válido. Bootstrap de UsuarioServicio abortado (todas las entradas skipped). Verificar el secret 'auth-service-principals-json' en Key Vault.");
            return;
        }

        if (entries.Count == 0)
        {
            _logger.LogInformation("Auth:ServicePrincipalsJson parseado como array vacío. Sin SPs que registrar.");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<IdentidadDbContext>();
        var empresaResolver = sp.GetRequiredService<IEmpresaResolverPort>();
        var clock = sp.GetRequiredService<IClock>();
        var meter = sp.GetRequiredService<IdentidadMeter>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();

        var originContext = sp.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(BootstrapServicePrincipalsHostedService));
        using var bypass = empresaContext.Bypass();

        await PostgresAdvisoryLock.ExecuteAsync(
            db,
            BootstrapSuperAdminHostedService.BootstrapLockId,
            async ct =>
            {
                // Detectar duplicados de AppId en config (D-BOOTSTRAP). Skip la
                // duplicada Y la original — no podemos elegir cuál es "la buena".
                var duplicateAppIds = entries
                    .GroupBy(e => e.AppId)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToHashSet();

                if (duplicateAppIds.Count > 0)
                {
                    foreach (var dupId in duplicateAppIds)
                    {
                        _logger.LogError(
                            "AppId {AppId} aparece en {Count} entradas de Auth:ServicePrincipalsJson. Todas las entradas con ese AppId quedan SKIPPED. Resolver duplicado en config.",
                            dupId, entries.Count(e => e.AppId == dupId));
                        meter.SpBootstrapSkipped.Add(entries.Count(e => e.AppId == dupId));
                    }
                }

                var validClavesPermisos = PermisosCanonicos.Todos
                    .Select(p => p.Codigo)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var entry in entries)
                {
                    if (duplicateAppIds.Contains(entry.AppId))
                    {
                        continue; // ya logueado arriba
                    }

                    try
                    {
                        await ProcessEntryAsync(
                            entry, db, empresaResolver, clock, meter, validClavesPermisos, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Excepción no esperada persistiendo SP '{Nombre}' (AppId={AppId}). Skipped, las demás entradas continúan.",
                            entry.Name, entry.AppId);
                        meter.SpBootstrapSkipped.Add(1);
                    }
                }
            },
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ProcessEntryAsync(
        ServicePrincipalConfig entry,
        IdentidadDbContext db,
        IEmpresaResolverPort empresaResolver,
        IClock clock,
        IdentidadMeter meter,
        HashSet<string> validClavesPermisos,
        CancellationToken ct)
    {
        // Validación 1: nombre y AppId/ObjectId no vacíos.
        if (string.IsNullOrWhiteSpace(entry.Name)
            || entry.AppId == Guid.Empty
            || entry.ObjectId == Guid.Empty)
        {
            _logger.LogError(
                "SP entry inválida: Name='{Nombre}', AppId={AppId}, ObjectId={ObjectId}. Skipped.",
                entry.Name, entry.AppId, entry.ObjectId);
            meter.SpBootstrapSkipped.Add(1);
            return;
        }

        // Validación 2: permisos pertenecen al catálogo canónico.
        var clavesPermisos = entry.Permisos?.ToList() ?? new List<string>();
        var clavesInvalidas = clavesPermisos.Where(p => !validClavesPermisos.Contains(p)).ToList();
        if (clavesInvalidas.Count > 0)
        {
            _logger.LogError(
                "SP '{Nombre}' (AppId={AppId}) tiene {Count} permisos no canónicos: {Invalidos}. Skipped (resolver claves o agregar al catálogo).",
                entry.Name, entry.AppId, clavesInvalidas.Count, string.Join(", ", clavesInvalidas));
            meter.SpBootstrapSkipped.Add(1);
            return;
        }

        // Validación 3: EmpresaRfc resuelve a una empresa existente.
        if (string.IsNullOrWhiteSpace(entry.EmpresaRfc))
        {
            _logger.LogError(
                "SP '{Nombre}' (AppId={AppId}) sin EmpresaRfc. Skipped.",
                entry.Name, entry.AppId);
            meter.SpBootstrapSkipped.Add(1);
            return;
        }

        var empresa = await empresaResolver.ResolveByRfcAsync(entry.EmpresaRfc, ct);
        if (empresa is null)
        {
            _logger.LogError(
                "SP '{Nombre}' (AppId={AppId}) referencia EmpresaRfc='{Rfc}' que no existe en compartido.empresas. Skipped (crear empresa primero o corregir RFC).",
                entry.Name, entry.AppId, entry.EmpresaRfc);
            meter.SpBootstrapSkipped.Add(1);
            return;
        }

        // Upsert.
        var existente = await db.UsuariosServicio
            .FirstOrDefaultAsync(x => x.EntraAppId == entry.AppId, ct);

        if (existente is null)
        {
            existente = new UsuarioServicio(
                id: Guid.CreateVersion7(),
                nombre: entry.Name,
                entraAppId: entry.AppId,
                entraObjectId: entry.ObjectId,
                empresaId: empresa.Id,
                createdAtUtc: clock.UtcNow,
                notes: entry.Notes);
            db.UsuariosServicio.Add(existente);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "SP '{Nombre}' (id={Id}, AppId={AppId}) creado en empresa {Rfc}.",
                existente.Nombre, existente.Id, existente.EntraAppId, empresa.Rfc);
        }
        else
        {
            // Empresa change NO se permite (D-EMPRESA: el SP es 1:1 con
            // empresa). Si la config cambió de empresa, log warning y
            // mantener la asignación original — el operador debe crear
            // un SP nuevo en la otra empresa.
            if (existente.EmpresaId != empresa.Id)
            {
                _logger.LogWarning(
                    "SP '{Nombre}' (AppId={AppId}) en config apunta a empresa {RfcConfig} pero está asignado a empresa {IdActual} en BD. Cambios de empresa no se aplican automáticamente — crear SP nuevo en la otra empresa y desactivar este.",
                    entry.Name, entry.AppId, empresa.Rfc, existente.EmpresaId);
            }

            existente.ActualizarMetadata(entry.Name, entry.ObjectId, entry.Notes);
            if (!existente.Activo)
            {
                existente.Reactivar();
                _logger.LogInformation(
                    "SP '{Nombre}' (AppId={AppId}) reactivado por bootstrap (estaba Activo=false).",
                    existente.Nombre, existente.EntraAppId);
            }
            await db.SaveChangesAsync(ct);
        }

        // Sincronizar permisos: agregar faltantes, remover sobrantes.
        var actuales = await db.UsuarioServicioPermisos
            .Where(p => p.UsuarioServicioId == existente.Id)
            .Select(p => p.PermisoClave)
            .ToListAsync(ct);

        var deseados = clavesPermisos.ToHashSet(StringComparer.Ordinal);
        var actualesSet = actuales.ToHashSet(StringComparer.Ordinal);

        var aAgregar = deseados.Except(actualesSet).ToList();
        var aRemover = actualesSet.Except(deseados).ToList();

        if (aAgregar.Count > 0)
        {
            foreach (var clave in aAgregar)
            {
                db.UsuarioServicioPermisos.Add(new UsuarioServicioPermiso(existente.Id, clave));
            }
        }

        if (aRemover.Count > 0)
        {
            await db.UsuarioServicioPermisos
                .Where(p => p.UsuarioServicioId == existente.Id && aRemover.Contains(p.PermisoClave))
                .ExecuteDeleteAsync(ct);
        }

        if (aAgregar.Count > 0 || aRemover.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "SP '{Nombre}' (AppId={AppId}) permisos sincronizados: +{Add} / -{Remove}. Total esperado: {Total}.",
                existente.Nombre, existente.EntraAppId, aAgregar.Count, aRemover.Count, deseados.Count);
        }

        meter.SpBootstrapUpserted.Add(1);
    }
}
