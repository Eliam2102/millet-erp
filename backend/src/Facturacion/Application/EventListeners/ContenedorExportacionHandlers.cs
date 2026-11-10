using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Facturacion.Application.Pedidos.RegistrarSalidaFacturable;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Eventos;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.EventListeners;

// Contratos espejo de Comercio Exterior (comercio_exterior.contenedor.*.v1). Facturación solo lee lo que usa.
public sealed record ContenedorEmbarcadoPayload(Guid EmpresaId, Guid ExpedienteId, string? FolioExpediente,
    Guid ContenedorId, string NumeroContenedor, string? BlGuia, DateOnly? FechaEmbarqueReal,
    IReadOnlyList<PedidoContenedorPayload> Pedidos)
{
    public const string EventType = "comercio_exterior.contenedor.embarcado.v1";
    public const string PreparadoEventType = "comercio_exterior.contenedor.preparado.v1";
}

public sealed record ContenedorCanceladoPayload(Guid EmpresaId, Guid ExpedienteId, string? FolioExpediente,
    Guid ContenedorId, string? NumeroContenedor, string? Motivo, IReadOnlyList<PedidoContenedorPayload>? Pedidos)
{
    public const string EventType = "comercio_exterior.contenedor.cancelado.v1";
}

public sealed record PedidoContenedorPayload(Guid PedidoFacturableId, string? NumeroPedido, IReadOnlyList<LineaContenedorPayload> Lineas);
public sealed record LineaContenedorPayload(int Posicion, decimal Cantidad, string? Unidad);

/// <summary>
/// FAC-08 Fase 1: un contenedor embarcado en Comercio Exterior da una salida facturable por pedido
/// (<c>Origen = ContenedorExportacion</c>, referencia = número de contenedor). Reusa el registro de
/// salidas (<see cref="RegistrarSalidaFacturableCommand"/>), idempotente por (contenedor, pedido): una
/// reentrega no crea otra salida ni cambia importes. Un pedido que no admite la salida (no existe, ya se
/// facturó completo, rebasa lo pendiente) queda en la bandeja de excepciones y no detiene a los demás.
/// </summary>
public sealed record RegistrarSalidasContenedorCommand(ContenedorEmbarcadoPayload Payload) : IRequest<int>;

public sealed class RegistrarSalidasContenedorHandler(FacturacionDbContext db, ISender sender, ILogger<RegistrarSalidasContenedorHandler> logger)
    : IRequestHandler<RegistrarSalidasContenedorCommand, int>
{
    public async Task<int> Handle(RegistrarSalidasContenedorCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        if (p.EmpresaId == Guid.Empty || p.ContenedorId == Guid.Empty || string.IsNullOrWhiteSpace(p.NumeroContenedor) || p.Pedidos is null)
            throw new BusinessRuleException("CE_EVENTO_INVALIDO", "El embarque no trae empresa, contenedor o pedidos.");
        var referencia = Referencia(p.NumeroContenedor);
        var nuevas = 0;
        foreach (var pedido in p.Pedidos)
        {
            // Un pedido que se está facturando (bloqueado) se reintenta: el evento vuelve a entregarse.
            var estado = await db.PedidosFacturables.Where(x => x.Id == pedido.PedidoFacturableId)
                .Select(x => (EstadoPedidoFacturable?)x.Estado).FirstOrDefaultAsync(cancellationToken);
            if (estado == EstadoPedidoFacturable.Bloqueado)
                throw new InvalidOperationException($"El pedido {pedido.NumeroPedido} se está facturando; se reintenta la salida del contenedor {referencia}.");
            try
            {
                var r = await sender.Send(new RegistrarSalidaFacturableCommand(pedido.PedidoFacturableId, pedido.NumeroPedido,
                    OrigenSalidaFacturable.ContenedorExportacion, referencia,
                    pedido.Lineas.Select(l => new SalidaFacturableLineaDto(l.Posicion, l.Cantidad)).ToList(), p.ContenedorId), cancellationToken);
                if (!r.YaRegistrada) nuevas++;
            }
            catch (Exception ex) when (ex is BusinessRuleException or EntityNotFoundException or ValidationException or ConflictException)
            {
                logger.LogWarning(ex, "[FAC-08] Salida del contenedor {Contenedor} para el pedido {Pedido} no se registró", referencia, pedido.NumeroPedido);
                await AbrirRevisionAsync(db, p.EmpresaId, pedido, MotivoExcepcion.Otro,
                    $"Salida del contenedor {referencia} (expediente {p.FolioExpediente}) no registrada: {ex.Message}", cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        return nuevas;
    }

    internal static string Referencia(string? numero)
    {
        var r = (numero ?? "").Trim();
        return r.Length > SalidaFacturable.ReferenciaMaxLength ? r[..SalidaFacturable.ReferenciaMaxLength] : r;
    }

    /// <summary>Abre la excepción de revisión del pedido; no la duplica si ya hay una abierta con el mismo detalle.</summary>
    internal static async Task AbrirRevisionAsync(FacturacionDbContext db, Guid empresaId, PedidoContenedorPayload pedido,
        MotivoExcepcion motivo, string detalle, CancellationToken ct)
    {
        var origen = await db.PedidosFacturables.Where(x => x.Id == pedido.PedidoFacturableId)
            .Select(x => new { x.Origen, x.NumeroPedido }).FirstOrDefaultAsync(ct);
        var pedidoRef = origen?.NumeroPedido ?? pedido.NumeroPedido ?? pedido.PedidoFacturableId.ToString();
        if (pedidoRef.Length > 60) pedidoRef = pedidoRef[..60];
        if (detalle.Length > 1000) detalle = detalle[..1000];
        if (await db.ExcepcionesImportacion.AnyAsync(e => e.EmpresaId == empresaId && e.PedidoRef == pedidoRef && !e.Resuelto && e.Detalle == detalle, ct))
            return;
        db.ExcepcionesImportacion.Add(ExcepcionImportacion.Crear(empresaId, origen?.Origen ?? OrigenPedido.Aw, pedidoRef, motivo, detalle));
    }
}

/// <summary>
/// FAC-08 Fase 1: un contenedor embarcado se canceló en Comercio Exterior. Su salida sin factura vigente se
/// descarta (sus cantidades vuelven a quedar pendientes del pedido); la que ya tiene factura vigente pasa a
/// revisión en la bandeja de excepciones, igual que una cancelación de A+W sobre un pedido facturado.
/// Idempotente por contenedor (<see cref="EventoProcesado"/>).
/// </summary>
public sealed record CancelarSalidasContenedorCommand(ContenedorCanceladoPayload Payload) : IRequest;

public sealed class CancelarSalidasContenedorHandler(FacturacionDbContext db, IClock clock)
    : IRequestHandler<CancelarSalidasContenedorCommand>
{
    public async Task Handle(CancelarSalidasContenedorCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        if (p.EmpresaId == Guid.Empty || p.ContenedorId == Guid.Empty)
            throw new BusinessRuleException("CE_EVENTO_INVALIDO", "La cancelación no trae empresa o contenedor.");
        if (await db.EventosProcesados.AnyAsync(e => e.EventoId == p.ContenedorId && e.EventoTipo == ContenedorCanceladoPayload.EventType, cancellationToken))
            return;

        var salidas = await db.SalidasFacturables
            .Where(s => s.EmpresaId == p.EmpresaId && s.Origen == OrigenSalidaFacturable.ContenedorExportacion && s.OrigenId == p.ContenedorId)
            .ToListAsync(cancellationToken);
        var referencia = RegistrarSalidasContenedorHandler.Referencia(p.NumeroContenedor);
        var motivo = $"Contenedor {referencia} cancelado en Comercio Exterior (expediente {p.FolioExpediente}): {p.Motivo}".Trim();
        var (descartadas, revision) = await CancelacionSalidas.AplicarAsync(db, p.EmpresaId, salidas, motivo, clock.UtcNow, cancellationToken);
        db.EventosProcesados.Add(new EventoProcesado(p.ContenedorId, ContenedorCanceladoPayload.EventType, clock.UtcNow,
            $"contenedor {referencia}: {descartadas} salida(s) descartada(s), {revision} a revisión"));
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// FAC-08: regla común al cancelarse el origen de salidas facturables (contenedor de CE, albarán de PT). La salida sin factura
/// vigente se descarta y sus cantidades vuelven a quedar pendientes del pedido; la que ya tiene factura vigente se conserva y
/// pasa a revisión en la bandeja de excepciones (<see cref="MotivoExcepcion.CancelacionSobreFacturado"/>), igual que una
/// cancelación de A+W sobre un pedido facturado. Repetirla no cambia nada: lo descartado se queda así y la revisión no se duplica.
/// No guarda: el que llama hace <c>SaveChangesAsync</c>.
/// </summary>
public static class CancelacionSalidas
{
    public static async Task<(int Descartadas, int Revision)> AplicarAsync(FacturacionDbContext db, Guid empresaId,
        IReadOnlyList<SalidaFacturable> salidas, string motivo, DateTimeOffset ahora, CancellationToken ct)
    {
        var ids = salidas.Select(s => s.Id).ToList();
        var facturas = await db.FacturasVenta
            .Where(f => f.SalidaFacturableId != null && ids.Contains(f.SalidaFacturableId.Value)
                && f.Estado != EstadoTimbrado.Cancelado && f.Estado != EstadoTimbrado.Descartada)
            .Select(f => new { SalidaId = f.SalidaFacturableId!.Value, f.Folio })
            .ToListAsync(ct);
        var descartadas = 0; var revision = 0;
        foreach (var s in salidas)
        {
            var factura = facturas.FirstOrDefault(f => f.SalidaId == s.Id);
            if (factura is null)
            {
                if (!s.Descartada) { s.Descartar(motivo, ahora); descartadas++; }
                continue;
            }
            revision++;
            await RegistrarSalidasContenedorHandler.AbrirRevisionAsync(db, empresaId,
                new PedidoContenedorPayload(s.PedidoFacturableId, null, []), MotivoExcepcion.CancelacionSobreFacturado,
                $"{motivo}. La parcialidad {s.NumeroParcialidad} ya está facturada (folio {factura.Folio}); revise cancelar o sustituir el CFDI.",
                ct);
        }
        return (descartadas, revision);
    }
}
