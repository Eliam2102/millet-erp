using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.EventListeners;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Pedidos.CancelarSalidaFacturable;

/// <summary>
/// FAC-08 Fase 2: Producto terminado canceló la salida de un albarán ya emitido (<c>Referencia</c> = folio del albarán).
/// Misma regla que el contenedor cancelado de la Fase 1 (<see cref="CancelacionSalidas"/>): sin factura vigente la salida se
/// descarta y lo embarcado vuelve a quedar pendiente; con factura vigente se conserva y va a revisión. Idempotente.
/// </summary>
public sealed record CancelarSalidaProductoTerminadoCommand(string Referencia, string Motivo) : IRequest<CancelacionSalidaResponse>;

/// <summary><c>Registrada</c> = había salida con esa referencia; <c>Descartada</c> y <c>EnRevision</c> dicen cómo quedó.</summary>
public sealed record CancelacionSalidaResponse(bool Registrada, bool Descartada, bool EnRevision);

public sealed class CancelarSalidaProductoTerminadoHandler(FacturacionDbContext db, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<CancelarSalidaProductoTerminadoCommand, CancelacionSalidaResponse>
{
    public async Task<CancelacionSalidaResponse> Handle(CancelarSalidaProductoTerminadoCommand command, CancellationToken cancellationToken)
    {
        var empresaId = empresa.Current ?? throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");
        var referencia = (command.Referencia ?? "").Trim();
        if (referencia.Length == 0)
            throw new BusinessRuleException("SALIDA_REFERENCIA_REQUERIDA", "Falta el folio del albarán de la salida a cancelar.");

        var salida = await db.SalidasFacturables
            .FirstOrDefaultAsync(s => s.EmpresaId == empresaId && s.Origen == OrigenSalidaFacturable.ProductoTerminado
                && s.OrigenId == null && s.Referencia == referencia, cancellationToken);
        // Nada que cancelar: el albarán no llegó a registrar salida (p. ej. falló antes de emitir).
        if (salida is null) return new CancelacionSalidaResponse(false, false, false);

        var motivo = $"Albarán {referencia} cancelado en Producto terminado: {command.Motivo}".Trim().TrimEnd(':');
        var (_, revision) = await CancelacionSalidas.AplicarAsync(db, empresaId, [salida], motivo, clock.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new CancelacionSalidaResponse(true, salida.Descartada, revision > 0);
    }
}
