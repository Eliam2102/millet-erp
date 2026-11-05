using MediatR;

namespace Millet.ProductoTerminado.Application.Traspasos;

/// <summary>Salida de piezas EnPT de la sucursal origen hacia destino: pasan a EnTransito (PT5.9). Las piezas se identifican por etiqueta.</summary>
public sealed record CrearTraspasoCommand(Guid SucursalOrigenId, Guid SucursalDestinoId, IReadOnlyList<string> Etiquetas) : IRequest<TraspasoDto>;

/// <summary>Recepción en destino. <c>PiezaIds</c> null = todas las que siguen en tránsito (total); una lista parcial deja diferencia pendiente.</summary>
public sealed record RecibirTraspasoCommand(Guid TraspasoId, IReadOnlyList<Guid>? PiezaIds) : IRequest<TraspasoDto>;

public sealed record ListarTraspasosQuery(string? Estado, Guid? SucursalId, int Page, int PageSize) : IRequest<TraspasosPagina>;

public sealed record ObtenerTraspasoQuery(Guid Id) : IRequest<TraspasoDto>;

public sealed record TraspasoPiezaDto(Guid PiezaId, string Etiqueta, string NumeroPedido, bool Recibida);

/// <summary><c>Piezas</c> solo viene en el detalle; en el listado va vacío. <c>Pendientes</c> = piezas aún en tránsito.</summary>
public sealed record TraspasoDto(Guid Id, Guid SucursalOrigenId, Guid SucursalDestinoId, string Estado, DateTimeOffset SalidaAt, DateTimeOffset? RecibidoAt, int Total, int Pendientes, IReadOnlyList<TraspasoPiezaDto> Piezas);

public sealed record TraspasosPagina(IReadOnlyList<TraspasoDto> Items, int Total);
