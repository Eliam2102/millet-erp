using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.SharedKernel.Infrastructure.Outbox;

/// <summary>
/// Outbox de un módulo como fuente del tablero de integraciones (I13.6): los
/// eventos que el módulo publica a Service Bus. Sin publicar y sin intentos =
/// pendiente; con intentos hasta <see cref="OutboxPublisherOptions.MaxAttempts"/>
/// = reintentando (el worker lo vuelve a intentar en cada ciclo); arriba de ese
/// límite = error, que el worker ya no toma («dead-letter pasivo») y se reintenta
/// a mano reiniciando el contador.
/// </summary>
public sealed class OutboxFuenteTablero<TContext>(TContext db, IOptionsMonitor<OutboxPublisherOptions> opciones, string modulo)
    : IFuenteTableroIntegracion
    where TContext : DbContext
{
    private OutboxPublisherOptions Opciones => opciones.Get(typeof(TContext).Name);

    public string Clave => $"outbox.{typeof(TContext).Name.Replace("DbContext", "", StringComparison.Ordinal).ToLowerInvariant()}";
    public string Nombre => $"Eventos de {modulo} ({Opciones.ServiceBusTopicName})";
    public string Grupo => "Service Bus";
    public bool PermiteReintento => true;

    public async Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var max = Opciones.MaxAttempts;
        var desde = ahora.AddHours(-24);
        var outbox = db.Set<IntegrationEventOutboxEntry>().AsNoTracking();
        var sinPublicar = outbox.Where(e => e.PublishedAt == null);

        var pendientes = await sinPublicar.CountAsync(e => e.Attempts == 0, ct);
        var reintentando = await sinPublicar.CountAsync(e => e.Attempts > 0 && e.Attempts <= max, ct);
        var enError = await sinPublicar.CountAsync(e => e.Attempts > max, ct);
        var ok = await outbox.CountAsync(e => e.PublishedAt >= desde, ct);
        var ultimoExito = await outbox.Where(e => e.PublishedAt != null).MaxAsync(e => e.PublishedAt, ct);
        var masAntiguo = await sinPublicar.Where(e => e.Attempts <= max)
            .OrderBy(e => e.OccurredAt).Select(e => (DateTimeOffset?)e.OccurredAt).FirstOrDefaultAsync(ct);

        var problemas = await sinPublicar.OrderByDescending(e => e.Attempts).ThenBy(e => e.OccurredAt)
            .Take(maxMensajes).ToListAsync(ct);
        var recientes = problemas.Count >= maxMensajes
            ? []
            : await outbox.Where(e => e.PublishedAt != null).OrderByDescending(e => e.PublishedAt)
                .Take(maxMensajes - problemas.Count).ToListAsync(ct);

        var mensajes = problemas.Concat(recientes).Select(e => new MensajeIntegracion(
            e.Id.ToString(), e.EventType, Estado(e, max), e.Attempts, e.LastError, e.OccurredAt, e.PublishedAt)).ToList();
        return new ResumenFuenteIntegracion(pendientes, reintentando, enError, ok, ultimoExito, masAntiguo, mensajes);
    }

    public async Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(mensajeId, out var id)) return false;
        var e = await db.Set<IntegrationEventOutboxEntry>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null || !e.ReiniciarIntentos()) return false;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal static EstadoMensajeIntegracion Estado(IntegrationEventOutboxEntry e, int maxAttempts) =>
        e.PublishedAt is not null ? EstadoMensajeIntegracion.Ok
        : e.Attempts == 0 ? EstadoMensajeIntegracion.Pendiente
        : e.Attempts <= maxAttempts ? EstadoMensajeIntegracion.Reintentando
        : EstadoMensajeIntegracion.Error;
}

public static class TableroIntegracionesServiceCollectionExtensions
{
    /// <summary>Registra el outbox de <typeparamref name="TContext"/> como fuente del tablero de integraciones (I13.6).</summary>
    public static IServiceCollection AddFuenteTableroOutbox<TContext>(this IServiceCollection services, string modulo)
        where TContext : DbContext =>
        services.AddScoped<IFuenteTableroIntegracion>(sp => new OutboxFuenteTablero<TContext>(
            sp.GetRequiredService<TContext>(), sp.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>(), modulo));
}
