using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Anticipos;

/// <summary>
/// 86e3zg0vb: el anticipo y la factura final deben ser del mismo <b>cliente</b>, no solo del mismo RFC. Con el RFC genérico extranjero
/// (XEXX010101000) todos los clientes del exterior comparten RFC: cruzar solo por RFC permitiría aplicar el anticipo de un cliente a la
/// factura de otro. Con RFC nominal el RFC sigue bastando cuando la factura no trae cliente (caso nacional sin cambio).
/// </summary>
public static class AnticiposDelCliente
{
    /// <summary>Al emitir la factura final: <paramref name="clienteId"/> es el del comando o el del pedido.</summary>
    public static void Verificar(IEnumerable<Anticipo> anticipos, Guid? clienteId, string rfcFactura)
    {
        foreach (var a in anticipos)
        {
            if (clienteId is { } id && a.ClienteId != id)
                throw new BusinessRuleException("ANTICIPO_CLIENTE_DISTINTO",
                    "El anticipo es de otro cliente; solo se aplica a facturas del cliente que lo pagó.");
            if (clienteId is null && DatosFiscalesReceptor.EsRfcGenerico(rfcFactura))
                throw new BusinessRuleException("ANTICIPO_CLIENTE_REQUERIDO",
                    "Con RFC genérico (cliente extranjero) indica el cliente de la factura para aplicar su anticipo.");
        }
    }

    /// <summary>
    /// Sobre una factura ya timbrada (aplicar o apartar después): el cliente sale de su pedido. Sin pedido y con RFC genérico, vale la
    /// relación 07 de la factura al CFDI del anticipo (se la puso quien emitió la factura para ese anticipo); si no, se rechaza.
    /// </summary>
    public static async Task VerificarAsync(FacturacionDbContext db, Anticipo anticipo, FacturaAnticipo cfdiAnticipo, FacturaVenta factura,
        CancellationToken ct)
    {
        if (!string.Equals(anticipo.ReceptorRfc, factura.ReceptorRfc, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("ANTICIPO_CLIENTE_DISTINTO",
                "El anticipo y la factura deben ser del mismo cliente (RFC receptor).");
        Guid? clienteId = factura.PedidoFacturableId is { } pedidoId
            ? await db.PedidosFacturables.AsNoTracking().Where(p => p.Id == pedidoId).Select(p => (Guid?)p.ClienteId).FirstOrDefaultAsync(ct)
            : null;
        if (clienteId is { } id)
        {
            if (anticipo.ClienteId != id)
                throw new BusinessRuleException("ANTICIPO_CLIENTE_DISTINTO",
                    "El anticipo es de otro cliente; solo se aplica a facturas del cliente que lo pagó.");
            return;
        }
        if (DatosFiscalesReceptor.EsRfcGenerico(factura.ReceptorRfc)
            && !factura.Relaciones.Any(r => r.TipoRelacion == "07"
                && string.Equals(r.UuidRelacionado, cfdiAnticipo.Uuid, StringComparison.OrdinalIgnoreCase)))
            throw new BusinessRuleException("ANTICIPO_CLIENTE_NO_VERIFICABLE",
                "La factura es a RFC genérico, no viene de un pedido y no está relacionada (07) con este anticipo: no se puede confirmar que sea del mismo cliente.");
    }
}
