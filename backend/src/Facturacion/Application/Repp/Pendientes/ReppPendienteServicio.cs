using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Facturas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Repp.Pendientes;

public sealed class ReppPendienteServicio(FacturacionDbContext db, IClientesReadPort clientes,
    ICatalogosSatReadPort catalogos, IReppBancarioReadPort bancario)
{
    public async Task<ReppPendiente> ObtenerAsync(Guid id, bool bloquear, CancellationToken ct)
    {
        // La exclusión ocurre antes de llamar al PAC, también entre petición individual y lote.
        var query = bloquear && db.Database.IsRelational()
            ? db.ReppPendientes.FromSqlInterpolated($"SELECT * FROM facturacion.repp_pendiente WHERE id = {id} FOR UPDATE")
            : db.ReppPendientes;
        return await query.Include(p => p.Facturas).SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new EntityNotFoundException("REPP_PENDIENTE_NO_ENCONTRADO", "No existe el pendiente de REP.");
    }

    public async Task ValidarAsync(ReppPendiente p, string formaPago, IReadOnlyCollection<RelacionRepp> relacion, CancellationToken ct)
    {
        p.VerificarEditable();
        p.ValidarRelacion(relacion);
        if (formaPago == "99" || !await catalogos.ExisteFormaPagoAsync(formaPago, ct))
            throw new BusinessRuleException("REPP_FORMA_PAGO_INVALIDA", "Selecciona una forma de pago real y activa del catálogo SAT.");
        var cliente = await clientes.ObtenerAsync(p.ClienteId, ct)
            ?? throw new BusinessRuleException("REPP_CLIENTE_NO_ENCONTRADO", "El cliente del pago no existe en el catálogo.");
        var ids = relacion.Select(f => f.FacturaVentaId).ToArray();
        var facturas = await db.FacturasVenta.Where(f => ids.Contains(f.Id)).ToListAsync(ct);
        if (facturas.Count != ids.Length)
            throw new BusinessRuleException("REPP_FACTURA_NO_ENCONTRADA", "Alguna factura relacionada no existe en la empresa.");
        var pedidoIds = facturas.Where(f => f.PedidoFacturableId.HasValue).Select(f => f.PedidoFacturableId!.Value).ToArray();
        var pedidos = await db.PedidosFacturables.Where(pedido => pedidoIds.Contains(pedido.Id))
            .Select(pedido => new { pedido.Id, pedido.ClienteId }).ToDictionaryAsync(pedido => pedido.Id, pedido => pedido.ClienteId, ct);
        var acreditados = await SaldoPorCobrar.AcreditadoPorFacturaAsync(db, ids, ct);
        var pagos = await SaldoPorCobrar.PagosVigentes(db).Where(f => ids.Contains(f.FacturaVentaId))
            .GroupBy(f => f.FacturaVentaId).Select(g => new { Id = g.Key, Total = g.Sum(f => f.ImportePagado) })
            .ToDictionaryAsync(g => g.Id, g => g.Total, ct);
        foreach (var factura in facturas)
        {
            var mismoCliente = factura.PedidoFacturableId is Guid pedidoId
                ? pedidos.GetValueOrDefault(pedidoId) == p.ClienteId
                : !cliente.EsGenerico && !factura.ReceptorEsGenerico && !string.IsNullOrWhiteSpace(cliente.Rfc)
                  && string.Equals(factura.ReceptorRfc, cliente.Rfc, StringComparison.OrdinalIgnoreCase);
            if (!mismoCliente)
                throw new BusinessRuleException("REPP_FACTURA_OTRO_CLIENTE", "La factura debe estar identificada con el cliente del pago.");
            if (factura.Estado != EstadoTimbrado.Timbrado || factura.MetodoPago != "PPD")
                throw new BusinessRuleException("REPP_FACTURA_NO_PPD", "Solo se pueden relacionar facturas timbradas PPD. Las PUE no requieren REP.");
            // Conserva la regla PAP_MONEDA_DISTINTA de las propuestas de CxC.
            if (factura.Moneda != p.Moneda)
                throw new BusinessRuleException("REPP_MONEDA_DISTINTA", "La factura debe tener la misma moneda que el pago confirmado.");
            var saldo = factura.Total - acreditados.GetValueOrDefault(factura.Id) - pagos.GetValueOrDefault(factura.Id);
            if (saldo <= 0 || relacion.Single(f => f.FacturaVentaId == factura.Id).Importe > saldo)
                throw new BusinessRuleException("REPP_SALDO_INSUFICIENTE", $"La factura {factura.Folio} no tiene saldo suficiente para el importe relacionado.");
        }
    }

    public async Task<decimal?> TipoCambioAsync(ReppPendiente p, CancellationToken ct)
    {
        if (p.Moneda == "MXN") return null;
        return await bancario.TipoCambioAsync(p.Moneda, p.FechaValor, ct)
            ?? throw new BusinessRuleException("REPP_TC_FALTANTE", "TC por registrar: registra el tipo de cambio histórico del día del pago en Catálogos.");
    }
}
