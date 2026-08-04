using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Aprobadores;

/// <summary>
/// Handler de <see cref="DesignarAprobadorCommand"/>: cierra la
/// vigencia anterior si existe e inserta la nueva en una sola TX.
/// El UNIQUE filtered index <c>ux_aprobadores_departamento_vigente</c>
/// es la guardia final si dos requests concurrentes intentan designar
/// al mismo (empresa, depto, rol).
///
/// <para>
/// El endpoint es responsable de:
/// 1. Verificar el permiso <c>compras.aprobadores.administrar</c>.
/// 2. Validar que <c>UsuarioId</c> existe y está activo en
///    <c>identidad.usuarios</c> (cross-module — Compras no debe
///    conocer al módulo Identidad directamente).
/// </para>
/// </summary>
public sealed class DesignarAprobadorHandler : IRequestHandler<DesignarAprobadorCommand, DesignarAprobadorResponse>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;

    public DesignarAprobadorHandler(
        ComprasDbContext db,
        ICurrentUserContext currentUser,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _currentEmpresa = currentEmpresa;
        _clock = clock;
    }

    public async Task<DesignarAprobadorResponse> Handle(
        DesignarAprobadorCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (_currentUser.UserId is not Guid actorId)
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");

        var now = _clock.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // 1. Cerrar la vigencia anterior si existe.
        var vigente = await _db.AprobadoresDepartamento
            .FirstOrDefaultAsync(
                a => a.EmpresaId == empresaId
                  && a.DepartamentoId == command.DepartamentoId
                  && a.Rol == command.Rol
                  && a.VigenteHasta == null,
                ct);

        if (vigente is not null)
        {
            // Re-designar al mismo usuario: short-circuit, no-op.
            if (vigente.UsuarioId == command.UsuarioId)
            {
                await tx.CommitAsync(ct);
                return new DesignarAprobadorResponse(vigente.Id, vigente.VigenteDesde);
            }

            vigente.Cerrar(now);
        }

        // 2. Insertar el nuevo.
        var nuevo = new AprobadorDepartamento(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            departamentoId: command.DepartamentoId,
            rol: command.Rol,
            usuarioId: command.UsuarioId,
            vigenteDesde: now,
            designadoPor: actorId,
            motivo: command.Motivo,
            createdAt: now);

        _db.AprobadoresDepartamento.Add(nuevo);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new DesignarAprobadorResponse(nuevo.Id, nuevo.VigenteDesde);
    }
}
