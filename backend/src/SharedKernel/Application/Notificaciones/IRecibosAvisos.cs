namespace Millet.SharedKernel.Application.Notificaciones;

public sealed record ReciboAviso(string ClaveOcurrencia, long Secuencia, DateTimeOffset? LeidoAt);
public sealed record RecibosSincronizados(IReadOnlyDictionary<string, ReciboAviso> Recibos, long HastaSecSeguro);

public interface IRecibosAvisos
{
    Task<T> ConBloqueoAsync<T>(ContextoAvisos contexto, Func<Task<T>> operacion, CancellationToken ct);
    Task<RecibosSincronizados> SincronizarAsync(ContextoAvisos contexto, IReadOnlyList<AvisoUsuario> avisos, CancellationToken ct);
    Task MarcarLeidoAsync(ContextoAvisos contexto, string clave, CancellationToken ct);
}
