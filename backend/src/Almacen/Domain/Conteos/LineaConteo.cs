using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Conteos;

/// <summary>
/// Línea de un <see cref="ConteoInventario"/>. Tabla
/// <c>almacen.lineas_conteo</c>. UNIQUE
/// <c>(conteo_id, articulo_id, ubicacion_id)</c> (ADR-0047 C7.2c — el conteo
/// baja a nivel rack; el índice espeja la PK de saldos por ubicación).
/// <c>SubAlmacenId</c> se conserva denormalizado (lo usa el bloqueo Anual).
///
/// <para>
/// <b>Snapshot inmutable</b> al iniciar (cuidado §4.1 del 04-cuidados-infra):
/// <see cref="CantidadTeorica"/> y <see cref="CostoPromedioSnapshot"/>
/// se congelan al pasar el conteo a EnCurso. Movimientos posteriores
/// no afectan el snapshot — el aprobador compara contra valores
/// estables.
/// </para>
/// <para>
/// <b>Captura sin sesgo</b> (A6): el contador captura
/// <see cref="CantidadRealCapturada"/> sin ver
/// <see cref="CantidadTeorica"/>. La comparación solo la ve el
/// aprobador.
/// </para>
/// </summary>
public sealed class LineaConteo : BaseEntity, IAuditable
{
    public Guid ConteoId { get; private set; }
    public Guid ArticuloId { get; private set; }
    public Guid SubAlmacenId { get; private set; }
    public Guid UbicacionId { get; private set; }
    public decimal CantidadTeorica { get; private set; }
    public decimal CostoPromedioSnapshot { get; private set; }
    public decimal? CantidadRealCapturada { get; private set; }
    public Guid? CapturadoPor { get; private set; }
    public DateTimeOffset? CapturadoAt { get; private set; }
    public bool RequiereRecuento { get; private set; }
    public bool AprobadoIndividualmente { get; private set; }
    public string? Justificacion { get; private set; }

    internal LineaConteo() { }

    public LineaConteo(
        Guid id,
        Guid conteoId,
        Guid articuloId,
        Guid subAlmacenId,
        Guid ubicacionId,
        decimal cantidadTeorica,
        decimal costoPromedioSnapshot) : base(id)
    {
        if (conteoId == Guid.Empty)
            throw new BusinessRuleException("LINEA_CONTEO_SIN_CONTEO",
                "La línea requiere conteo padre.");
        if (articuloId == Guid.Empty)
            throw new BusinessRuleException("LINEA_CONTEO_SIN_ARTICULO",
                "La línea requiere artículo.");
        if (subAlmacenId == Guid.Empty)
            throw new BusinessRuleException("LINEA_CONTEO_SIN_SUB_ALMACEN",
                "La línea requiere sub-almacén.");
        if (ubicacionId == Guid.Empty)
            throw new BusinessRuleException("LINEA_CONTEO_SIN_UBICACION",
                "La línea requiere ubicación (rack).");
        if (cantidadTeorica < 0)
            throw new BusinessRuleException("LINEA_CONTEO_CANT_NEGATIVA",
                "La cantidad teórica no puede ser negativa.");
        if (costoPromedioSnapshot < 0)
            throw new BusinessRuleException("LINEA_CONTEO_COSTO_NEGATIVO",
                "El costo promedio no puede ser negativo.");

        ConteoId = conteoId;
        ArticuloId = articuloId;
        SubAlmacenId = subAlmacenId;
        UbicacionId = ubicacionId;
        CantidadTeorica = cantidadTeorica;
        CostoPromedioSnapshot = costoPromedioSnapshot;
    }

    /// <summary>
    /// Captura la cantidad real medida por el contador. El contador
    /// NO ve <see cref="CantidadTeorica"/> ni la diferencia — el
    /// handler del endpoint filtra esos campos del payload (A6).
    /// </summary>
    public void Capturar(decimal cantidadReal, Guid capturadoPor)
    {
        if (cantidadReal < 0)
            throw new BusinessRuleException("CAPTURA_CANT_NEGATIVA",
                "La cantidad capturada no puede ser negativa.");
        if (capturadoPor == Guid.Empty)
            throw new BusinessRuleException("CAPTURA_SIN_USUARIO",
                "Se requiere el id del contador.");

        CantidadRealCapturada = cantidadReal;
        CapturadoPor = capturadoPor;
        CapturadoAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// F7-PR2: marca la línea como pendiente de recuento porque la
    /// variación excede el umbral A7. El conteo no puede pasar a
    /// Aprobado hasta que esta línea tenga al menos un recuento.
    /// </summary>
    public void MarcarRequiereRecuento() => RequiereRecuento = true;

    /// <summary>F7-PR2: aprobado individualmente (con justificación).</summary>
    public void AprobarConJustificacion(string justificacion)
    {
        if (string.IsNullOrWhiteSpace(justificacion) || justificacion.Length > 500)
            throw new BusinessRuleException("APROBACION_JUSTIFICACION_INVALIDA",
                "La justificación es requerida (≤500 chars).");
        AprobadoIndividualmente = true;
        Justificacion = justificacion;
    }
}
