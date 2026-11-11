using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.CartaPorte;

/// <summary>
/// M-354: un tramo del traslado de un pedido, con su modalidad. <see cref="ModalidadTramo.VehiculoPropio"/> emite una Carta
/// Porte de Millet (queda ligada en <see cref="CartaPorteId"/>); <see cref="ModalidadTramo.TransportistaTercero"/> no emite y
/// registra el documento del tercero. Lleva <see cref="SucursalId"/> del pedido para el alcance por sucursal (ADR-0051).
/// </summary>
public sealed class TramoTraslado : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid SucursalId { get; private set; }
    public Guid PedidoFacturableId { get; private set; }

    /// <summary>Posición del tramo en el traslado (1..n), única por pedido.</summary>
    public int Orden { get; private set; }

    public ModalidadTramo Modalidad { get; private set; }

    public string Origen { get; private set; } = string.Empty;
    public string Destino { get; private set; } = string.Empty;
    public decimal? DistanciaKm { get; private set; }
    public string? OrigenCodigoPostal { get; private set; }
    public string? OrigenEstado { get; private set; }
    public string? DestinoCodigoPostal { get; private set; }
    public string? DestinoEstado { get; private set; }
    public DateTimeOffset? FechaSalida { get; private set; }

    /// <summary>Carta Porte de Millet de este tramo (solo vehículo propio).</summary>
    public Guid? CartaPorteId { get; private set; }

    // ---- Documento del transportista tercero (el tramo no emite Carta Porte de Millet).
    public string? TerceroNombre { get; private set; }
    public string? TerceroRfc { get; private set; }

    /// <summary>Documento que ampara el traslado (p. ej. «CartaPorte» del transportista).</summary>
    public string? DocumentoTipo { get; private set; }
    public string? DocumentoFolio { get; private set; }
    public string? DocumentoUuid { get; private set; }
    public DateOnly? DocumentoFecha { get; private set; }
    public DateTimeOffset? DocumentoRegistradoEn { get; private set; }
    public string? DocumentoRegistradoPor { get; private set; }

    public bool TieneDocumentoTercero => !string.IsNullOrWhiteSpace(DocumentoFolio) || !string.IsNullOrWhiteSpace(DocumentoUuid);

    private TramoTraslado() { }

    private TramoTraslado(Guid id) : base(id) { }

    public static TramoTraslado Crear(
        Guid empresaId, Guid sucursalId, Guid pedidoFacturableId, int orden, ModalidadTramo modalidad,
        string origen, string destino, decimal? distanciaKm, string? origenCp, string? origenEstado,
        string? destinoCp, string? destinoEstado, DateTimeOffset? fechaSalida)
    {
        if (!Enum.IsDefined(modalidad))
            throw new BusinessRuleException("TRAMO_MODALIDAD_INVALIDA", "La modalidad del tramo no es válida.");
        if (orden < 1)
            throw new BusinessRuleException("TRAMO_ORDEN_INVALIDO", "El orden del tramo debe ser >= 1.");
        if (string.IsNullOrWhiteSpace(origen) || string.IsNullOrWhiteSpace(destino))
            throw new BusinessRuleException("TRAMO_UBICACION_REQUERIDA", "El origen y el destino del tramo son obligatorios.");
        if (distanciaKm is < 0)
            throw new BusinessRuleException("TRAMO_DISTANCIA_INVALIDA", "La distancia no puede ser negativa.");

        return new TramoTraslado(Guid.CreateVersion7())
        {
            EmpresaId = empresaId,
            SucursalId = sucursalId,
            PedidoFacturableId = pedidoFacturableId,
            Orden = orden,
            Modalidad = modalidad,
            Origen = origen.Trim(),
            Destino = destino.Trim(),
            DistanciaKm = distanciaKm,
            OrigenCodigoPostal = Limpiar(origenCp),
            OrigenEstado = Limpiar(origenEstado),
            DestinoCodigoPostal = Limpiar(destinoCp),
            DestinoEstado = Limpiar(destinoEstado),
            FechaSalida = fechaSalida?.ToUniversalTime(),
        };
    }

    /// <summary>Liga la Carta Porte emitida para este tramo. Solo vehículo propio y una sola vez.</summary>
    public void VincularCartaPorte(Guid cartaPorteId)
    {
        ExigirEmiteCartaPorte();
        if (CartaPorteId is not null)
            throw new BusinessRuleException("TRAMO_YA_TIENE_CARTA_PORTE", "El tramo ya tiene su Carta Porte.");
        CartaPorteId = cartaPorteId;
    }

    /// <summary>Un tramo de transportista tercero no emite Carta Porte de Millet.</summary>
    public void ExigirEmiteCartaPorte()
    {
        if (Modalidad != ModalidadTramo.VehiculoPropio)
            throw new BusinessRuleException(
                "CARTA_PORTE_TRAMO_TERCERO",
                "Este tramo lo recorre un transportista tercero: Millet no emite Carta Porte; registra el documento del tercero.");
    }

    /// <summary>Registra (o reemplaza) el documento del transportista tercero que ampara el tramo.</summary>
    public void RegistrarDocumentoTercero(
        string terceroNombre, string? terceroRfc, string documentoTipo, string? folio, string? uuid, DateOnly? fecha,
        string? registradoPor, DateTimeOffset ahora)
    {
        if (Modalidad != ModalidadTramo.TransportistaTercero)
            throw new BusinessRuleException("TRAMO_NO_ES_TERCERO", "Solo un tramo de transportista tercero registra el documento del tercero.");
        if (string.IsNullOrWhiteSpace(terceroNombre))
            throw new BusinessRuleException("TRAMO_TERCERO_SIN_NOMBRE", "Indica el transportista tercero.");
        if (string.IsNullOrWhiteSpace(documentoTipo))
            throw new BusinessRuleException("TRAMO_DOCUMENTO_SIN_TIPO", "Indica el tipo de documento del tercero.");
        if (string.IsNullOrWhiteSpace(folio) && string.IsNullOrWhiteSpace(uuid))
            throw new BusinessRuleException("TRAMO_DOCUMENTO_SIN_FOLIO", "Indica el folio o el UUID del documento del tercero.");

        TerceroNombre = terceroNombre.Trim();
        TerceroRfc = Limpiar(terceroRfc)?.ToUpperInvariant();
        DocumentoTipo = documentoTipo.Trim();
        DocumentoFolio = Limpiar(folio);
        DocumentoUuid = Limpiar(uuid)?.ToUpperInvariant();
        DocumentoFecha = fecha;
        DocumentoRegistradoEn = ahora;
        DocumentoRegistradoPor = Limpiar(registradoPor);
    }

    private static string? Limpiar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
