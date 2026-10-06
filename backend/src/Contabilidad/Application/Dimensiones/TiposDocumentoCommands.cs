using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Contabilidad.Application.Catalogo;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Dimensiones;

public sealed record TipoDocumentoResponse(Guid Id, string Clave, string Nombre, bool Activo, bool EsPrueba, int Version)
{
    public static TipoDocumentoResponse De(TipoDocumentoContable t) => new(t.Id, t.Clave, t.Nombre, t.Activo, t.EsPrueba, t.Version);
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

public sealed record CrearTipoDocumentoCommand(string Clave, string Nombre, bool EsPrueba) : IRequest<TipoDocumentoResponse>;

public sealed class CrearTipoDocumentoValidator : AbstractValidator<CrearTipoDocumentoCommand>
{
    public CrearTipoDocumentoValidator()
    {
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(120);
    }
}

public sealed class CrearTipoDocumentoHandler(ContabilidadDbContext db) : IRequestHandler<CrearTipoDocumentoCommand, TipoDocumentoResponse>
{
    public async Task<TipoDocumentoResponse> Handle(CrearTipoDocumentoCommand request, CancellationToken cancellationToken)
    {
        var clave = request.Clave.Trim().ToUpperInvariant();
        if (await db.TiposDocumento.AnyAsync(t => t.Clave == clave, cancellationToken))
            throw new ConflictException("CONTAB_TIPO_DOC_DUPLICADO", $"Ya existe un tipo de documento con la clave {clave}.");
        var tipo = new TipoDocumentoContable(Guid.CreateVersion7(), clave, request.Nombre.Trim(), request.EsPrueba);
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
public sealed record EditarTipoDocumentoCommand(Guid Id, int VersionEsperada, string Nombre, bool Activo) : IRequest<TipoDocumentoResponse>;

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
        tipo.Editar(request.Nombre.Trim());
        tipo.CambiarEstatus(request.Activo);
        await db.SaveChangesAsync(cancellationToken);
        return TipoDocumentoResponse.De(tipo);
    }
}
