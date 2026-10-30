using MediatR;
using Millet.ProductoTerminado.Application.Escaneos;

namespace Millet.ProductoTerminado.Application.Consultas;

/// <summary>Piezas en PT, por sucursal física. Con <c>Pedido</c> devuelve además el avance por posición (calculado, nunca almacenado).</summary>
public sealed record ListarPiezasQuery(string? Pedido, Guid? SucursalId, int Page, int PageSize) : IRequest<PiezasPagina>;

/// <summary>Bandeja de escaneos abiertos (Pospuesto, Excepcion, Reposicion sin resolver).</summary>
public sealed record ListarEscaneosQuery(string? Resultado, int Page, int PageSize) : IRequest<EscaneosPagina>;

public sealed record PiezaDto(Guid Id, string Etiqueta, string NumeroPedido, int Posicion, int NumeroPieza, Guid SucursalId, string Estado, DateTimeOffset EscaneadaAt);

/// <summary><c>AvanceFisico</c>: SinPiezas | Parcial | Completo.</summary>
public sealed record AvancePosicionDto(int Posicion, string? Descripcion, int Esperadas, int Terminadas, int Faltantes, string AvanceFisico);

public sealed record PiezasPagina(IReadOnlyList<PiezaDto> Items, int Total, IReadOnlyList<AvancePosicionDto> Avance);

public sealed record EscaneosPagina(IReadOnlyList<EscaneoDto> Items, int Total);
