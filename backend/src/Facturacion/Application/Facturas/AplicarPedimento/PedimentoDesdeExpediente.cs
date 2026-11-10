using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application.PublicPorts;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Facturas.AplicarPedimento;

/// <summary>Pedimento que el expediente de Comercio exterior ofrece para una factura retenida.</summary>
public sealed record PedimentoOfrecido(string? Numero, DateOnly? Fecha, string Motivo);

/// <summary>
/// M-216: el pedimento de una factura retenida en <c>PendientePedimento</c> sale del expediente de
/// Comercio exterior ligado a su pedido (y al contenedor de su salida, FAC-08). Lo usan el lector del
/// worker (para liberarla) y el detalle de la factura (para decir qué falta).
/// </summary>
public static class PedimentoDesdeExpediente
{
    public const string SinExpediente = "Sin expediente de Comercio exterior ligado: captura el pedimento de importación.";

    public static async Task<PedimentoOfrecido> ConsultarAsync(FacturacionDbContext db, IEmbarqueExportacionReadPort? embarques,
        Guid? pedidoFacturableId, Guid? salidaFacturableId, CancellationToken ct)
    {
        if (pedidoFacturableId is not Guid pedido || embarques is null) return new(null, null, SinExpediente);
        var contenedor = salidaFacturableId is not Guid salida ? null
            : await db.SalidasFacturables.AsNoTracking()
                .Where(s => s.Id == salida && s.Origen == OrigenSalidaFacturable.ContenedorExportacion)
                .Select(s => s.OrigenId).FirstOrDefaultAsync(ct);
        var embarque = await embarques.ObtenerAsync(pedido, contenedor, ct);
        if (!embarque.Ligado) return new(null, null, SinExpediente);
        var folio = embarque.FolioExpediente ?? "ligado";
        if (string.IsNullOrWhiteSpace(embarque.NumeroPedimento))
            return new(null, null, $"El expediente {folio} aún no tiene pedimento capturado.");
        if (!PedimentoExpediente.Aplicable(embarque.NumeroPedimento))
            return new(null, null, $"El pedimento del expediente {folio} no tiene el formato de 21 caracteres: corrígelo en Comercio exterior o captúralo aquí.");
        return new(embarque.NumeroPedimento.Trim(), embarque.FechaPedimento,
            $"El pedimento {embarque.NumeroPedimento.Trim()} del expediente {folio} se aplicará en la siguiente revisión automática.");
    }

    /// <summary>Motivo de la retención para el detalle; null si la factura no está en <c>PendientePedimento</c>.</summary>
    public static async Task<string?> MotivoAsync(FacturacionDbContext db, IEmbarqueExportacionReadPort? embarques,
        FacturaVenta factura, CancellationToken ct) =>
        factura.Estado != EstadoTimbrado.PendientePedimento ? null
            : (await ConsultarAsync(db, embarques, factura.PedidoFacturableId, factura.SalidaFacturableId, ct)).Motivo;
}
