using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>Puerto del aviso al jefe de Almacén (adaptador de correo en Infrastructure).</summary>
public interface IAvisoMensajesSinAcuse
{
    Task AvisarAsync(int cantidad, TimeSpan masAntiguo, CancellationToken ct);
}

/// <summary>
/// M-283: si hay mensajes de <c>alm_mensaje</c> sin acuse más allá de <see cref="AlmMensajeOptions.AlarmaSinAcuseMinutos"/>,
/// avisa al jefe de Almacén (<c>Almacen:Avisos:JefeAlmacen</c>) y vuelve a avisar solo cada
/// <see cref="AlmMensajeOptions.AlarmaReavisoMinutos"/> mientras siga igual. Al quedar en cero se rearma.
/// </summary>
public sealed class AlmMensajeAlarma(
    IAlmMensajeOrigen origen, IAvisoMensajesSinAcuse aviso, IOptions<AlmMensajeOptions> options, ILogger<AlmMensajeAlarma> logger,
    TimeProvider? reloj = null)
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private DateTimeOffset? _ultimoAviso;

    /// <summary>Devuelve cuántos mensajes están fuera de plazo (0 si la alarma está apagada).</summary>
    public async Task<int> EvaluarAsync(CancellationToken ct)
    {
        var minutos = options.Value.AlarmaSinAcuseMinutos;
        if (minutos <= 0) return 0;
        var r = await origen.ContarSinAcuseAntiguosAsync(minutos * 60, ct);
        if (r.Cantidad == 0) { _ultimoAviso = null; return 0; }
        var ahora = _reloj.GetUtcNow();
        var reaviso = TimeSpan.FromMinutes(Math.Max(1, options.Value.AlarmaReavisoMinutos));
        if (_ultimoAviso is { } ultimo && ahora - ultimo < reaviso) return r.Cantidad;
        try
        {
            await aviso.AvisarAsync(r.Cantidad, TimeSpan.FromSeconds(r.MasAntiguoSegundos), ct);
            _ultimoAviso = ahora;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Un fallo del correo no debe frenar la lectura; se reintenta en el siguiente ciclo.
            logger.LogError(ex, "[AlmMensajeAlarma] No se pudo avisar de {Cantidad} mensaje(s) sin acuse.", r.Cantidad);
        }
        return r.Cantidad;
    }
}
