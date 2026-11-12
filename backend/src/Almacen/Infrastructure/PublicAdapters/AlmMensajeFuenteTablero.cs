using Microsoft.Extensions.Options;
using Millet.Almacen.Application.AppAlmacenes;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

/// <summary>
/// M-283: mensajes de la app de Almacenes (<c>alm_mensaje</c>) sin acuse más allá del plazo de la alarma, en el tablero de
/// integraciones (I13.6). «Pendientes» = cuántos llevan más de <see cref="AlmMensajeOptions.AlarmaSinAcuseMinutos"/> sin acuse.
/// Sin la integración habilitada o sin conexión configurada el tablero la muestra «Sin lectura».
/// </summary>
public sealed class AlmMensajeFuenteTablero(IAlmMensajeOrigen origen, IOptions<AlmMensajeOptions> options) : IFuenteTableroIntegracion
{
    public string Clave => "almacenes.mensajes-sin-acuse";
    public string Nombre => "Mensajes de la app de Almacenes sin acuse";
    public string Grupo => "Almacenes";
    public bool PermiteReintento => false;

    public async Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken)
    {
        if (!options.Value.Habilitado || options.Value.AlarmaSinAcuseMinutos <= 0)
            throw new FuenteTableroSinConexionException("La lectura de alm_mensaje está apagada en este ambiente.");
        var r = await origen.ContarSinAcuseAntiguosAsync(options.Value.AlarmaSinAcuseMinutos * 60, cancellationToken);
        DateTimeOffset? masAntiguo = r.Cantidad == 0 ? null : ahora.AddSeconds(-r.MasAntiguoSegundos);
        return new ResumenFuenteIntegracion(r.Cantidad, 0, 0, 0, null, masAntiguo, []);
    }

    public Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken) => Task.FromResult(false);
}
