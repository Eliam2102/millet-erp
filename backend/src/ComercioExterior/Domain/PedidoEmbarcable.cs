namespace Millet.ComercioExterior.Domain;

public sealed record LineaEmbarcable(int Posicion, Guid? ProductoId, string Descripcion, decimal Cantidad, string Unidad);
public sealed record PedidoEmbarcable(Guid Id, string NumeroPedido, Guid ClienteId, string ClienteNombre,
    Guid SucursalId, bool EsExportacion, string Estado, long? VersionOrigen,
    IReadOnlyList<LineaEmbarcable> Lineas, string? FacturaFolio, string? FacturaUuid, bool FacturaVigente = false);

