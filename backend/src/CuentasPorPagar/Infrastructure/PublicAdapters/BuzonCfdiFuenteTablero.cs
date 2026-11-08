using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.CuentasPorPagar.Infrastructure.Mailbox;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.Workers;
using Millet.SharedKernel.Application.Integration.Tablero;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;

/// <summary>
/// Revisión del buzón de CFDI de proveedores (P8.1) en el tablero de integraciones
/// (I13.6). El buzón no tiene cola por mensaje: el correo que falla se queda en la
/// bandeja y se vuelve a leer en la siguiente corrida. Por eso se muestra la
/// bitácora de corridas:
/// <list type="bullet">
///   <item>Corridas fallidas seguidas: «reintentando», con el número de fallas
///   (a las 5 se abre la alerta de reintentos altos).</item>
///   <item>Flujo detenido: no ha corrido en tres intervalos con el buzón activo.</item>
///   <item>Ok: corridas sin error en las últimas 24 h.</item>
/// </list>
/// Los CFDI que quedaron «a revisión» son trabajo de Cuentas por pagar, no fallas de
/// la integración, y se atienden en la bandeja del buzón.
/// </summary>
public sealed class BuzonCfdiFuenteTablero(
    CuentasPorPagarDbContext db,
    IOptionsMonitor<WorkerSchedulingOptions> programacion,
    IOptionsMonitor<MailboxOptions> buzon) : IFuenteTableroIntegracion
{
    /// <summary>Corridas que se muestran como mensajes, además del resumen de fallas seguidas.</summary>
    private const int CorridasEnLista = 20;

    public string Clave => "cxp.buzon-cfdi";
    public string Nombre => "Buzón de CFDI de proveedores";
    public string Grupo => "Correo";
    public bool PermiteReintento => false;

    public async Task<ResumenFuenteIntegracion> LeerAsync(DateTimeOffset ahora, int maxMensajes, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var activo = !programacion.CurrentValue.Disabled && buzon.CurrentValue.IsActivo;
        var corridas = db.BuzonEjecuciones.AsNoTracking();
        var desde = ahora.AddHours(-24);

        var ok = await corridas.CountAsync(c => c.Error == null && c.InicioUtc >= desde, ct);
        var ultimoExito = await corridas.Where(c => c.Error == null).MaxAsync(c => (DateTimeOffset?)c.FinUtc, ct);
        var recientes = await corridas.OrderByDescending(c => c.InicioUtc)
            .Take(Math.Min(maxMensajes, CorridasEnLista)).ToListAsync(ct);

        var fallidasSeguidas = recientes.TakeWhile(c => c.Error != null).ToList();
        var mensajes = new List<MensajeIntegracion>();
        if (fallidasSeguidas.Count > 0)
            mensajes.Add(new MensajeIntegracion(
                "fallas-seguidas",
                $"{fallidasSeguidas.Count} corrida(s) seguidas sin poder leer el buzón",
                EstadoMensajeIntegracion.Reintentando,
                fallidasSeguidas.Count,
                fallidasSeguidas[0].Error,
                fallidasSeguidas[^1].InicioUtc,
                null));
        mensajes.AddRange(recientes.Select(c => new MensajeIntegracion(
            c.Id.ToString(),
            $"Corrida: {c.Mensajes} correo(s), {c.Ingresados} ingresado(s), {c.Duplicados} duplicado(s), {c.ARevision} a revisión",
            c.Error is null ? EstadoMensajeIntegracion.Ok : EstadoMensajeIntegracion.Error,
            0,
            c.Error,
            c.InicioUtc,
            c.Error is null ? c.FinUtc : null)));

        // Sin corrida en tres intervalos con el buzón activo: la siguiente se esperaba un intervalo después de la última.
        var intervalo = TimeSpan.FromSeconds(Math.Max(60, programacion.CurrentValue.MailboxIntervalSeconds));
        DateTimeOffset? esperadaDesde = activo && recientes.Count > 0 && ahora - recientes[0].InicioUtc > 3 * intervalo
            ? recientes[0].InicioUtc + intervalo
            : null;

        return new ResumenFuenteIntegracion(
            esperadaDesde is null ? 0 : 1,
            fallidasSeguidas.Count > 0 ? 1 : 0,
            0, ok, ultimoExito, esperadaDesde, mensajes);
    }

    public Task<bool> ReintentarAsync(string mensajeId, CancellationToken cancellationToken) => Task.FromResult(false);
}
