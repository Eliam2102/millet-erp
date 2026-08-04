using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.Application.RfcsReceptores.AgregarRfcReceptor;

public sealed record AgregarRfcReceptorCommand(
    Guid EmpresaId,
    string Rfc) : IRequest<RfcReceptorResponse>;

public sealed class AgregarRfcReceptorValidator : AbstractValidator<AgregarRfcReceptorCommand>
{
    public AgregarRfcReceptorValidator()
    {
        RuleFor(c => c.EmpresaId).NotEqual(Guid.Empty);
        RuleFor(c => c.Rfc).NotEmpty().Length(12, 13);
    }
}

public sealed class AgregarRfcReceptorHandler
    : IRequestHandler<AgregarRfcReceptorCommand, RfcReceptorResponse>
{
    private readonly IntegracionesFiscalDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly IClock _clock;

    public AgregarRfcReceptorHandler(
        IntegracionesFiscalDbContext db,
        ICurrentEmpresaContext currentEmpresa,
        IClock clock)
    {
        _db = db;
        _currentEmpresa = currentEmpresa;
        _clock = clock;
    }

    public async Task<RfcReceptorResponse> Handle(
        AgregarRfcReceptorCommand command, CancellationToken cancellationToken)
    {
        var empresaActual = _currentEmpresa.Current
            ?? throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT.");
        if (empresaActual != command.EmpresaId)
            throw new CrossTenantViolationException(
                nameof(RfcReceptor), empresaActual, command.EmpresaId);

        var rfcNormalizado = command.Rfc.Trim().ToUpperInvariant();

        // Pre-check de unicidad para devolver un 409 limpio. La UNIQUE
        // index del schema es el guard final si dos requests entran
        // concurrentes con el mismo RFC.
        var existe = await _db.RfcsReceptores
            .AsNoTracking()
            .AnyAsync(r => r.EmpresaId == command.EmpresaId && r.Rfc == rfcNormalizado,
                cancellationToken);

        if (existe)
            throw new ConflictException("RFC_RECEPTOR_DUPLICADO",
                $"Ya existe un RFC '{rfcNormalizado}' para esta empresa.");

        var entity = new RfcReceptor(
            id: Guid.CreateVersion7(),
            empresaId: command.EmpresaId,
            rfc: command.Rfc);

        _db.RfcsReceptores.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return entity.ToResponse(_clock.UtcNow);
    }
}
