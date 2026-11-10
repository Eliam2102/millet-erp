using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Tambores;

/// <summary>
/// Tambor o cubeta surtido a una línea de producción (M-172, AC-030). Nace <see cref="Activo"/> con la salida que lo
/// descontó del inventario; cuando sale otro del mismo artículo a la misma línea, éste queda <see cref="Gastado"/> con
/// fecha de fin y el folio de la salida que lo reemplazó, sin captura de sobrante. Tabla <c>almacen.tambores_linea</c>.
/// </summary>
public sealed class TamborEnLinea : BaseEntity, IAuditable
{
    public const string Activo = "ACTIVO";
    public const string Gastado = "GASTADO";

    public Guid LineaProduccionId { get; private set; }
    public Guid ArticuloId { get; private set; }
    /// <summary>Movimiento de salida que lo descontó del inventario y su folio.</summary>
    public Guid MovimientoId { get; private set; }
    public string Folio { get; private set; } = string.Empty;
    /// <summary>Kilos de la presentación al surtirlo (dato provisional del artículo hasta M-106).</summary>
    public decimal? KgPresentacion { get; private set; }
    public DateTimeOffset FechaInicio { get; private set; }
    public string Estado { get; private set; } = Activo;
    public DateTimeOffset? FechaFin { get; private set; }
    public string? FolioReemplazo { get; private set; }

    private TamborEnLinea() { }

    public TamborEnLinea(Guid lineaProduccionId, Guid articuloId, Guid movimientoId, string folio, decimal? kgPresentacion,
        DateTimeOffset fechaInicio) : base(Guid.CreateVersion7())
    {
        LineaProduccionId = lineaProduccionId; ArticuloId = articuloId; MovimientoId = movimientoId; Folio = folio;
        KgPresentacion = kgPresentacion; FechaInicio = fechaInicio;
    }

    /// <summary>El tambor se da por consumido al entrar otro del mismo artículo a la misma línea.</summary>
    public void Gastar(DateTimeOffset fechaFin, string folioReemplazo)
    {
        Estado = Gastado; FechaFin = fechaFin; FolioReemplazo = folioReemplazo;
    }
}
