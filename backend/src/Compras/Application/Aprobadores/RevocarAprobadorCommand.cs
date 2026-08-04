using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Aprobadores;

/// <summary>
/// Revoca al aprobador vigente con el id dado: setea
/// <c>VigenteHasta = now()</c>. Idempotente: si ya estaba cerrado, no-op.
/// 404 si el id no existe en la empresa actual.
/// </summary>
public sealed record RevocarAprobadorCommand(Guid Id) : IRequest;

public sealed class RevocarAprobadorHandler : IRequestHandler<RevocarAprobadorCommand>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;

    public RevocarAprobadorHandler(
        ComprasDbContext db, ICurrentEmpresaContext currentEmpresa, IClock clock)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
        _clock = clock;
    }

    public async Task Handle(RevocarAprobadorCommand command, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException(
                "EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");

        var aprobador = await _db.AprobadoresDepartamento
            .FirstOrDefaultAsync(a => a.Id == command.Id && a.EmpresaId == empresaId, ct)
            ?? throw new EntityNotFoundException(
                "APROBADOR_NO_ENCONTRADO",
                $"No se encontró asignación de aprobador con id '{command.Id}' en la empresa actual.");

        aprobador.Cerrar(_clock.UtcNow);
        await _db.SaveChangesAsync(ct);
    }
}
