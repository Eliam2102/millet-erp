using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Administracion.Domain;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.NotasCredito;

/// <summary>
/// Autogenera y timbra la NC de la "ranura" (RANURA-PR2) cuando una
/// <see cref="FacturaVenta"/> emitida desde un pedido A+W con
/// <see cref="PedidoFacturable.Ranura"/> queda <c>Timbrado</c>. Definición
/// fiscal de Millet: la factura va por el total, la ranura se documenta con
/// NC (motivo <see cref="MotivoNotaCredito.Ranura"/>, relación 01) y la caja
/// cobra <c>total − NC acreditadas</c> ([Decisión 13-K] — ya operativo en
/// <c>RegistrarCobroMostrador</c>, sin cambios en caja).
///
/// <para>
/// Se invoca desde TODOS los caminos donde la factura llega a Timbrado:
/// emisión directa (<c>EmitirFacturaVentaHandler</c>), reintento
/// (<c>ReintentarTimbradoHandler</c>) y pedimento diferido
/// (<c>AplicarPedimentoHandler</c>). Idempotente: si la factura ya tiene una
/// NC de ranura vigente, no emite otra. Si la NC no queda timbrada, LANZA —
/// mismo rollback total que la NC de amortización (§3.bis.4).
/// </para>
/// </summary>
public static class NcRanuraEmisor
{
    /// <summary>
    /// Emite la NC de ranura si aplica; <c>null</c> si no aplica (factura no
    /// timbrada, sin pedido, pedido sin ranura o NC ya emitida).
    /// <paramref name="pedido"/> es opcional: los callers que ya lo tienen
    /// trackeado lo pasan; si es <c>null</c> se resuelve por
    /// <see cref="FacturaVenta.PedidoFacturableId"/>.
    /// </summary>
    public static async Task<NotaCreditoRanuraEmitida?> EmitirSiAplicaAsync(
        FacturacionDbContext db,
        ISender sender,
        ICfdiTimbradoPort fiscal,
        ICfdiRepositorioPort cfdiRepo,
        IIntegrationEventPublisher eventos,
        FacturaVenta factura,
        PedidoFacturable? pedido,
        Guid? usuarioEmisorId,
        DateTimeOffset ahora,
        CancellationToken cancellationToken)
    {
        if (factura.Estado != EstadoTimbrado.Timbrado)
            return null;

        if (pedido is null && factura.PedidoFacturableId is Guid pedidoId)
            pedido = await db.PedidosFacturables
                .FirstOrDefaultAsync(p => p.Id == pedidoId, cancellationToken);

        if (pedido?.Ranura is not decimal ranura || ranura <= 0)
            return null;

        // Idempotencia entre caminos (emisión / reintento / pedimento): una
        // NC de ranura vigente sobre esta factura ya la documenta.
        var yaEmitida = await db.NotasCredito.AsNoTracking()
            .AnyAsync(n => n.FacturaRelacionadaId == factura.Id
                           && n.Motivo == MotivoNotaCredito.Ranura
                           && n.Estado != EstadoTimbrado.Cancelado
                           && n.Estado != EstadoTimbrado.Descartada,
                cancellationToken);
        if (yaEmitida)
            return null;

        var reserva = await sender.Send(
            new ReservarFolioCommand(
                factura.EmpresaId, factura.SucursalId,
                TipoDocumentoSerie.NotaCredito, DateOnly.FromDateTime(ahora.UtcDateTime)),
            cancellationToken);

        // Receptor snapshot heredado de la factura (ya validado al emitirla).
        var receptor = new DatosFiscalesReceptor(
            Rfc: factura.ReceptorRfc,
            Nombre: factura.ReceptorNombre,
            RegimenFiscal: factura.ReceptorRegimenFiscal,
            CodigoPostal: factura.ReceptorCodigoPostal,
            UsoCfdi: factura.ReceptorUsoCfdi,
            Pais: factura.ReceptorPais,
            EsGenerico: factura.ReceptorEsGenerico);

        // La ranura de A+W es BRUTA; se desglosa con la tasa del documento
        // (transferida a todas las líneas, FAC-DET-PR2). Sin tasa >0 (p.ej.
        // exportación 0%) el monto va sin desglose de IVA.
        var tasaIva = factura.Lineas
            .Select(l => l.TasaIvaTraslado)
            .FirstOrDefault(t => t is > 0);

        var nc = NotaCredito.CrearRanura(
            empresaId: factura.EmpresaId,
            folio: reserva.Folio,
            folioNumero: reserva.Numero,
            sucursalId: factura.SucursalId,
            cajaId: factura.CajaId,
            usuarioEmisorId: usuarioEmisorId,
            receptor: receptor,
            emisor: factura.SnapshotEmisor(),
            formaPago: factura.FormaPago,
            moneda: factura.Moneda,
            tipoCambio: factura.TipoCambio,
            periodoAnio: factura.PeriodoAnio,
            periodoMes: factura.PeriodoMes,
            canalVentaId: factura.CanalVentaId,
            facturaRelacionadaId: factura.Id,
            montoTotal: ranura,
            tasaIva: tasaIva,
            descripcion: $"Ranura pedido {pedido.NumeroPedido}");

        if (!string.IsNullOrWhiteSpace(factura.Uuid))
            nc.AgregarRelacion(factura.Uuid!, "01");

        var timbre = await TimbradoEjecutor.TimbrarYAplicarAsync(db,
            nc, CfdiEmisionBuilder.DesdeNotaCredito(nc, ahora),
            fiscal, cfdiRepo, ahora, cancellationToken);

        if (timbre.Estado != TimbradoEstado.Timbrado || string.IsNullOrWhiteSpace(timbre.Uuid))
            throw new BusinessRuleException(
                "NC_RANURA_NO_TIMBRADA",
                $"La NC de la ranura del pedido {pedido.NumeroPedido} no se timbró " +
                $"({timbre.Estado}); se aborta la operación (rollback total).");

        db.NotasCredito.Add(nc);

        await eventos.PublishAsync(new NotaCreditoTimbradaIntegrationEvent(
            nc.EmpresaId, ahora, nc.Id, nc.Motivo.ToString(), nc.Uuid!, nc.Total, factura.Id, null),
            cancellationToken);

        return new NotaCreditoRanuraEmitida(nc.Id, nc.Folio, nc.Uuid, nc.Total);
    }
}

/// <summary>NC de ranura autogenerada al timbrar la factura del pedido (RANURA-PR2).</summary>
public sealed record NotaCreditoRanuraEmitida(
    Guid Id,
    string Folio,
    string? Uuid,
    decimal Total);
