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

    /// <summary>
    /// REQUISICION_CANCELADA (M-446): cancela la RQ de la app por su folio del ERP (<paramref name="folioErp"/>, v9) o, si no viene o no
    /// existe, por su folio externo con el motivo de la app; no mueve existencia
    /// (lo recibido baja con la SALIDA_MANUAL que la app manda enseguida). RQ inexistente (nunca llegó o el ERP la rechazó) o ya
    /// terminada → sin cambios y sin error.
    /// </summary>
    Task<AvisoRqAppResultado> CancelarAsync(string folioExterno, string? motivo, CancellationToken ct, string? folioErp = null);

    /// <summary>
    /// REQUISICION_REDUCIDA (M-446): quita de lo que falta comprar las piezas de cada posición (cancelación de una entrada o cierre
    /// con faltante); Compras ya no compra eso. Misma regla de RQ inexistente o terminada que <see cref="CancelarAsync"/>.
    /// </summary>
    Task<AvisoRqAppResultado> ReducirAsync(string folioExterno, IReadOnlyList<LineaQuitadaRqApp> lineas, string? motivo, CancellationToken ct,
        string? folioErp = null);
}

public sealed record LineaQuitadaRqApp(int Posicion, int Piezas);

/// <summary><c>Folio</c> = folio del ERP de la RQ (null si no existe en el ERP); <c>Error</c> no nulo = no se aplicó.</summary>
public sealed record AvisoRqAppResultado(string? Folio, string? Error);

public sealed record CrearRqAppSolicitud(
    string FolioExterno, string SucursalCodigo, int ProveedorClaveAw,
    DateOnly? FechaRequerida, string? Comentario, IReadOnlyList<LineaRqApp> Lineas)
{
    /// <summary>Equipo (Dim3) de Almacén que llevan las líneas (Jorge, 11-nov: lo que pide la app es del área de Almacén).</summary>
    public Guid? CentroCostoId { get; init; }
}

/// <summary><c>Posicion</c> (v9): la de la app; REQUISICION_REDUCIDA se refiere a ella. Medidas en mm enteros (ver el parseo).</summary>
public sealed record LineaRqApp(string ArticuloAw, int Piezas, int? AnchoMm, int? AltoMm, string? Obra, int? Posicion = null);

public sealed record CrearRqAppResultado(bool Duplicada, string? Folio, string? Error);
