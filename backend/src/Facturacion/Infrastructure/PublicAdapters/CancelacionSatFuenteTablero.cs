using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>
/// Cancelaciones de CFDI ante el SAT en el tablero de integraciones (I13.6).
/// <list type="bullet">
///   <item>Pendientes: solicitudes que el PAC no ha confirmado (Solicitada) o que
///   esperan la respuesta del SAT o del receptor (EnProceso).</item>
///   <item>Flujo detenido: solo cuenta lo que no ha salido al PAC (Solicitada). La
///   espera del receptor es de hasta 3 días hábiles y no es una falla.</item>
///   <item>Error: rechazos del SAT o del receptor de las últimas 24 h (el CFDI
///   sigue vigente y hay que atenderlo).</item>
///   <item>Ok: aceptadas y aceptaciones tácitas (vencidas).</item>
/// </list>
/// No se reintenta desde aquí: una cancelación rechazada se vuelve a pedir en su bandeja.
/// </summary>
public sealed class CancelacionSatFuenteTablero(FacturacionDbContext db) : IFuenteTableroIntegracion
{
    public string Clave => "facturacion.cancelacion-sat";
    public string Nombre => "Cancelaciones de CFDI ante el SAT";
    public string Grupo => "PAC y SAT";
    public bool PermiteReintento => false;

    public async Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var desde = ahora.AddHours(-24);
        var solicitudes = db.SolicitudesCancelacion.AsNoTracking();
        var abiertas = solicitudes.Where(s => s.Estado == EstadoSolicitudCancelacion.Solicitada || s.Estado == EstadoSolicitudCancelacion.EnProceso);
        var aceptadas = solicitudes.Where(s => s.Estado == EstadoSolicitudCancelacion.Aceptada || s.Estado == EstadoSolicitudCancelacion.Vencida);
        var rechazadas = solicitudes.Where(s => s.Estado == EstadoSolicitudCancelacion.Rechazada && s.ResueltaEn >= desde);

        var pendientes = await abiertas.CountAsync(ct);
        var enError = await rechazadas.CountAsync(ct);
        var ok = await aceptadas.CountAsync(s => s.ResueltaEn >= desde, ct);
        var ultimoExito = await aceptadas.Where(s => s.ResueltaEn != null).MaxAsync(s => s.ResueltaEn, ct);
        var sinSalir = await solicitudes.Where(s => s.Estado == EstadoSolicitudCancelacion.Solicitada)
            .OrderBy(s => s.SolicitadaEn).Select(s => (DateTimeOffset?)s.SolicitadaEn).FirstOrDefaultAsync(ct);

        var lista = await (from s in solicitudes
                           join c in db.Comprobantes.AsNoTracking() on s.ComprobanteId equals c.Id into cs
                           from c in cs.DefaultIfEmpty()
                           where s.Estado == EstadoSolicitudCancelacion.Solicitada || s.Estado == EstadoSolicitudCancelacion.EnProceso
                               || (s.ResueltaEn >= desde && (s.Estado == EstadoSolicitudCancelacion.Rechazada
                                   || s.Estado == EstadoSolicitudCancelacion.Aceptada || s.Estado == EstadoSolicitudCancelacion.Vencida))
                           orderby (s.Estado == EstadoSolicitudCancelacion.Rechazada ? 0
                               : s.Estado == EstadoSolicitudCancelacion.Solicitada || s.Estado == EstadoSolicitudCancelacion.EnProceso ? 1 : 2),
                               s.SolicitadaEn descending
                           select new { s.Id, s.Estado, s.EstatusSat, s.MensajeError, s.MotivoSat, s.SolicitadaEn, s.ResueltaEn, Folio = c == null ? null : c.Folio })
            .Take(maxMensajes).ToListAsync(ct);
        var mensajes = lista.Select(s => new MensajeIntegracion(
            s.Id.ToString(),
            $"{(string.IsNullOrWhiteSpace(s.Folio) ? "Comprobante sin folio" : $"Folio {s.Folio}")} · motivo {s.MotivoSat}",
            s.Estado switch
            {
                EstadoSolicitudCancelacion.Rechazada => EstadoMensajeIntegracion.Error,
                EstadoSolicitudCancelacion.Solicitada or EstadoSolicitudCancelacion.EnProceso => EstadoMensajeIntegracion.Pendiente,
                _ => EstadoMensajeIntegracion.Ok,
            },
            0,
            s.Estado == EstadoSolicitudCancelacion.Rechazada ? Recortar(s.MensajeError ?? s.EstatusSat ?? "Rechazada") : null,
            s.SolicitadaEn,
            s.ResueltaEn)).ToList();
        return new ResumenFuenteIntegracion(pendientes, 0, enError, ok, ultimoExito, sinSalir, mensajes);
    }

    public Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken) => Task.FromResult(false);

    private static string Recortar(string texto) => texto.Length <= 500 ? texto : texto[..500];
}
