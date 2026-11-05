using MediatR;

namespace Millet.ProductoTerminado.Application.Salidas;

/// <summary>Pedido de la hoja con las etiquetas de lo cargado. El orden de la lista es la secuencia de entrega.</summary>
public sealed record PedidoCargadoDto(string NumeroPedido, IReadOnlyList<string> Etiquetas);

/// <summary>
/// Hoja de salida en Borrador (PT5.5): las piezas pasan a Cargada y cada pedido recibe su albarán. Sin etiquetas arma solo la ruta (PT5.4): las piezas se cargan después, una por una. Lo foráneo (carretera federal) exige los datos de la Carta Porte.
/// </summary>
public sealed record CrearHojaSalidaCommand(
    Guid SucursalId, string Destino, bool Foranea, Guid? VehiculoId, Guid? OperadorId, decimal? DistanciaKm, decimal? PesoKg,
    string? OrigenCodigoPostal, string? OrigenEstado, string? DestinoCodigoPostal, string? DestinoEstado, IReadOnlyList<PedidoCargadoDto> Pedidos) : IRequest<HojaSalidaDto>;

/// <summary>Emite los albaranes: descuenta las piezas (Embarcada), registra la salida en Facturación, emite la Carta Porte si es foránea y pasa los pedidos a 71. Emitir otra vez no repite nada.</summary>
public sealed record EmitirHojaSalidaCommand(Guid Id) : IRequest<HojaSalidaDto>;

/// <summary>Solo desde Borrador: las piezas vuelven a EnPT.</summary>
public sealed record CancelarHojaSalidaCommand(Guid Id) : IRequest<HojaSalidaDto>;

public sealed record ListarHojasSalidaQuery(string? Estado, Guid? SucursalId, int Page, int PageSize) : IRequest<HojasSalidaPagina>;

public sealed record ObtenerHojaSalidaQuery(Guid Id) : IRequest<HojaSalidaDto>;

public sealed record AlbaranLineaDto(int Posicion, string? Descripcion, int Piezas);

/// <summary>PT5.4: <c>Esperadas</c> son las piezas del pedido; <c>Disponibles</c> (solo en carga) las que se pueden cargar; <c>Rotas</c> las pendientes de reposición.</summary>
public sealed record AlbaranDto(Guid Id, string Folio, int Secuencia, string NumeroPedido, string Cliente, string? Destino, DateTimeOffset? EmitidoAt, int Total, IReadOnlyList<AlbaranLineaDto> Lineas, IReadOnlyList<string> Etiquetas,
    int Esperadas = 0, IReadOnlyList<string>? Disponibles = null, IReadOnlyList<string>? Rotas = null);

/// <summary><c>Albaranes</c> solo viene en el detalle. <c>YaEmitida</c>: la emisión se pidió de nuevo y no cambió nada.</summary>
public sealed record HojaSalidaDto(
    Guid Id, string Folio, Guid SucursalId, string Destino, bool Foranea, Guid? VehiculoId, Guid? OperadorId, string Estado, DateTimeOffset CreadaAt, DateTimeOffset? EmitidaAt,
    string? CartaPorteFolio, int Pedidos, int Piezas, IReadOnlyList<AlbaranDto> Albaranes, bool YaEmitida = false);

public sealed record HojasSalidaPagina(IReadOnlyList<HojaSalidaDto> Items, int Total);
