using MediatR;
using Millet.ProductoTerminado.Application.Escaneos;

namespace Millet.ProductoTerminado.Application.Consultas;

/// <summary>Piezas en PT, por sucursal física. Con <c>Pedido</c> devuelve además el avance por posición (calculado, nunca almacenado).</summary>
public sealed record ListarPiezasQuery(string? Pedido, Guid? SucursalId, int Page, int PageSize) : IRequest<PiezasPagina>;

/// <summary>Bandeja de escaneos abiertos (Pospuesto, Excepcion, Reposicion sin resolver).</summary>
public sealed record ListarEscaneosQuery(string? Resultado, int Page, int PageSize) : IRequest<EscaneosPagina>;

/// <summary>
/// M-251: <c>Almacen</c>/<c>Lugar</c> = dónde quedó (null = por ubicar); <c>EstacionOrigen</c>, <c>AreaOrigen</c> y <c>TerminadaAwAt</c> = la salida del
/// proceso de producción en A+W que originó la entrada; <c>CostoPendiente</c> hasta la valuación de PT.
/// </summary>
public sealed record PiezaDto(Guid Id, string Etiqueta, string NumeroPedido, int Posicion, int NumeroPieza, Guid SucursalId, string Estado, DateTimeOffset EscaneadaAt, Guid? SucursalDestinoId = null,
    string? Almacen = null, string? Lugar = null, bool PorUbicar = false, int? EstacionOrigen = null, string? AreaOrigen = null, DateTimeOffset? TerminadaAwAt = null, bool CostoPendiente = true);

/// <summary>M-251: confirma la ubicación física de las piezas escaneadas (Nacional o Exportación, y el lugar dentro del almacén).</summary>
public sealed record UbicarPiezasCommand(IReadOnlyList<string> Etiquetas, string Almacen, string? Lugar) : IRequest<IReadOnlyList<PiezaDto>>;

/// <summary>
/// Indicadores por posición (PT5.2), todos calculados: Esperadas (A+W), Terminadas (piezas dadas de alta con la regla SRC-114),
/// EnPt (terminadas que aún no se cargan ni entregan), Cargadas, Entregadas y Pendientes (esperadas - terminadas).
/// <c>AvanceFisico</c>: SinPiezas | Parcial | Completo. Sin estatus de pedido: lo pone A+W (SRC-112/114).
/// </summary>
public sealed record AvancePosicionDto(int Posicion, string? Descripcion, int Esperadas, int Terminadas, int EnPt, int Cargadas, int Entregadas, int Pendientes, string AvanceFisico);

/// <summary>Estado del pedido en el ERP (PT5.2): <c>Retenido</c> (69) o <c>Liberado</c> (70) con su motivo y hora. Es independiente del avance físico (E6) y no es el 69 de A+W.</summary>
public sealed record EstadoPedidoDto(string Estado, string? Motivo, DateTimeOffset? Desde);

public sealed record PiezasPagina(IReadOnlyList<PiezaDto> Items, int Total, IReadOnlyList<AvancePosicionDto> Avance, EstadoPedidoDto? EstadoPedido = null);

public sealed record EscaneosPagina(IReadOnlyList<EscaneoDto> Items, int Total);

/// <summary>Monitor de salidas (PT5.3): pedidos en 70 sin salida. <c>Alerta</c>: <c>24h</c> | <c>48h</c> en el filtro; en la fila además <c>ninguna</c>. Orden: el más antiguo primero.</summary>
public sealed record ListarSalidasPendientesQuery(Guid? SucursalId, string? Alerta, string? Buscar, int Page, int PageSize) : IRequest<SalidasPendientesPagina>;

/// <summary><c>HorasSinSalida</c> son horas completas desde <c>LiberadoAt</c> (70). <c>Alerta</c>: ninguna | 24h | 48h.</summary>
public sealed record SalidaPendienteDto(Guid PedidoPtId, string NumeroPedido, string Cliente, string? Destino, Guid SucursalId, int Piezas, DateTimeOffset LiberadoAt, int HorasSinSalida, string Alerta);

/// <summary><c>Total</c>, <c>Con24h</c> y <c>Con48h</c> cuentan todo lo filtrado por sucursal y búsqueda, antes de paginar y de filtrar por alerta.</summary>
public sealed record SalidasPendientesPagina(IReadOnlyList<SalidaPendienteDto> Items, int Total, int Con24h, int Con48h);
