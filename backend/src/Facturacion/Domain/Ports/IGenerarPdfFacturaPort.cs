using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;

namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Genera el PDF de un comprobante fiscal. Para la <see cref="FacturaVenta"/>
/// (F2-PR2) hay dos formatos (§7.1 levantamiento):
/// <see cref="FormatoPdfFactura.Bilingue"/> (ES/EN, tamaño carta, para el
/// cliente/aduanas) y <see cref="FormatoPdfFactura.TermicaSimplificada"/>
/// (sólo español, angosto, para impresora térmica). La factura de anticipo y
/// la nota de crédito (13-C, doc 13) son documentos de concepto único y solo
/// existen en bilingüe. Server-side con QuestPDF (mismo motor que la OC).
/// </summary>
public interface IGenerarPdfFacturaPort
{
    PdfFacturaGenerado Generar(FacturaVenta factura, FormatoPdfFactura formato);

    /// <summary>PDF bilingüe de una factura de anticipo (concepto único, 13-C).</summary>
    PdfFacturaGenerado Generar(FacturaAnticipo factura);

    /// <summary>PDF bilingüe de una nota de crédito (Egreso; motivo + relaciones, 13-C).</summary>
    PdfFacturaGenerado Generar(NotaCredito notaCredito);
}

public enum FormatoPdfFactura
{
    /// <summary>Carta, bilingüe ES/EN.</summary>
    Bilingue = 1,

    /// <summary>Ticket angosto (≈80mm), sólo español.</summary>
    TermicaSimplificada = 2,
}

public sealed record PdfFacturaGenerado(byte[] Contenido, string ContentType, string NombreSugerido);
