namespace Millet.Facturacion.Domain.Facturas;

public sealed record ContenedorFacturaCe(string Numero, string BlGuia, DateOnly FechaEmbarqueReal);
/// <summary>Copia fiscal inmutable; VAL-CE-08 pendiente: número de exportación fuera del XML.</summary>
public sealed record DatosEmbarqueCe(string FolioExpediente, string NumeroPedimento, string ClavePedimento,
    DateOnly FechaPedimento, string Incoterm, IReadOnlyList<ContenedorFacturaCe> Contenedores, string? Aduana = null, string? AduanaNombre = null);
