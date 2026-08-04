using MediatR;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.Aprobadores;

/// <summary>
/// Designa un aprobador para un <c>(departamento, rol)</c> en la
/// empresa actual. Si ya hay uno vigente, lo cierra y graba el nuevo
/// en una sola TX EF (F9-PR1).
/// </summary>
public sealed record DesignarAprobadorCommand(
    Guid DepartamentoId,
    RolAprobador Rol,
    Guid UsuarioId,
    string? Motivo) : IRequest<DesignarAprobadorResponse>;

public sealed record DesignarAprobadorResponse(Guid Id, DateTimeOffset VigenteDesde);
