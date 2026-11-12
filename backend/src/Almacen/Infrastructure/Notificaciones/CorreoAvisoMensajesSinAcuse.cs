using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.SharedKernel.Application.Correo;

namespace Millet.Almacen.Infrastructure.Notificaciones;

/// <summary>
/// M-283: aviso al jefe de Almacén (<c>Almacen:Avisos:JefeAlmacen</c>) de que hay mensajes de la app sin acuse. Sin correos
/// configurados el aviso no sale y queda en el log (como los demás avisos de Almacén).
/// </summary>
public sealed class CorreoAvisoMensajesSinAcuse(
    ICorreoSaliente correo, IOptions<AvisosAlmacenOptions> options, ILogger<CorreoAvisoMensajesSinAcuse> logger) : IAvisoMensajesSinAcuse
{
    public async Task AvisarAsync(int cantidad, TimeSpan masAntiguo, CancellationToken ct)
    {
        var para = AvisosAlmacenOptions.Validos(options.Value.JefeAlmacen);
        if (para.Count == 0)
        {
            logger.LogWarning("[AvisosAlmacen] Sin correos en {Seccion}:JefeAlmacen; el aviso de {Cantidad} mensaje(s) sin acuse no se envió.",
                AvisosAlmacenOptions.SectionName, cantidad);
            return;
        }
        var minutos = (int)Math.Max(1, Math.Round(masAntiguo.TotalMinutes));
        var html = "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
                   "<h2>Mensajes de la app de Almacenes sin acuse</h2>" +
                   $"<p>Hay <strong>{cantidad}</strong> mensaje(s) de la app de Almacenes que el ERP no ha acusado; " +
                   $"el más antiguo lleva {minutos} minuto(s).</p>" +
                   "<p>Revísalos en el ERP, Administración › Integraciones › Tablero.</p></div>";
        await correo.EnviarAsync(new MensajeCorreo(null, CorreoNotificacionService.Modulo, "mensajes-sin-acuse", null, para,
            "Mensajes de la app de Almacenes sin acuse", html), ct);
    }
}
