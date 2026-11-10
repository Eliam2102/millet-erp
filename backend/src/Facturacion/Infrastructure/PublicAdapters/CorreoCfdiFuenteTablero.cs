using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Envios;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>
/// Envío del CFDI por correo al cliente en el tablero de integraciones (I13.6).
/// El worker reintenta hasta <see cref="EnvioCfdiCorreoOptions.MaxIntentos"/>;
/// después queda en error y el reintento manual lo reencola (un solo correo).
/// </summary>
public sealed class CorreoCfdiFuenteTablero(FacturacionDbContext db, IOptionsMonitor<EnvioCfdiCorreoOptions> opciones)
    : IFuenteTableroIntegracion
{
    public string Clave => "facturacion.correo-cfdi";
    public string Nombre => "Envío de CFDI por correo a clientes";
    public string Grupo => "Correo";
    public bool PermiteReintento => true;

    public async Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var max = opciones.CurrentValue.MaxIntentos;
        var desde = ahora.AddHours(-24);
        var envios = db.BitacorasEnvioCorreo.AsNoTracking();
        var abiertos = envios.Where(b => b.Estado == EstadoEnvioCorreo.Pendiente
            || (b.Estado == EstadoEnvioCorreo.Fallido && b.Intentos < max));

        var pendientes = await abiertos.CountAsync(b => b.Estado == EstadoEnvioCorreo.Pendiente, ct);
        var reintentando = await abiertos.CountAsync(b => b.Estado == EstadoEnvioCorreo.Fallido, ct);
        var enError = await envios.CountAsync(b => b.Estado == EstadoEnvioCorreo.Fallido && b.Intentos >= max, ct);
        var ok = await envios.CountAsync(b => b.EnviadoAt >= desde, ct);
        var ultimoExito = await envios.Where(b => b.EnviadoAt != null).MaxAsync(b => b.EnviadoAt, ct);
        var masAntiguo = await abiertos.OrderBy(b => b.CreatedAt).Select(b => (DateTimeOffset?)b.CreatedAt).FirstOrDefaultAsync(ct);

        var lista = await (from b in envios
                           join c in db.Comprobantes.AsNoTracking() on b.ComprobanteId equals c.Id into cs
                           from c in cs.DefaultIfEmpty()
                           orderby (b.Estado == EstadoEnvioCorreo.Fallido && b.Intentos >= max ? 0
                               : b.Estado == EstadoEnvioCorreo.Enviado ? 2 : 1), b.CreatedAt descending
                           select new { b.Id, b.Estado, b.Intentos, b.UltimoError, b.CreatedAt, b.EnviadoAt, Folio = c == null ? null : c.Folio })
            .Take(maxMensajes).ToListAsync(ct);
        var mensajes = lista.Select(b => new MensajeIntegracion(
            b.Id.ToString(), b.Folio ?? "Comprobante sin folio", Estado(b.Estado, b.Intentos, max), b.Intentos, b.UltimoError,
            b.CreatedAt, b.EnviadoAt)).ToList();
        return new ResumenFuenteIntegracion(pendientes, reintentando, enError, ok, ultimoExito, masAntiguo, mensajes);
    }

    public async Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(mensajeId, out var id)) return false;
        var envio = await db.BitacorasEnvioCorreo.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (envio is null || envio.Estado == EstadoEnvioCorreo.Enviado) return false;
        envio.Reencolar();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static EstadoMensajeIntegracion Estado(EstadoEnvioCorreo estado, int intentos, int max) => estado switch
    {
        EstadoEnvioCorreo.Enviado => EstadoMensajeIntegracion.Ok,
        EstadoEnvioCorreo.Pendiente => intentos > 0 ? EstadoMensajeIntegracion.Reintentando : EstadoMensajeIntegracion.Pendiente,
        _ => intentos >= max ? EstadoMensajeIntegracion.Error : EstadoMensajeIntegracion.Reintentando,
    };
}
