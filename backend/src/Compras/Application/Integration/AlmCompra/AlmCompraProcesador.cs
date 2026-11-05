using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MediatR;
using Millet.Compras.Domain.AlmCompra;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.AlmCompra;

public sealed record AlmCompraTick(int Escritos, int Acusados, int Fallas);

/// <summary>
/// Un ciclo del worker de salida a Almacenes. Por referencia (folio de la OC) y en orden de secuencia:
/// <list type="bullet">
/// <item>el primer mensaje sin terminar manda: nada posterior se escribe hasta que tenga acuse PROCESADO/DUPLICADO;</item>
/// <item>ERROR (acuse, error local o reintentos agotados) detiene la referencia; un ERROR con acuse se vuelve a leer porque
/// Almacenes puede reprocesarlo desde su aplicación y cambiar el acuse;</item>
/// <item>una falla del transporte reintenta con espera creciente y, agotados los intentos, pasa a ERROR.</item>
/// </list>
/// El llamador pone el contexto de empresa (el worker abre el bypass; no es un endpoint). Escribir es idempotente por <c>mensajeId</c>, así que dos instancias o un ciclo repetido no duplican.
/// </summary>
public sealed class AlmCompraProcesador(
    ComprasDbContext db, IAlmCompraTransporte transporte, IOptions<AlmCompraOptions> options, IClock clock,
    IAlmCompraCancelacion cancelacion, IPublisher publisher, ILogger<AlmCompraProcesador> logger)
{
    public async Task<AlmCompraTick> ProcesarAsync(CancellationToken ct)
    {
        int escritos = 0, acusados = 0, fallas = 0;

        var referencias = await db.AlmCompraEnvios.AsNoTracking()
            .Where(e => e.Estado != AlmCompraEnvio.Procesado && e.Estado != AlmCompraEnvio.Duplicado && e.Estado != AlmCompraEnvio.Rechazado)
            .Select(e => e.Referencia).Distinct().ToListAsync(ct);

        foreach (var referencia in referencias)
        {
            var filas = await db.AlmCompraEnvios
                .Where(e => e.Referencia == referencia && e.Estado != AlmCompraEnvio.Procesado && e.Estado != AlmCompraEnvio.Duplicado && e.Estado != AlmCompraEnvio.Rechazado)
                .OrderBy(e => e.Secuencia).ToListAsync(ct);

            foreach (var fila in filas)
            {
                try
                {
                    var post = new List<object>();
                    var sigue = await AvanzarAsync(fila, post, ct);
                    escritos += sigue.Escrito ? 1 : 0; acusados += sigue.Acusado ? 1 : 0; fallas += sigue.Falla ? 1 : 0;
                    await db.SaveChangesAsync(ct);
                    foreach (var evento in post) await publisher.Publish(evento, ct);
                    if (!sigue.Continuar) break;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Otra instancia avanzó esta fila: se retoma en el siguiente ciclo.
                    db.ChangeTracker.Clear();
                    break;
                }
            }
            db.ChangeTracker.Clear();
        }
        return new AlmCompraTick(escritos, acusados, fallas);
    }

    private async Task<(bool Continuar, bool Escrito, bool Acusado, bool Falla)> AvanzarAsync(AlmCompraEnvio fila, List<object> post, CancellationToken ct)
    {
        var ahora = clock.UtcNow;
        var escrito = false;
        if (fila.Estado == AlmCompraEnvio.Pendiente)
        {
            if (fila.ProximoIntentoEn > ahora) return (false, false, false, false);
            try
            {
                await transporte.EscribirAsync(
                    new AlmCompraMensaje(fila.MensajeId, fila.Tipo, AlmCompraEnvio.VersionContrato, ahora, fila.Referencia, fila.Sobre), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Solo el tipo y el mensaje corto de la excepción: nunca el sobre.
                fila.RegistrarFalla($"{ex.GetType().Name}: {ex.Message}", ahora, options.Value.MaxIntentos);
                logger.LogWarning("[AlmCompra] Falla al escribir {MensajeId} (intento {Intentos}): {Tipo}",
                    fila.MensajeId, fila.Intentos, ex.GetType().Name);
                return (false, false, false, true);
            }
            fila.MarcarEscrito(ahora);
            escrito = true;
        }
        else if (fila.Estado == AlmCompraEnvio.Error && fila.EnviadoEn is null)
        {
            return (false, false, false, false); // error local o reintentos agotados: se reintenta a mano
        }

        // ENVIADO, o ERROR con acuse (Almacenes pudo reprocesarlo): leer el acuse.
        AlmCompraAcuse? acuse;
        try { acuse = await transporte.LeerAcuseAsync(fila.MensajeId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("[AlmCompra] Falla al leer el acuse de {MensajeId}: {Tipo}", fila.MensajeId, ex.GetType().Name);
            return (false, escrito, false, true);
        }
        if (acuse is null) return (false, escrito, false, false); // Almacenes aún no responde
        if (acuse.Estado is not (AlmCompraEnvio.Procesado or AlmCompraEnvio.Duplicado or AlmCompraEnvio.Error))
        {
            fila.AplicarAcuse(AlmCompraEnvio.Error, null, $"Acuse con estado desconocido '{acuse.Estado}'.", ahora);
            return (false, escrito, true, false);
        }
        // Almacenes rechaza una cancelación porque ya hay entrada: no es una falla de la compra, el ERP revierte la OC y la
        // referencia sigue (RECHAZADO). Cualquier otro ERROR sí detiene la referencia.
        var estadoFinal = fila.Tipo == AlmCompraEnvio.TipoCancelada && acuse.Estado == AlmCompraEnvio.Error ? AlmCompraEnvio.Rechazado : acuse.Estado;
        if (fila.Estado == estadoFinal) return (false, escrito, false, false); // ERROR que sigue en ERROR: nada que guardar
        fila.AplicarAcuse(estadoFinal, acuse.CompraId, acuse.Error, ahora);
        if (fila.Tipo == AlmCompraEnvio.TipoCancelada)
        {
            if (estadoFinal == AlmCompraEnvio.Rechazado) await cancelacion.RevertirAsync(fila.OrdenCompraId, acuse.Error, ct);
            else post.AddRange(await cancelacion.FinalizarAsync(fila.OrdenCompraId, ct));
        }
        return (fila.Terminado, escrito, true, false);
    }
}
