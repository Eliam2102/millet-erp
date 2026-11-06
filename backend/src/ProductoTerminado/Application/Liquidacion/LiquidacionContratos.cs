using MediatR;

namespace Millet.ProductoTerminado.Application.Liquidacion;

public sealed record LiquidacionAlbaranInput(Guid AlbaranId, bool Firmado, decimal Efectivo, decimal Voucher, string? VoucherReferencia);

public sealed record ImprevistoInput(string Descripcion, decimal Importe);

/// <summary>Liquida en caja la hoja de salida emitida (PT5.7). Con diferencia queda abierta y se puede volver a liquidar; sin diferencia se cierra.</summary>
public sealed record LiquidarRutaPtCommand(Guid SalidaId, IReadOnlyList<LiquidacionAlbaranInput> Albaranes, IReadOnlyList<ImprevistoInput> Imprevistos) : IRequest<LiquidacionDto>;

public sealed record ObtenerLiquidacionQuery(Guid SalidaId) : IRequest<LiquidacionDto>;

/// <summary>Hojas emitidas con el estado de su liquidación (Pendiente = aún no se liquida).</summary>
public sealed record ListarLiquidacionesQuery(string? Estado, Guid? SucursalId, int Page, int PageSize) : IRequest<LiquidacionesPagina>;

/// <summary>
/// Cierre del día (PT5.7): avisa a CxC de cada albarán a contra entrega de una hoja emitida hasta esa fecha (hora de Ciudad de México; hoy si no se indica)
/// que sigue sin liquidar. Idempotente: un albarán avisado no se vuelve a avisar. Devuelve cuántos avisos nuevos salieron.
/// </summary>
public sealed record CerrarDiaContraEntregaCommand(DateOnly? Hasta = null) : IRequest<int>;

public sealed record LiquidacionLineaDto(Guid AlbaranId, string AlbaranFolio, string NumeroPedido, string Cliente, bool ContraEntrega, decimal Esperado, bool Firmado, decimal Efectivo, decimal Voucher,
    string? VoucherReferencia, decimal Diferencia, bool Conciliado, bool Avisado);

public sealed record LiquidacionDto(Guid SalidaId, string Folio, string Destino, string Estado, DateTimeOffset? LiquidadaAt, IReadOnlyList<LiquidacionLineaDto> Albaranes,
    IReadOnlyList<ImprevistoInput> Imprevistos, decimal TotalEsperado, decimal TotalCobrado, decimal Diferencia);

public sealed record LiquidacionResumenDto(Guid SalidaId, string Folio, Guid SucursalId, string Destino, DateTimeOffset EmitidaAt, int Pedidos, int ContraEntrega, string Estado, decimal Diferencia);

public sealed record LiquidacionesPagina(IReadOnlyList<LiquidacionResumenDto> Items, int Total);
