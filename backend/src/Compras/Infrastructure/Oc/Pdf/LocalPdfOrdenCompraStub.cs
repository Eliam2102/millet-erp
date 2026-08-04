using System.Text;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Pdf;

namespace Millet.Compras.Infrastructure.Oc.Pdf;

/// <summary>
/// Implementación stub (F6-PR1) de <see cref="IGenerarPdfOrdenCompraPort"/>.
/// Produce un "PDF" placeholder en texto plano con folio, totales y
/// líneas agrupadas. El content-type sigue siendo <c>application/pdf</c>
/// para que el flujo del endpoint sea consistente con la impl real
/// (F6-PR3 con QuestPDF).
///
/// <para>
/// La agrupación cosmética de líneas por artículo (§3.bis.4) la hace
/// este stub: si dos líneas tienen el mismo <c>articulo_id</c>,
/// aparecen como una sola fila con la cantidad sumada en el PDF al
/// proveedor. NO oculta los <c>departamento_solicitante_id</c> ni
/// <c>requisicion_id</c> originales — eso es trabajo del template real
/// en F6-PR3.
/// </para>
///
/// PLATFORM-TODO(<![CDATA[<PdfService>]]>): reemplazado por
/// <c>QuestPdfOrdenCompraImpl</c> en F6-PR3.
/// </summary>
public sealed class LocalPdfOrdenCompraStub : IGenerarPdfOrdenCompraPort
{
    public const string GeneradorId = "LocalPdfOrdenCompraStub";

    private readonly ILogger<LocalPdfOrdenCompraStub> _logger;

    public LocalPdfOrdenCompraStub(ILogger<LocalPdfOrdenCompraStub> logger)
    {
        _logger = logger;
    }

    public Task<PdfOrdenCompraGenerado> GenerarAsync(OrdenCompra oc, CancellationToken cancellationToken)
    {
        var totales = oc.CalcularTotales();
        var sb = new StringBuilder();
        sb.AppendLine("ORDEN DE COMPRA — PLACEHOLDER PDF (LocalPdfOrdenCompraStub)");
        sb.AppendLine("===========================================================");
        sb.AppendLine($"Folio:      {oc.Folio.Valor}");
        sb.AppendLine($"Año folio:  {oc.FolioAnio}");
        sb.AppendLine($"Empresa:    {oc.EmpresaId}");
        sb.AppendLine($"Proveedor:  {oc.ProveedorId}");
        sb.AppendLine($"Moneda:     {oc.Moneda}");
        sb.AppendLine();
        sb.AppendLine("Líneas (agrupadas por artículo §3.bis.4):");

        var agrupadas = oc.Lineas
            .GroupBy(l => l.ArticuloId)
            .Select(g => new
            {
                ArticuloId = g.Key,
                CantidadTotal = g.Sum(l => l.Cantidad),
                Unidad = g.First().UnidadMedida,
                PrecioUnitario = g.First().PrecioUnitario,
                SubtotalGrupo = g.Sum(l => l.SubtotalLinea),
            });

        foreach (var grupo in agrupadas)
        {
            sb.AppendLine(
                $"  - {grupo.ArticuloId}  qty={grupo.CantidadTotal} {grupo.Unidad}  " +
                $"pu={grupo.PrecioUnitario:F2}  subtotal={grupo.SubtotalGrupo:F2}");
        }

        sb.AppendLine();
        sb.AppendLine($"Subtotal antes descuento: {totales.SubtotalAntesDescuento:F2}");
        sb.AppendLine($"Descuento global:         {totales.DescuentoGlobalAplicado:F2}");
        sb.AppendLine($"Gastos adicionales:       {totales.GastosAdicionales:F2}");
        sb.AppendLine($"Base gravable:            {totales.BaseGravable:F2}");
        sb.AppendLine($"IVA total:                {totales.IvaTotal:F2}");
        sb.AppendLine($"Retención ISR:            {totales.RetencionIsrTotal:F2}");
        sb.AppendLine($"Redondeo:                 {totales.Redondeo:F2}");
        sb.AppendLine($"TOTAL A PAGAR:            {totales.TotalAPagar:F2} {oc.Moneda}");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        _logger.LogInformation(
            "Stub PDF generado. OcId={OcId} Folio={Folio} Tamaño={Bytes}",
            oc.Id, oc.Folio.Valor, bytes.Length);

        return Task.FromResult(new PdfOrdenCompraGenerado(
            Contenido: bytes,
            ContentType: "application/pdf",
            NombreSugerido: $"{oc.Folio.Valor}.pdf"));
    }
}
