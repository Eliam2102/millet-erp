using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>
/// Resultado de los avisos de pedido en firme de vuelta a A+W (U1.4, tabla-puente
/// <c>aw_solicitud_pedido</c>) en el tablero de integraciones (I13.6). Cuando la
/// conexión con A+W falla, el pedido ya quedó en el ERP y solo falta avisarle a
/// A+W: el worker lo reintenta cada ciclo hasta <see cref="AwWriteBackOptions.MaxIntentos"/>
/// y después lo deja en error para reintento manual.
/// </summary>
public sealed class AwWriteBackFuenteTablero(FacturacionDbContext db, IOptionsMonitor<AwWriteBackOptions> opciones)
    : IFuenteTableroIntegracion
{
    public string Clave => "facturacion.aw-resultado-pedidos";
    public string Nombre => "Resultado de pedidos hacia A+W";
    public string Grupo => "A+W";
    public bool PermiteReintento => true;

    public async Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var max = opciones.CurrentValue.MaxIntentos;
        var desde = ahora.AddHours(-24);
        var controles = db.IngestaControles.AsNoTracking().Where(c => c.WriteBackSolicitudId != null);
        var abiertos = controles.Where(c => c.WriteBackPendiente);

        var pendientes = await abiertos.CountAsync(c => c.WriteBackIntentos == 0, ct);
        var reintentando = await abiertos.CountAsync(c => c.WriteBackIntentos > 0 && c.WriteBackIntentos < max, ct);
        var enError = await abiertos.CountAsync(c => c.WriteBackIntentos >= max, ct);
        var ok = await controles.CountAsync(c => !c.WriteBackPendiente && c.WriteBackAt >= desde, ct);
        var ultimoExito = await controles.Where(c => c.WriteBackAt != null).MaxAsync(c => c.WriteBackAt, ct);
        // UltimaLecturaAt se toca al pedir el write-back y en cada falla; sin falla es la hora en que quedó pendiente.
        var masAntiguo = await abiertos.Where(c => c.WriteBackIntentos == 0).OrderBy(c => c.UltimaLecturaAt)
            .Select(c => (DateTimeOffset?)c.UltimaLecturaAt).FirstOrDefaultAsync(ct);

        var lista = await controles
            .OrderBy(c => c.WriteBackPendiente && c.WriteBackIntentos >= max ? 0 : c.WriteBackPendiente ? 1 : 2)
            .ThenByDescending(c => c.UltimaLecturaAt)
            .Take(maxMensajes)
            .Select(c => new { c.Id, c.ClaveNatural, c.WriteBackPendiente, c.WriteBackIntentos, c.WriteBackUltimoError, c.CreatedAt, c.WriteBackAt })
            .ToListAsync(ct);
        var mensajes = lista.Select(c => new MensajeIntegracion(
            c.Id.ToString(), $"Pedido {c.ClaveNatural}",
            !c.WriteBackPendiente ? EstadoMensajeIntegracion.Ok
                : c.WriteBackIntentos >= max ? EstadoMensajeIntegracion.Error
                : c.WriteBackIntentos > 0 ? EstadoMensajeIntegracion.Reintentando
                : EstadoMensajeIntegracion.Pendiente,
            c.WriteBackIntentos, c.WriteBackUltimoError, c.CreatedAt, c.WriteBackAt)).ToList();
        return new ResumenFuenteIntegracion(pendientes, reintentando, enError, ok, ultimoExito, masAntiguo, mensajes);
    }

    public async Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(mensajeId, out var id)) return false;
        var control = await db.IngestaControles.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (control is null || !control.ReiniciarIntentosWriteBack()) return false;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
