using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Application.Cambios;

public sealed record AwCambiosCicloResumen(int Upserts, int Eliminados, bool BarridoCompleto);

/// <summary>
/// Un ciclo de sincronización por CDC para una entidad: lee los cambios desde el watermark, relee cada referencia
/// con el sincronizador existente (nunca aplica un payload de CDC) y avanza el watermark solo tras el lote completo.
/// Sin watermark, o con LSN expirado, hace un barrido completo: el LSN se toma ANTES del barrido, así lo que cambie
/// durante él llega por CDC después. Un fallo transitorio propaga la excepción y el watermark no avanza.
/// </summary>
public sealed class AwCambiosAplicador(
    IAwCambiosOrigen origen, IntegracionesAwDbContext db, IServiceProvider sp,
    IOptions<AwCambiosOptions> options, TimeProvider time, ILogger<AwCambiosAplicador> logger)
{
    private const string Actor = "system:aw-cdc";

    public async Task<AwCambiosCicloResumen> CicloAsync(AwEntidadCambio entidad, CancellationToken ct)
    {
        var clave = entidad.ToString();
        var wm = await db.CdcWatermarks.SingleOrDefaultAsync(w => w.Entidad == clave, ct);
        if (wm is null) return await BarridoAsync(entidad, null, ct);

        int upserts = 0, eliminados = 0;
        while (true)
        {
            AwCambiosLote lote;
            try
            {
                lote = await origen.LeerCambiosAsync(entidad, wm.Lsn, options.Value.TamanoLote, ct);
            }
            catch (AwReaderException ex) when (ex.Kind == "cdc_lsn_expirado")
            {
                logger.LogWarning("CDC {Entidad}: LSN expirado; barrido completo de recuperación.", clave);
                return await BarridoAsync(entidad, wm, ct);
            }

            foreach (var cambio in lote.Cambios)
            {
                ct.ThrowIfCancellationRequested();
                if (cambio.Tipo == AwTipoCambio.Eliminado)
                {
                    // Ausencia != baja (doc 05 §8): solo se informa; la política de baja la define Millet.
                    eliminados++;
                    logger.LogWarning("CDC {Entidad} {Referencia}: borrado físico en A+W; no se da de baja.", clave, cambio.Referencia);
                    continue;
                }
                await AplicarAsync(entidad, cambio.Referencia, ct);
                upserts++;
            }

            wm.Avanzar(lote.SiguienteLsn, time.GetUtcNow());
            await db.SaveChangesAsync(ct);
            if (lote.Completo) return new(upserts, eliminados, false);
        }
    }

    private async Task AplicarAsync(AwEntidadCambio entidad, string referencia, CancellationToken ct)
    {
        if (entidad == AwEntidadCambio.Producto)
        {
            await sp.GetRequiredService<AwProductosSincronizador>().SincronizarReferenciaAsync(referencia, ct);
            return;
        }
        var id = await sp.GetRequiredService<AwClientesSincronizador>().ReintentarReferenciaAsync(referencia, Actor, ct);
        // ReintentarReferenciaAsync registra el fallo de lectura en la ejecución en vez de lanzarlo: aquí se vuelve
        // excepción para que el watermark no avance sobre un cambio que no se pudo leer.
        if (await db.ClientesEjecuciones.AsNoTracking().AnyAsync(e => e.Id == id && e.Estado == AwClientesEjecucionEstado.Fallida, ct))
            throw new AwReaderException($"No se pudo leer el cliente {referencia} del origen.", kind: "connection", isTransient: true);
    }

    private async Task<AwCambiosCicloResumen> BarridoAsync(AwEntidadCambio entidad, AwCdcWatermark? wm, CancellationToken ct)
    {
        var lsn = await origen.ObtenerLsnActualAsync(ct);
        if (entidad == AwEntidadCambio.Producto)
            await sp.GetRequiredService<AwProductosSincronizador>().SincronizarBarridoAsync(ct);
        else
            try { await sp.GetRequiredService<AwClientesSincronizador>().IniciarBarridoAsync(Actor, ct); }
            catch (AwClientesSyncException ex) when (ex.Code == "barrido_en_curso")
            {
                return new(0, 0, false); // hay un barrido vivo que podría no cubrir lo posterior a este LSN: se decide en el próximo ciclo
            }

        if (wm is null) db.CdcWatermarks.Add(wm = AwCdcWatermark.Crear(entidad.ToString(), lsn, time.GetUtcNow()));
        else wm.Avanzar(lsn, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return new(0, 0, true);
    }
}
