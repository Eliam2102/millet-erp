using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure.Telemetry;
using Millet.SharedKernel.Application;

namespace Millet.Identidad.Infrastructure.Adapters;

/// <summary>
/// Implementación productiva de <see cref="IServicePrincipalResolver"/>
/// que lee de <see cref="IdentidadDbContext"/>. Match exclusivo por
/// <c>EntraAppId</c> (D-RES); el <c>EntraObjectId</c> recibido se ignora
/// para resolución pero queda PLATFORM-TODO de validación multi-tenant.
///
/// <para>
/// Bypass del query filter por empresa: <c>UsuarioServicio</c> NO implementa
/// <c>IPerteneceAEmpresa</c>, por lo que no hay filter automático. Si en
/// el futuro se agrega, este resolver tendrá que envolverse en
/// <c>using empresaContext.Bypass()</c> porque resuelve sin contexto de
/// empresa pre-existente (justamente lo está estableciendo).
/// </para>
/// </summary>
public sealed class DefaultServicePrincipalResolver : IServicePrincipalResolver
{
    private readonly IdentidadDbContext _db;
    private readonly IdentidadMeter _meter;

    public DefaultServicePrincipalResolver(IdentidadDbContext db, IdentidadMeter meter)
    {
        _db = db;
        _meter = meter;
    }

    public async Task<ServicePrincipalResolutionResult> ResolveAsync(
        Guid entraAppId,
        Guid entraObjectId,
        CancellationToken cancellationToken = default)
    {
        // PLATFORM-TODO(<MultiTenantSpResolution>): cuando se soporte
        // multi-tenant, validar que entraObjectId coincida con
        // sp.EntraObjectId persistido (en single-tenant el AppId basta).
        _ = entraObjectId;

        var sp = await _db.UsuariosServicio
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EntraAppId == entraAppId, cancellationToken);

        if (sp is null)
        {
            _meter.SpResolutionUnknown.Add(1);
            return ServicePrincipalResolutionResult.NotFound();
        }

        if (!sp.Activo)
        {
            _meter.SpResolutionDisabled.Add(1);
            return ServicePrincipalResolutionResult.Disabled();
        }

        var permisos = await _db.UsuarioServicioPermisos
            .AsNoTracking()
            .Where(x => x.UsuarioServicioId == sp.Id)
            .Select(x => x.PermisoClave)
            .ToListAsync(cancellationToken);

        return ServicePrincipalResolutionResult.Found(new ResolvedServicePrincipal(
            Id: sp.Id,
            Nombre: sp.Nombre,
            EntraAppId: sp.EntraAppId,
            EmpresaId: sp.EmpresaId,
            Permisos: permisos));
    }
}
