namespace Millet.Compras.Domain.Ports.Almacen;

/// <summary>Línea de una entrada de compra confirmada en Almacén: cantidad en la unidad de la línea de OC, lote y rack (clave de la ubicación).</summary>
public sealed record RecepcionAwLinea(Guid LineaOcId, decimal Cantidad, string? Lote, string? Rack, string? FraccionArancelaria);

/// <summary>Documentos de importación capturados al dar entrada (solo existen si la OC es de importación).</summary>
public sealed record RecepcionAwImportacion(string NumeroPedimento, string NumeroBl, string NumeroContenedor, string Aduana, string ClavePedimento);

public sealed record RecepcionAw(Guid RecepcionId, string? Folio, DateOnly Fecha, RecepcionAwImportacion? Importacion, IReadOnlyList<RecepcionAwLinea> Lineas);

/// <summary>Lectura de las entradas de compra Registradas de una OC con lo que A+W necesita saber (86e3jvu83 · CA3.10, CA13.8, CA12.2).</summary>
public interface IRecepcionesAwReadPort
{
    Task<IReadOnlyList<RecepcionAw>> ListarPorOcAsync(Guid ocId, CancellationToken ct);
}
