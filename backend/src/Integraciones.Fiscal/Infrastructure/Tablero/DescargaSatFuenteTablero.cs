using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.Integraciones.Fiscal.Infrastructure.Tablero;

/// <summary>
/// Descarga masiva de CFDI del SAT (P8.2, vía FiscalAPI) en el tablero de
/// integraciones (I13.6). Cada solicitud de descarga es un mensaje:
/// <list type="bullet">
///   <item>Abierta (pendiente o esperando al SAT o a FiscalAPI): «pendiente». Las consultas
///   del poller (con espera creciente) no son fallas, así que no cuentan como reintentos.</item>
///   <item>Flujo detenido: una solicitud cuya siguiente consulta ya venció y el poller no la
///   tomó (el SAT tarda horas en responder, así que esperar no es una falla).</item>
///   <item>Error: solicitudes que terminaron en error en las últimas 24 h.</item>
///   <item>Ok: cosechadas (CFDI entregados a Cuentas por pagar).</item>
/// </list>
/// Mientras no haya FIEL los workers están apagados y no hay solicitudes.
/// No se reintenta desde aquí: la siguiente corrida del submitter pide el periodo de nuevo.
/// </summary>
public sealed class DescargaSatFuenteTablero(IntegracionesFiscalDbContext db) : IFuenteTableroIntegracion
{
    private static readonly EstadoSolicitudDescarga[] Abiertas =
        [EstadoSolicitudDescarga.Pendiente, EstadoSolicitudDescarga.EsperandoSat, EstadoSolicitudDescarga.EsperandoApi, EstadoSolicitudDescarga.Terminada];

    public string Clave => "fiscal.descarga-sat";
    public string Nombre => "Descarga masiva de CFDI del SAT";
    public string Grupo => "PAC y SAT";
    public bool PermiteReintento => false;

    public async Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var desde = ahora.AddHours(-24);
        var solicitudes = db.SolicitudesDescarga.AsNoTracking();
        var abiertas = solicitudes.Where(s => Abiertas.Contains(s.Estado));
        var errores = solicitudes.Where(s => s.Estado == EstadoSolicitudDescarga.Error && s.UpdatedAt >= desde);

        var pendientes = await abiertas.CountAsync(ct);
        var enError = await errores.CountAsync(ct);
        var ok = await solicitudes.CountAsync(s => s.CosechadaAt >= desde, ct);
        var ultimoExito = await solicitudes.Where(s => s.CosechadaAt != null).MaxAsync(s => s.CosechadaAt, ct);
        var vencida = await abiertas.Where(s => s.NextPollAt < ahora).OrderBy(s => s.NextPollAt)
            .Select(s => (DateTimeOffset?)s.NextPollAt).FirstOrDefaultAsync(ct);

        var lista = await solicitudes
            .Where(s => Abiertas.Contains(s.Estado) || s.UpdatedAt >= desde)
            .OrderBy(s => s.Estado == EstadoSolicitudDescarga.Error ? 0 : Abiertas.Contains(s.Estado) ? 1 : 2)
            .ThenByDescending(s => s.CreatedAt)
            .Take(maxMensajes)
            .Select(s => new { s.Id, s.StartDate, s.EndDate, s.Estado, s.AttemptsPoll, s.ErrorCodigo, s.ErrorMensaje, s.InvoiceCount, s.CreatedAt, s.CosechadaAt })
            .ToListAsync(ct);
        var mensajes = lista.Select(s => new MensajeIntegracion(
            s.Id.ToString(),
            $"Periodo {s.StartDate:yyyy-MM-dd} a {s.EndDate:yyyy-MM-dd}{(s.InvoiceCount is { } n ? $" · {n} CFDI" : "")}",
            s.Estado == EstadoSolicitudDescarga.Error ? EstadoMensajeIntegracion.Error
                : !Abiertas.Contains(s.Estado) ? EstadoMensajeIntegracion.Ok
                : EstadoMensajeIntegracion.Pendiente,
            s.AttemptsPoll,
            s.Estado == EstadoSolicitudDescarga.Error ? Error(s.ErrorCodigo, s.ErrorMensaje) : null,
            s.CreatedAt,
            s.CosechadaAt)).ToList();
        return new ResumenFuenteIntegracion(pendientes, 0, enError, ok, ultimoExito, vencida, mensajes);
    }

    public Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken) => Task.FromResult(false);

    private static string Error(string? codigo, string? mensaje)
    {
        var texto = string.IsNullOrWhiteSpace(codigo) ? mensaje ?? "Error"
            : string.IsNullOrWhiteSpace(mensaje) ? codigo : $"{codigo}: {mensaje}";
        return texto.Length <= 500 ? texto : texto[..500];
    }
}
