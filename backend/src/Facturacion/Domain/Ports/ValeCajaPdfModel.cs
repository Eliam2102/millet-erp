namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Modelo de impresión del Vale de Caja (86e3ww6tw): comprobante interno de
/// entrega de efectivo de un retiro de caja, con el formato que mandó Millet
/// el 28-oct. Lo arma <c>ValeCajaPdfModelBuilder</c> desde el retiro ya
/// guardado, así la reimpresión sale con el mismo folio y los mismos datos;
/// solo cambia la fecha de impresión.
/// </summary>
/// <param name="FormaPagoOtro">Clave y descripción SAT cuando la forma no es efectivo ni cheque.</param>
public sealed record ValeCajaPdfModel(
    int Folio,
    DateOnly FechaExpedicion,
    string Cajero,
    decimal Importe,
    string ImporteEnLetra,
    string Concepto,
    FormaPagoVale FormaPago,
    string? FormaPagoOtro,
    DateOnly FechaImpresion,
    string EmpresaNombre,
    byte[]? Logo,
    string NombreArchivo);

/// <summary>Casilla marcada en «Forma de pago» del vale.</summary>
public enum FormaPagoVale
{
    Efectivo = 1,
    Cheque = 2,
    Otro = 3,
}

/// <summary>Genera el PDF del Vale de Caja (QuestPDF, kit de formatos de Millet).</summary>
public interface IGenerarValeCajaPdfPort
{
    PdfFacturaGenerado Generar(ValeCajaPdfModel modelo);
}
