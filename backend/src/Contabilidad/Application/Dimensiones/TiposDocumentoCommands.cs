using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Contabilidad.Application.Catalogo;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Dimensiones;

/// <summary>K10.9: el tipo de documento es también el tipo de operación de la póliza, con su tipo de póliza del SAT.</summary>
public sealed record TipoDocumentoResponse(Guid Id, string Clave, string Nombre, bool Activo, bool EsPrueba, int Version, TipoPolizaSat TipoSat)
{
    public static TipoDocumentoResponse De(TipoDocumentoContable t) => new(t.Id, t.Clave, t.Nombre, t.Activo, t.EsPrueba, t.Version, t.TipoSat);
}

public sealed record ListarTiposDocumentoQuery(bool IncluirInactivos) : IRequest<IReadOnlyList<TipoDocumentoResponse>>;

public sealed class ListarTiposDocumentoHandler(ContabilidadDbContext db) : IRequestHandler<ListarTiposDocumentoQuery, IReadOnlyList<TipoDocumentoResponse>>
{
    public async Task<IReadOnlyList<TipoDocumentoResponse>> Handle(ListarTiposDocumentoQuery request, CancellationToken cancellationToken)
    {
        var q = db.TiposDocumento.AsNoTracking();
        if (!request.IncluirInactivos) q = q.Where(t => t.Estatus == EstatusCatalogo.Activo);
        return [.. (await q.OrderBy(t => t.Clave).ToListAsync(cancellationToken)).Select(TipoDocumentoResponse.De)];
    }
}

/// <summary>Un tipo real (no de prueba) lleva clave de dos letras (K10.9); <c>TipoSat</c> por omisión es diario.</summary>
public sealed record CrearTipoDocumentoCommand(string Clave, string Nombre, bool EsPrueba, TipoPolizaSat TipoSat = TipoPolizaSat.Diario) : IRequest<TipoDocumentoResponse>;

public sealed class CrearTipoDocumentoValidator : AbstractValidator<CrearTipoDocumentoCommand>
{
    public CrearTipoDocumentoValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(120);
        RuleFor(c => c.TipoSat).IsInEnum();
        RuleFor(c => c.Clave).Must(c => TipoDocumentoContable.EsClaveDeOperacion(c.Trim().ToUpperInvariant()))
            .When(c => !c.EsPrueba && !string.IsNullOrWhiteSpace(c.Clave))
            .WithMessage("La clave del tipo de operación son dos letras (TT, PC, RF…).");
    }
}

public sealed class CrearTipoDocumentoHandler(ContabilidadDbContext db) : IRequestHandler<CrearTipoDocumentoCommand, TipoDocumentoResponse>
{
    public async Task<TipoDocumentoResponse> Handle(CrearTipoDocumentoCommand request, CancellationToken cancellationToken)
    {
        var clave = request.Clave.Trim().ToUpperInvariant();
        if (await db.TiposDocumento.AnyAsync(t => t.Clave == clave, cancellationToken))
            throw new ConflictException("CONTAB_TIPO_DOC_DUPLICADO", $"Ya existe un tipo de documento con la clave {clave}.");
        var tipo = new TipoDocumentoContable(Guid.CreateVersion7(), clave, request.Nombre.Trim(), request.EsPrueba, request.TipoSat);
        db.TiposDocumento.Add(tipo);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException e) when (PoliticaCatalogo.EsViolacionUnica(e, "ux_tipos_documento_clave"))
        {
            throw new ConflictException("CONTAB_TIPO_DOC_DUPLICADO", $"Ya existe un tipo de documento con la clave {clave}.");
        }
        return TipoDocumentoResponse.De(tipo);
    }
}

/// <summary>La clave es inmutable (la usan los movimientos como snapshot). Desactivar no borra: las reglas y movimientos se conservan.</summary>
public sealed record EditarTipoDocumentoCommand(Guid Id, int VersionEsperada, string Nombre, bool Activo, TipoPolizaSat? TipoSat = null) : IRequest<TipoDocumentoResponse>;

public sealed class EditarTipoDocumentoValidator : AbstractValidator<EditarTipoDocumentoCommand>
{
    public EditarTipoDocumentoValidator() => RuleFor(c => c.Nombre).NotEmpty().MaximumLength(120);
}

public sealed class EditarTipoDocumentoHandler(ContabilidadDbContext db) : IRequestHandler<EditarTipoDocumentoCommand, TipoDocumentoResponse>
{
    public async Task<TipoDocumentoResponse> Handle(EditarTipoDocumentoCommand request, CancellationToken cancellationToken)
    {
        var tipo = await db.TiposDocumento.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_TIPO_DOC_NO_ENCONTRADO", $"No existe el tipo de documento '{request.Id}'.");
        if (tipo.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(TipoDocumentoContable), request.Id);
        tipo.Editar(request.Nombre.Trim(), request.TipoSat ?? tipo.TipoSat);
        tipo.CambiarEstatus(request.Activo);
        await db.SaveChangesAsync(cancellationToken);
        return TipoDocumentoResponse.De(tipo);
    }
}
