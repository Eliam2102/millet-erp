using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>
/// Timbrado con el PAC en el tablero de integraciones (I13.6). Pendiente es un
/// comprobante en «timbrado en proceso» (esperando respuesta del PAC); error es un
/// timbrado fallido de las últimas 24 h (los más viejos ya se atendieron o se
/// descartaron en la bandeja y no deben dejar la alerta abierta para siempre).
/// No se reintenta desde aquí: un fallo por tiempo de espera puede tener timbre en
/// el SAT y se revisa en la bandeja de timbrado antes de reintentar.
/// </summary>
public sealed class TimbradoPacFuenteTablero(FacturacionDbContext db) : IFuenteTableroIntegracion
{
    public string Clave => "facturacion.timbrado-pac";
    public string Nombre => "Timbrado de CFDI con el PAC";
    public string Grupo => "PAC y SAT";
    public bool PermiteReintento => false;

    public async Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var desde = ahora.AddHours(-24);
        var comprobantes = db.Comprobantes.AsNoTracking();
        var enProceso = comprobantes.Where(c => c.Estado == EstadoTimbrado.TimbradoEnProceso);
        var fallidos = comprobantes.Where(c => c.Estado == EstadoTimbrado.TimbradoFallido && c.UpdatedAt >= desde);

        var pendientes = await enProceso.CountAsync(ct);
        var enError = await fallidos.CountAsync(ct);
        var ok = await comprobantes.CountAsync(c => c.FechaTimbrado >= desde, ct);
        var ultimoExito = await comprobantes.Where(c => c.FechaTimbrado != null).MaxAsync(c => c.FechaTimbrado, ct);
        var masAntiguo = await enProceso.OrderBy(c => c.UpdatedAt).Select(c => (DateTimeOffset?)c.UpdatedAt).FirstOrDefaultAsync(ct);

        var lista = await comprobantes
            .Where(c => c.Estado == EstadoTimbrado.TimbradoEnProceso
                || (c.Estado == EstadoTimbrado.TimbradoFallido && c.UpdatedAt >= desde)
                || c.FechaTimbrado >= desde)
            .OrderBy(c => c.Estado == EstadoTimbrado.TimbradoFallido ? 0 : c.Estado == EstadoTimbrado.TimbradoEnProceso ? 1 : 2)
            .ThenByDescending(c => c.UpdatedAt)
            .Take(maxMensajes)
            .Select(c => new { c.Id, c.Folio, c.Estado, c.TimbradoErrorCodigo, c.TimbradoErrorMensaje, c.CreatedAt, c.FechaTimbrado })
            .ToListAsync(ct);
        var ids = lista.Select(c => c.Id).ToList();
        var intentos = await db.BitacorasIntentoTimbrado.AsNoTracking().Where(b => ids.Contains(b.ComprobanteId))
            .GroupBy(b => b.ComprobanteId).Select(g => new { g.Key, Total = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Total, ct);

        var mensajes = lista.Select(c => new MensajeIntegracion(
            c.Id.ToString(),
            string.IsNullOrWhiteSpace(c.Folio) ? "Comprobante sin folio" : $"Folio {c.Folio}",
            c.Estado switch
            {
                EstadoTimbrado.TimbradoFallido => EstadoMensajeIntegracion.Error,
                EstadoTimbrado.TimbradoEnProceso => EstadoMensajeIntegracion.Pendiente,
                _ => EstadoMensajeIntegracion.Ok,
            },
            intentos.GetValueOrDefault(c.Id),
            c.Estado == EstadoTimbrado.TimbradoFallido ? Error(c.TimbradoErrorCodigo, c.TimbradoErrorMensaje) : null,
            c.CreatedAt,
            c.FechaTimbrado)).ToList();
        return new ResumenFuenteIntegracion(pendientes, 0, enError, ok, ultimoExito, masAntiguo, mensajes);
    }

    public Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken) => Task.FromResult(false);

    private static string? Error(string? codigo, string? mensaje)
    {
        var texto = (codigo, mensaje) switch
        {
            (null or "", null or "") => null,
            (null or "", _) => mensaje,
            (_, null or "") => codigo,
            _ => $"{codigo}: {mensaje}",
        };
        return texto is { Length: > 500 } ? texto[..500] : texto;
    }
}
