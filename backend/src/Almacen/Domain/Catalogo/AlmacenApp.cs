using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Catalogo;

/// <summary>
/// Llave de almacén de la app de Almacenes (M-446, lista de TI del 9-nov): <c>sucursal + tipo + línea</c> resuelve a una
/// ubicación del ERP, donde vive el saldo. La línea (HEGLA 1, HEGLA 2, BYSTRONIC) va vacía en herraje. La división
/// (EXPORTACION, OBRAS, GENERAL, IMPORTACION TEMPORAL) vive solo en la app; en A+W las cuatro comparten ubicación por línea
/// (<see cref="UbicacionAw"/>, de donde sale su costo promedio). Se carga por migración; no se edita desde la UI.
/// </summary>
public sealed class AlmacenApp : BaseEntity, IAuditable
{
    public Guid SucursalId { get; private set; }
    public string TipoAlmacen { get; private set; } = string.Empty;
    public string Linea { get; private set; } = string.Empty;
    public Guid UbicacionId { get; private set; }
    public string? UbicacionAw { get; private set; }
    public bool Activo { get; private set; } = true;

    private AlmacenApp() { }

    public AlmacenApp(Guid id, Guid sucursalId, string tipoAlmacen, string? linea, Guid ubicacionId, string? ubicacionAw = null) : base(id)
    {
        SucursalId = sucursalId;
        TipoAlmacen = Normalizar(tipoAlmacen);
        Linea = Normalizar(linea);
        UbicacionId = ubicacionId;
        UbicacionAw = ubicacionAw;
    }

    /// <summary>Mayúsculas sin espacios sobrantes; null o vacío = "" (herraje sin línea).</summary>
    public static string Normalizar(string? valor) =>
        string.Join(' ', (valor ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
