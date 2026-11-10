using MediatR;

namespace Millet.ProductoTerminado.Application.Liquidacion;

/// <summary>M-177: con voucher, <c>TipoTarjeta</c> («Credito» o «Debito») y <c>TarjetaUltimos4</c> para registrarlo en caja al cerrar.</summary>
public sealed record LiquidacionAlbaranInput(Guid AlbaranId, bool Firmado, decimal Efectivo, decimal Voucher, string? VoucherReferencia,
    string? TipoTarjeta = null, string? TarjetaUltimos4 = null);

public sealed record ImprevistoInput(string Descripcion, decimal Importe);

/// <summary>
/// Liquida en caja la hoja de salida emitida (PT5.7). Con diferencia queda abierta y se puede volver a liquidar; sin diferencia se cierra.
/// M-177: al cerrarse, lo cobrado contra entrega entra como cobro a la sesión de caja abierta de quien liquida (sucursal de la hoja), sobre la
/// factura timbrada de cada albarán; si la caja lo rechaza, la liquidación no se cierra.
/// </summary>
public sealed record LiquidarRutaPtCommand(Guid SalidaId, IReadOnlyList<LiquidacionAlbaranInput> Albaranes, IReadOnlyList<ImprevistoInput> Imprevistos) : IRequest<LiquidacionDto>;

public sealed record ObtenerLiquidacionQuery(Guid SalidaId) : IRequest<LiquidacionDto>;

/// <summary>Hojas emitidas con el estado de su liquidación (Pendiente = aún no se liquida).</summary>
public sealed record ListarLiquidacionesQuery(string? Estado, Guid? SucursalId, int Page, int PageSize) : IRequest<LiquidacionesPagina>;

/// <summary>
/// Cierre del día (PT5.7): avisa a CxC de cada albarán a contra entrega de una hoja emitida hasta esa fecha (hora de Ciudad de México; hoy si no se indica)
/// que sigue sin liquidar. Idempotente: un albarán avisado no se vuelve a avisar. Devuelve cuántos avisos nuevos salieron.
/// <c>PorAlerta</c> (lo usa el worker, regla de salida 5): solo las hojas que ya cumplieron 12 horas hábiles sin liquidar en la zona de su sucursal.
/// </summary>
public sealed record CerrarDiaContraEntregaCommand(DateOnly? Hasta = null, bool PorAlerta = false) : IRequest<int>;

/// <summary>M-177: <c>Esperado</c> = lo que la caja cobra de la factura del albarán (con IVA, neto de NC); sin factura, el total del pedido.
/// <c>FacturaFolio</c> y <c>CobradoEnCaja</c> dicen sobre qué factura y si el cobro ya entró a caja.</summary>
public sealed record LiquidacionLineaDto(Guid AlbaranId, string AlbaranFolio, string NumeroPedido, string Cliente, bool ContraEntrega, decimal Esperado, bool Firmado, decimal Efectivo, decimal Voucher,
    string? VoucherReferencia, decimal Diferencia, bool Conciliado, bool Avisado, string? TipoTarjeta = null, string? TarjetaUltimos4 = null,
    string? FacturaFolio = null, bool CobradoEnCaja = false);

public sealed record LiquidacionDto(Guid SalidaId, string Folio, string Destino, string Estado, DateTimeOffset? LiquidadaAt, IReadOnlyList<LiquidacionLineaDto> Albaranes,
    IReadOnlyList<ImprevistoInput> Imprevistos, decimal TotalEsperado, decimal TotalCobrado, decimal Diferencia);

/// <summary>Regla de salida 5: <c>Plazo</c> = EnPlazo, Alerta (12 horas hábiles sin liquidar) o Vencida (pasó <c>LimiteAt</c>); null si ya se cerró.</summary>
public sealed record LiquidacionResumenDto(Guid SalidaId, string Folio, Guid SucursalId, string Destino, DateTimeOffset EmitidaAt, int Pedidos, int ContraEntrega, string Estado, decimal Diferencia,
    DateTimeOffset? AlertaAt = null, DateTimeOffset? LimiteAt = null, string? Plazo = null);

public sealed record LiquidacionesPagina(IReadOnlyList<LiquidacionResumenDto> Items, int Total);
