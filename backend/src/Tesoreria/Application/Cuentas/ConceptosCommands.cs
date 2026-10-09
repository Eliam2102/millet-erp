using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Cuentas;

public sealed record ConceptoResponse(Guid Id, string Nombre, ClasificacionFlujo ClasificacionFlujo, bool Activo, bool EsEjemplo, int Version);
public sealed record ListarConceptosQuery(bool IncluirInactivos = false) : IRequest<IReadOnlyList<ConceptoResponse>>;
public sealed class ListarConceptosHandler(TesoreriaDbContext db) : IRequestHandler<ListarConceptosQuery, IReadOnlyList<ConceptoResponse>>
{
    public async Task<IReadOnlyList<ConceptoResponse>> Handle(ListarConceptosQuery q, CancellationToken cancellationToken) =>
        await db.ConceptosMovimiento.AsNoTracking().Where(c => q.IncluirInactivos || c.Activo)
            .OrderBy(c => c.ClasificacionFlujo).ThenBy(c => c.Nombre)
            .Select(c => new ConceptoResponse(c.Id, c.Nombre, c.ClasificacionFlujo, c.Activo, c.EsEjemplo, c.Version)).ToListAsync(cancellationToken);
}
public sealed record GuardarConceptoCommand(Guid? Id, string Nombre, ClasificacionFlujo ClasificacionFlujo,
    bool Activo = true, int? VersionEsperada = null) : IRequest<ConceptoResponse>;
public sealed class GuardarConceptoValidator : AbstractValidator<GuardarConceptoCommand>
{
    public GuardarConceptoValidator()
    {
        RuleFor(x => x.Nombre).NotEmpty().MaximumLength(120);
        RuleFor(x => x.ClasificacionFlujo).IsInEnum();
        RuleFor(x => x.VersionEsperada).NotNull().When(x => x.Id != null);
    }
}
public sealed class GuardarConceptoHandler(TesoreriaDbContext db) : IRequestHandler<GuardarConceptoCommand, ConceptoResponse>
{
    public async Task<ConceptoResponse> Handle(GuardarConceptoCommand command, CancellationToken cancellationToken)
    {
        if (await db.ConceptosMovimiento.AnyAsync(c => c.Id != command.Id && c.Nombre == command.Nombre.Trim(), cancellationToken))
            throw new ConflictException("CONCEPTO_DUPLICADO", "Ya existe un concepto con ese nombre.");
        var concepto = command.Id is Guid id
            ? await db.ConceptosMovimiento.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                ?? throw new EntityNotFoundException("CONCEPTO_NO_ENCONTRADO", "No se encontró el concepto.")
            : new ConceptoMovimiento(command.Nombre, command.ClasificacionFlujo);
        if (command.Id is not null && concepto.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(ConceptoMovimiento), concepto.Id);
        concepto.Actualizar(command.Nombre, command.ClasificacionFlujo, command.Activo);
        if (command.Id is null) db.ConceptosMovimiento.Add(concepto);
        await db.SaveChangesAsync(cancellationToken);
        return new(concepto.Id, concepto.Nombre, concepto.ClasificacionFlujo, concepto.Activo, concepto.EsEjemplo, concepto.Version);
    }
}
