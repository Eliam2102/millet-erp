using MediatR;

namespace Millet.Compras.Application.Motivos;

/// <summary>
/// Query para listar los motivos de rechazo activos del catálogo. Sin
/// filtro por <see cref="Domain.MotivoRechazoAplicaA"/> en v1; los 6
/// motivos seed aplican a las 3 acciones (rechazo, eliminación,
/// cancelación).
/// </summary>
public sealed record ListarMotivosRechazoQuery() : IRequest<IReadOnlyList<MotivoRechazoResponse>>;
