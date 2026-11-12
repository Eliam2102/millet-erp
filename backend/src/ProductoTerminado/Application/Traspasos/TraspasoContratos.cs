using MediatR;
using Millet.ProductoTerminado.Domain;

namespace Millet.ProductoTerminado.Application.Traspasos;

/// <summary>
/// Prepara y documenta el traspaso de piezas EnPT de la sucursal origen hacia destino (M-249). Las piezas se identifican por etiqueta y NO se mueven hasta que
/// otra persona lo autorice. Si es foráneo lleva los datos de transporte para la Carta Porte.
/// </summary>
public sealed record CrearTraspasoCommand(Guid SucursalOrigenId, Guid SucursalDestinoId, IReadOnlyList<string> Etiquetas, bool Foranea = false, TransporteTraspaso? Transporte = null) : IRequest<TraspasoDto>;

/// <summary>Autoriza (otra persona distinta de quien lo preparó): las piezas pasan a EnTransito, el foráneo emite su Carta Porte y la sucursal destino recibe el aviso.</summary>
public sealed record AutorizarTraspasoCommand(Guid TraspasoId) : IRequest<TraspasoDto>;

/// <summary>Recepción en destino. <c>PiezaIds</c> null = todas las que siguen en tránsito (total); una lista parcial deja diferencia pendiente.</summary>
public sealed record RecibirTraspasoCommand(Guid TraspasoId, IReadOnlyList<Guid>? PiezaIds) : IRequest<TraspasoDto>;

/// <summary><c>Clasificacion</c>: Faltante, DanadaEnTransito o EnCuarentena.</summary>
public sealed record DiferenciaTraspasoInput(Guid PiezaId, string Clasificacion, string Motivo, string? Evidencia);

/// <summary>M-350: clasifica las piezas que no llegaron (faltante, dañada en tránsito o en cuarentena) con su motivo y evidencia.</summary>
public sealed record ClasificarDiferenciasTraspasoCommand(Guid TraspasoId, IReadOnlyList<DiferenciaTraspasoInput> Piezas) : IRequest<TraspasoDto>;

/// <summary>M-350: aclara una pieza clasificada. <c>RegresadaAlOrigen</c> la devuelve al PT de origen; <c>DadaPorPerdida</c> la deja en revisión para Calidad. La que llega tarde se recibe con <see cref="RecibirTraspasoCommand"/>.</summary>
public sealed record AclararDiferenciaTraspasoCommand(Guid TraspasoId, Guid PiezaId, string Resolucion) : IRequest<TraspasoDto>;

/// <summary><c>Estado</c> admite varios separados por coma (la vista «Por recibir» pide <c>EnTransito,ConDiferencia</c>); <c>DestinoId</c> filtra por la sucursal que recibe.</summary>
public sealed record ListarTraspasosQuery(string? Estado, Guid? SucursalId, int Page, int PageSize, Guid? DestinoId = null) : IRequest<TraspasosPagina>;

public sealed record ObtenerTraspasoQuery(Guid Id) : IRequest<TraspasoDto>;

public sealed record TraspasoPiezaDto(Guid PiezaId, string Etiqueta, string NumeroPedido, bool Recibida, string? Clasificacion = null, string? Motivo = null, string? Evidencia = null, string? Resolucion = null);

/// <summary><c>Piezas</c> solo viene en el detalle; en el listado va vacío. <c>Pendientes</c> = piezas aún en tránsito (sin llegar ni aclarar); en un traspaso preparado, ninguna ha salido.</summary>
public sealed record TraspasoDto(Guid Id, string Folio, Guid SucursalOrigenId, Guid SucursalDestinoId, string Estado, DateTimeOffset SalidaAt, DateTimeOffset? RecibidoAt, int Total, int Pendientes, IReadOnlyList<TraspasoPiezaDto> Piezas,
    Guid PreparadoPorId, Guid? AutorizadoPorId, DateTimeOffset? AutorizadoAt, bool Foranea, string? Destino, string? CartaPorteFolio);

public sealed record TraspasosPagina(IReadOnlyList<TraspasoDto> Items, int Total);
