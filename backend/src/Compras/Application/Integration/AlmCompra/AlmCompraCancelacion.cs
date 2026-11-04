using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MediatR;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Application.Integration.AlmCompra;

/// <summary>
/// Aplica a la OC la respuesta de Almacenes a COMPRA_CANCELADA (86e3jvu83). Jorge 29-oct y 1-nov: la cancelación solo se acepta
/// mientras no haya entrada cerrada; si la hay, ERROR y lo sobrante sale como SALIDA_MANUAL desde la app. El ERP no ve las entradas
/// de la app, así que decide por el acuse. Todo en la misma transacción que el acuse: el llamador guarda.
/// </summary>
public interface IAlmCompraCancelacion
{
    Task<IReadOnlyList<object>> FinalizarAsync(Guid ocId, CancellationToken ct);
    Task RevertirAsync(Guid ocId, string? errorAlmacenes, CancellationToken ct);
}

public sealed class AlmCompraCancelacion(ComprasDbContext db, IClock clock, IPublisher publisher, ILogger<AlmCompraCancelacion> logger) : IAlmCompraCancelacion
{
    public const string MotivoEntrada = "ya tiene entrada en Almacenes; lo sobrante sale como salida manual";

    /// <summary>PROCESADO/DUPLICADO: cancela la OC y libera las RQ con los mismos eventos de siempre. Devuelve los eventos para publicar después de guardar.</summary>
    public async Task<IReadOnlyList<object>> FinalizarAsync(Guid ocId, CancellationToken ct)
    {
        var oc = await Cargar(ocId, ct);
        if (oc is null || !oc.CancelacionEsperaAlmacenes)
        {
            logger.LogWarning("[AlmCompra] Acuse de cancelación para la OC {OcId}, que ya no espera a Almacenes; no se cambia nada.", ocId);
            return [];
        }
        var dobleFirma = oc.CancelacionAlmacenesEsDeDobleFirma;
        var s = oc.SolicitudesCancelacion.Single(x => x.EsperaAlmacenes);
        var ahora = clock.UtcNow;
        var resultado = oc.FinalizarCancelacionAlmacenes(ahora);
        var rqIds = (dobleFirma
            ? resultado.LiberacionesParciales.Select(l => l.RequisicionId)
            : oc.Lineas.Where(l => l.RequisicionId.HasValue).Select(l => l.RequisicionId!.Value)).Distinct().ToArray();
        var rqs = await db.Requisiciones.Where(r => rqIds.Contains(r.Id)).ToListAsync(ct);
        foreach (var rq in dobleFirma ? rqs.Where(r => r.ComprometidaEnOcId == oc.Id) : rqs) rq.LiberarDeOc();
        await Cotizaciones.CotizacionesComparativo.LiberarPorOcAsync(db, oc.Id, s.SolicitanteId, ahora,
            $"OC cancelada: {s.MotivoSolicitud}", ct);
        await publisher.Publish(resultado.EventoCancelada, ct);
        return dobleFirma ? [] : rqIds.Select(id => (object)new LineaRqLiberadaEvent(id, oc.Id, oc.EmpresaId, null, ahora)).ToList();
    }

    /// <summary>ERROR: la OC vuelve a su estado previo y el aviso queda en la solicitud (visible en el detalle de la OC).</summary>
    public async Task RevertirAsync(Guid ocId, string? errorAlmacenes, CancellationToken ct)
    {
        var oc = await Cargar(ocId, ct);
        if (oc is null || !oc.CancelacionEsperaAlmacenes)
        {
            logger.LogWarning("[AlmCompra] Rechazo de cancelación para la OC {OcId}, que ya no espera a Almacenes; no se cambia nada.", ocId);
            return;
        }
        oc.RevertirCancelacionAlmacenes(clock.UtcNow,
            string.IsNullOrWhiteSpace(errorAlmacenes) ? MotivoEntrada : $"{MotivoEntrada} ({errorAlmacenes})");
        logger.LogWarning("[AlmCompra] Almacenes rechazó la cancelación de la OC {Folio}: {Motivo}", oc.Folio.Valor, MotivoEntrada);
    }

    private Task<Domain.Oc.OrdenCompra?> Cargar(Guid ocId, CancellationToken ct) =>
        db.OrdenesCompra.Include(o => o.Lineas).Include(o => o.SolicitudesCancelacion).FirstOrDefaultAsync(o => o.Id == ocId, ct);
}
