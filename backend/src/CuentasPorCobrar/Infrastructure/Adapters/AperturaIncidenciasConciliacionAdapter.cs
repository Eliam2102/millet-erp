using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Millet.CuentasPorCobrar.Domain.Ports.Conciliacion;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.CuentasPorCobrar.Infrastructure.Adapters;

/// <summary>
/// M-236: abre las incidencias de la conciliación en el contexto de la empresa de la corrida. A demanda la empresa ya es la
/// del usuario y se usa el puerto directo; en la corrida programada (worker, bypass: sin empresa ni usuario) abre en un
/// scope propio con la empresa explícita, mismo patrón que el worker de vales de Almacén. El origen de auditoría lo declara
/// el worker que dispara la corrida (<c>AsyncLocal</c>, cubre también este scope); aquí no se llama <c>SetOrigin</c>. La incidencia queda abierta por
/// «Sistema» (automática) a cargo del responsable indicado.
/// </summary>
public sealed class AperturaIncidenciasConciliacionAdapter(
    IServiceScopeFactory scopeFactory,
    ICurrentEmpresaContext empresaActual,
    IIncidenciasPort incidencias) : IAperturaIncidenciasConciliacionPort
{
    public async Task<IncidenciaAbierta> AbrirAsync(Guid empresaId, AbrirIncidenciaSolicitud solicitud, CancellationToken cancellationToken)
    {
        if (empresaActual.Current == empresaId)
            return await incidencias.AbrirAsync(solicitud, cancellationToken);

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var accessor = sp.GetRequiredService<IHttpContextAccessor>();
        var anterior = accessor.HttpContext;
        try
        {
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(MilletClaimTypes.CurrentEmpresaId, empresaId.ToString())], nameof(AperturaIncidenciasConciliacionAdapter))),
            };
            return await sp.GetRequiredService<IIncidenciasPort>().AbrirAsync(solicitud, cancellationToken);
        }
        finally
        {
            accessor.HttpContext = anterior;
        }
    }
}
