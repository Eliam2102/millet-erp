using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Ingesta.ProcesarNotaCreditoAw;

/// <summary>Procesa una nota de crédito de la tabla puente de A+W (86e3wjt5r).</summary>
public sealed record ProcesarNotaCreditoAwCommand(Guid EmpresaId, SolicitudNotaCreditoAw Solicitud)
    : IRequest<ProcesarNotaCreditoAwResponse>;

public sealed record ProcesarNotaCreditoAwResponse(ResultadoSolicitudAw Resultado, Guid? NotaCreditoId, string? Motivo);

/// <summary>
/// La nota nace en A+W; el ERP la deja <see cref="EstadoTimbrado.PorAutorizar"/>, ligada a la factura de origen, y
/// NO la timbra (la autoriza una persona: <c>AutorizarNotaCreditoHandler</c>). Idempotente por número de nota +
/// versión. Si la factura no existe, el cliente es otro o el importe excede el saldo, no crea la nota: abre una
/// excepción en la bandeja de ingesta y rechaza el acuse (A+W corrige con una versión nueva).
/// </summary>
public sealed class ProcesarNotaCreditoAwHandler(
    FacturacionDbContext db,
    IAwNotasCreditoPort puente,
    IClientesReadPort clientes,
    ISaldoFacturaReadPort saldos,
    ISender sender,
    IClock clock)
    : IRequestHandler<ProcesarNotaCreditoAwCommand, ProcesarNotaCreditoAwResponse>
{
    public async Task<ProcesarNotaCreditoAwResponse> Handle(ProcesarNotaCreditoAwCommand command, CancellationToken cancellationToken)
    {
        var sol = command.Solicitud;
        var (resultado, ncId, motivo) = await ProcesarAsync(command.EmpresaId, sol, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        // Si el acuse falla, la nota queda sin acuse y el siguiente barrido la relee: llega como idempotente.
        await puente.EscribirAcuseAsync(new AcuseNotaCreditoAw(
            sol.SolicitudId, ncId, ncId is null ? null : nameof(EstadoTimbrado.PorAutorizar), resultado, motivo), cancellationToken);
        return new ProcesarNotaCreditoAwResponse(resultado, ncId, motivo);
    }

    private async Task<(ResultadoSolicitudAw, Guid?, string?)> ProcesarAsync(
        Guid empresaId, SolicitudNotaCreditoAw sol, CancellationToken cancellationToken)
    {
        var existente = await db.NotasCredito.FirstOrDefaultAsync(n => n.AwNumeroNota == sol.NumeroNota, cancellationToken);
        if (existente is not null)
            return sol.Version <= existente.AwVersionNota
                ? (ResultadoSolicitudAw.Aplicada, existente.Id, "nota ya recibida (idempotente)")
                : (ResultadoSolicitudAw.Rechazada, existente.Id,
                    $"la nota {sol.NumeroNota} ya existe en el ERP (versión {existente.AwVersionNota}); una versión nueva no la modifica");

        if (!Enum.TryParse<MotivoNotaCredito>(sol.Motivo, ignoreCase: true, out var motivo)
            || motivo is not (MotivoNotaCredito.Bonificacion or MotivoNotaCredito.Devolucion))
            return await RechazarAsync(empresaId, sol, MotivoExcepcion.Otro,
                $"motivo '{sol.Motivo}' inválido: solo Bonificacion o Devolucion", cancellationToken);
        if (sol.Importe <= 0)
            return await RechazarAsync(empresaId, sol, MotivoExcepcion.Otro, "el importe debe ser mayor que cero", cancellationToken);

        // Factura de origen: por folio si A+W lo manda; si no, la factura timbrada del pedido (si hay más de una, A+W debe indicar el folio).
        var timbradas = db.FacturasVenta.Where(f => f.Estado == EstadoTimbrado.Timbrado);
        var candidatas = string.IsNullOrWhiteSpace(sol.FacturaFolio)
            ? await (from f in timbradas
                     join p in db.PedidosFacturables on f.PedidoFacturableId equals (Guid?)p.Id
                     where p.Origen == OrigenPedido.Aw && p.NumeroPedido == sol.NumeroPedido
                     select f).Take(2).ToListAsync(cancellationToken)
            : await timbradas.Where(f => f.Folio == sol.FacturaFolio).Take(2).ToListAsync(cancellationToken);
        if (candidatas.Count != 1)
            return await RechazarAsync(empresaId, sol, MotivoExcepcion.NcFacturaNoExiste,
                candidatas.Count == 0
                    ? $"no hay factura timbrada vigente para {(string.IsNullOrWhiteSpace(sol.FacturaFolio) ? $"el pedido {sol.NumeroPedido}" : $"el folio {sol.FacturaFolio}")}"
                    : $"el pedido {sol.NumeroPedido} tiene más de una factura timbrada; A+W debe indicar el folio de la factura",
                cancellationToken);
        var factura = candidatas[0];

        var pedido = await db.PedidosFacturables.AsNoTracking().FirstOrDefaultAsync(p => p.Id == factura.PedidoFacturableId, cancellationToken);
        var cliente = await clientes.ResolverPorReferenciaAsync(sol.ClienteRef, cancellationToken);
        if (cliente is null || pedido is null || cliente.ClienteId != pedido.ClienteId)
            return await RechazarAsync(empresaId, sol, MotivoExcepcion.NcClienteDistinto,
                $"el cliente '{sol.ClienteRef}' no es el de la factura {factura.Folio}", cancellationToken);

        var saldo = await saldos.ObtenerSaldoAsync(factura.Id, cancellationToken) ?? factura.Total;
        var reservado = await db.NotasCredito
            .Where(n => n.FacturaRelacionadaId == factura.Id && n.Estado == EstadoTimbrado.PorAutorizar)
            .SumAsync(n => n.Total, cancellationToken);
        if (sol.Importe > saldo - reservado)
            return await RechazarAsync(empresaId, sol, MotivoExcepcion.NcImporteExcedeSaldo,
                $"importe {sol.Importe:N2} mayor que el saldo {saldo - reservado:N2} de la factura {factura.Folio}"
                + (reservado > 0 ? $" (hay {reservado:N2} en notas por autorizar)" : string.Empty), cancellationToken);

        var ahora = clock.UtcNow;
        var reserva = await sender.Send(
            new ReservarFolioCommand(empresaId, factura.SucursalId, TipoDocumentoSerie.NotaCredito,
                DateOnly.FromDateTime(ahora.UtcDateTime)), cancellationToken);
        var baseGravable = factura.Subtotal - factura.Descuento;
        var nc = NotaCredito.CrearDesdeAw(
            empresaId, reserva.Folio, reserva.Numero, factura.SucursalId, factura.CajaId,
            new DatosFiscalesReceptor(factura.ReceptorRfc, factura.ReceptorNombre, factura.ReceptorRegimenFiscal,
                factura.ReceptorCodigoPostal, factura.ReceptorUsoCfdi, factura.ReceptorPais, factura.ReceptorEsGenerico),
            factura.SnapshotEmisor(), factura.FormaPago, factura.Moneda, factura.TipoCambio, ahora.Year, ahora.Month,
            factura.CanalVentaId, motivo, factura.Id, sol.Importe,
            tasaIva: baseGravable > 0 ? Math.Round(factura.ImpuestosTrasladados / baseGravable, 2) : 0m,
            sol.Descripcion, sol.NumeroNota, sol.Version, sol.NumeroPedido, pedido.VersionOrigen);
        nc.AgregarRelacion(factura.Uuid!, motivo.ClaveTipoRelacion());
        db.NotasCredito.Add(nc);

        // Una versión anterior rechazada que ya quedó corregida deja de estorbar en la bandeja.
        foreach (var abierta in await ExcepcionesAbiertasAsync(empresaId, sol, cancellationToken))
            abierta.Resolver(usuarioId: null, ahora);
        return (ResultadoSolicitudAw.Aplicada, nc.Id, null);
    }

    private static string Ref(SolicitudNotaCreditoAw sol) => $"NC {sol.NumeroNota}";

    private Task<List<ExcepcionImportacion>> ExcepcionesAbiertasAsync(
        Guid empresaId, SolicitudNotaCreditoAw sol, CancellationToken cancellationToken)
    {
        var pedidoRef = Ref(sol);
        return db.ExcepcionesImportacion
            .Where(e => e.EmpresaId == empresaId && e.Origen == OrigenPedido.Aw && e.PedidoRef == pedidoRef && !e.Resuelto)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Una sola excepción abierta por nota (se refresca si la causa se re-observa) y acuse Rechazada.</summary>
    private async Task<(ResultadoSolicitudAw, Guid?, string?)> RechazarAsync(
        Guid empresaId, SolicitudNotaCreditoAw sol, MotivoExcepcion motivo, string detalle, CancellationToken cancellationToken)
    {
        detalle = $"Nota {sol.NumeroNota} v{sol.Version}: {detalle}";
        var abierta = (await ExcepcionesAbiertasAsync(empresaId, sol, cancellationToken)).FirstOrDefault();
        if (abierta is null)
            db.ExcepcionesImportacion.Add(ExcepcionImportacion.Crear(empresaId, OrigenPedido.Aw, Ref(sol), motivo, detalle));
        else
            abierta.Actualizar(motivo, detalle);
        return (ResultadoSolicitudAw.Rechazada, null, detalle);
    }
}
