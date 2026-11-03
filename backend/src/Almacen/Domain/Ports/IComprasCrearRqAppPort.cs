namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de escritura hacia Compras para crear la requisición que nace del mensaje
/// REQUISICION_ENVIADA de la app de Almacenes (contrato v1 §8.1). Interfaz en Almacén
/// (consumidor); adapter en <c>Compras.Infrastructure.PublicAdapters</c>. Idempotente por
/// <c>FolioExterno</c>: si ya existe devuelve su folio con
/// <see cref="CrearRqAppResultado.Duplicada"/>. Los errores esperables de datos (proveedor,
/// artículo o sucursal desconocidos, regla de negocio) regresan en <c>Error</c>, sin crear nada.
/// </summary>
public interface IComprasCrearRqAppPort
{
    Task<CrearRqAppResultado> CrearAsync(CrearRqAppSolicitud s, CancellationToken ct);
}

public sealed record CrearRqAppSolicitud(
    string FolioExterno, string SucursalCodigo, int ProveedorClaveAw,
    DateOnly? FechaRequerida, string? Comentario, IReadOnlyList<LineaRqApp> Lineas);

public sealed record LineaRqApp(string ArticuloAw, int Piezas, int? AnchoMm, int? AltoMm, string? Obra);

public sealed record CrearRqAppResultado(bool Duplicada, string? Folio, string? Error);
