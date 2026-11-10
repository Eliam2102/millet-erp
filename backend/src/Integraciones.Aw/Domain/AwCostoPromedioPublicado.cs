using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Aw.Domain;

/// <summary>
/// M-447: último costo promedio que el ERP publicó a la app de Almacenes (<c>dbo.erp_costo_promedio</c>) por
/// artículo A+W + sucursal + almacén (tipo + línea) + medida de hoja. Es la marca de agua del publicador: si el promedio
/// vigente difiere del guardado aquí, se publica una fila nueva con <see cref="VersionCosto"/> + 1. Telemetría operativa, sin auditoría.
/// Sin medida (material por cantidad) AnchoMm = AltoMm = 0; sin lista de almacenes, tipo y línea van "".
/// </summary>
public sealed class AwCostoPromedioPublicado : BaseEntity, INotAudited
{
    public int ArticuloAw { get; private set; }
    public string Sucursal { get; private set; } = string.Empty;
    public string AlmacenTipo { get; private set; } = string.Empty;
    public string Linea { get; private set; } = string.Empty;
    public int AnchoMm { get; private set; }
    public int AltoMm { get; private set; }
    public decimal Costo { get; private set; }
    /// <summary>Versión publicada (BaseEntity.Version ya es el token de concurrencia).</summary>
    public int VersionCosto { get; private set; }
    public DateTimeOffset PublicadoEnUtc { get; private set; }

    private AwCostoPromedioPublicado() { } // EF Core

    public static AwCostoPromedioPublicado Crear(int articuloAw, string sucursal, string almacenTipo, string linea,
        int anchoMm, int altoMm, decimal costo, DateTimeOffset ahora) => new()
    {
        Id = Guid.CreateVersion7(), ArticuloAw = articuloAw, Sucursal = sucursal, AlmacenTipo = almacenTipo, Linea = linea,
        AnchoMm = anchoMm, AltoMm = altoMm, Costo = costo, VersionCosto = 1, PublicadoEnUtc = ahora,
    };

    public void Registrar(decimal costo, DateTimeOffset ahora)
    {
        Costo = costo;
        VersionCosto++;
        PublicadoEnUtc = ahora;
    }
}
