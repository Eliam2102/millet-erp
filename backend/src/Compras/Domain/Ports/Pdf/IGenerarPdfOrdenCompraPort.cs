using System.Collections.Immutable;
using Millet.Documentos;

namespace Millet.Compras.Domain.Ports.Pdf;

/// <summary>
/// Puerto para dibujar el PDF institucional de una OC (formato Millet, FMT-2).
/// Recibe un modelo de impresión ya resuelto (sin ids ni acceso a datos) y devuelve
/// los bytes del PDF + content type. Armar el modelo es de
/// <c>ImpresionOrdenCompraService</c>.
/// </summary>
public interface IGenerarPdfOrdenCompraPort
{
    /// <param name="marca">"ORIGINAL", "COPIA n · dd/MM/yyyy" o null (vista previa, sin marca).</param>
    Task<PdfOrdenCompraGenerado> GenerarAsync(
        OrdenCompraImpresion modelo,
        string? marca,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resultado de <see cref="IGenerarPdfOrdenCompraPort.GenerarAsync"/>:
/// bytes del PDF + metadata mínima para subir al blob storage.
/// </summary>
public sealed record PdfOrdenCompraGenerado(
    byte[] Contenido,
    string ContentType,
    string NombreSugerido);

/// <summary>Datos de impresión de una OC: solo texto y números listos para mostrar, sin ids.</summary>
public sealed record OrdenCompraImpresion
{
    public required string Folio { get; init; }
    public required string Moneda { get; init; }
    public required DateOnly FechaDocumento { get; init; }
    public DateOnly? FechaEntrega { get; init; }
    public string? Referencia { get; init; }
    public required EmpresaEncabezado Empresa { get; init; }
    public required ProveedorImpresion Proveedor { get; init; }
    public ContactoImpresion? Contacto { get; init; }
    public required string EntregaNombre { get; init; }
    public ImmutableArray<string> EntregaLineas { get; init; } = [];
    public string? UsoCfdi { get; init; }
    public string? MetodoPago { get; init; }
    public string? FormaPago { get; init; }
    public string? CondicionPago { get; init; }
    /// <summary>Solo proveedor extranjero.</summary>
    public string? Incoterm { get; init; }
    public ImmutableArray<RenglonImpresion> Renglones { get; init; } = [];
    public decimal Subtotal { get; init; }
    public decimal Descuento { get; init; }
    /// <summary>Gastos adicionales de la OC: Millet no los usa, solo se imprimen si existen para que la suma cuadre.</summary>
    public decimal OtrosCargos { get; init; }
    public decimal BaseGravable { get; init; }
    public decimal Impuesto { get; init; }
    public decimal Retenciones { get; init; }
    public decimal Redondeo { get; init; }
    public decimal Total { get; init; }
    public string? Motivo { get; init; }
    public FirmaImpresion Elaboro { get; init; } = FirmaImpresion.Vacia;
    public FirmaImpresion Reviso { get; init; } = FirmaImpresion.Vacia;
    public FirmaImpresion Autorizo { get; init; } = FirmaImpresion.Vacia;
}

public sealed record ProveedorImpresion(string Clave, string RazonSocial, string Rfc,
    ImmutableArray<string> DomicilioLineas, string? TaxId = null);

public sealed record ContactoImpresion(string? Nombre, string? Telefono, string? Email);

public sealed record RenglonImpresion(string Clave, string Descripcion, string? Comentario, decimal Cantidad,
    string UnidadMedida, decimal PrecioUnitario, decimal ImpuestoPorcentaje, decimal Total);

public sealed record FirmaImpresion(string? Nombre, DateOnly? Fecha)
{
    public static FirmaImpresion Vacia { get; } = new(null, null);
}
