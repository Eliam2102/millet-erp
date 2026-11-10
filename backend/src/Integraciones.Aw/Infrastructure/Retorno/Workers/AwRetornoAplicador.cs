using System.Data.Common;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Infrastructure.Retorno;

/// <summary>
/// M-160: aplica un aviso a A+W (eventos de Facturación y de PT) y, si <c>aw_retorno</c> no responde por conexión, lo guarda
/// como pendiente en vez de dejarlo caer a dead-letter. Lo usan el listener de Service Bus, el consumidor de PT, el
/// reintento periódico y el reproceso de dead-letter, así los cuatro aplican igual.
/// </summary>
public static class AwRetornoAplicador
{
    /// <summary>Errores de SQL Server que significan «A+W no está» (red, login, base no disponible, tiempo agotado).</summary>
    private static readonly HashSet<int> ErroresConexionSql =
    [
        -2, -1, 2, 53, 64, 121, 233, 258, 1205, 1222, 4060, 4221, 18456, 10053, 10054, 10060, 10061, 11001,
        40143, 40197, 40501, 40613, 49918, 49919, 49920,
    ];

    /// <summary>
    /// true si la falla es de conexión con A+W (se reintenta más tarde); false si es del aviso mismo (payload, regla,
    /// constraint) y debe seguir su camino de siempre (dead-letter).
    /// </summary>
    public static bool EsDeConexion(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case SqlException sql:
                    return sql.Class >= 20 || ErroresConexionSql.Contains(sql.Number);
                case DbException db when db.IsTransient: // Npgsql (origen Demo): conexión rechazada, socket, timeout
                case TimeoutException or SocketException or IOException:
                    return true;
            }
        }
        return false;
    }

    /// <summary>Mensaje corto de la falla para el pendiente (el detalle completo queda en el log).</summary>
    public static string Mensaje(Exception ex) => ex is SqlException sql ? $"SQL {sql.Number}: {sql.Message}" : $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>¿Este evento escribe en aw_retorno?</summary>
    public static bool EsAviso(string eventType) =>
        FacturacionRetornoListenerWorker.EsAviso(eventType) || ProductoTerminadoRetornoLocalConsumer.EsAviso(eventType);

    /// <summary>Aplica el aviso en un scope propio. Lanza la falla tal cual (conexión o no).</summary>
    public static async Task AplicarAsync(IServiceScopeFactory scopes, string origen, string eventType, string body, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        using var marca = sp.GetService<IAuditOriginContext>()?.SetOrigin(origen);
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        if (FacturacionRetornoListenerWorker.EsAviso(eventType))
            await FacturacionRetornoListenerWorker.AplicarAsync(sp.GetRequiredService<AwRetornoDocumentoService>(), eventType, body, ct);
        else if (ProductoTerminadoRetornoLocalConsumer.EsAviso(eventType))
            await ProductoTerminadoRetornoLocalConsumer.AplicarAsync(sp, eventType, body, ct);
    }

    /// <summary>
    /// Aplica el aviso; si A+W no responde por conexión, lo guarda como pendiente y devuelve false (el mensaje se completa).
    /// Cualquier otra falla se relanza. Si guardar el pendiente falla (base del ERP caída), también se relanza: el mensaje
    /// no se pierde, Service Bus lo reintenta.
    /// </summary>
    public static async Task<bool> AplicarOPendienteAsync(
        IServiceScopeFactory scopes, string origen, string eventType, string body, CancellationToken ct)
    {
        try
        {
            await AplicarAsync(scopes, origen, eventType, body, ct);
            return true;
        }
        catch (Exception ex) when (EsDeConexion(ex) && !ct.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AwRetornoPendientes>().GuardarAsync(eventType, body, Mensaje(ex), ct);
            return false;
        }
    }
}
