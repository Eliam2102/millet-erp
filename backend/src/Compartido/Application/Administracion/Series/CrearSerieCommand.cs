using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Series;

/// <summary>
/// Crea una <see cref="Serie"/> nueva (F-Admin-PR6.1). Duplicado por
/// (Empresa, Sucursal, TipoDocumento, Prefijo, Sufijo) → 409
/// <c>SERIE_DUPLICADA</c>.
///
/// <para>
/// <see cref="Id"/> = <see cref="Guid.Empty"/> ⇒ autogenera con
/// <c>Guid.CreateVersion7()</c>. <see cref="SucursalId"/> = null aplica
/// cross-sucursal.
/// </para>
/// </summary>
public sealed record CrearSerieCommand(
    Guid Id,
    Guid EmpresaId,
    Guid? SucursalId,
    TipoDocumentoSerie TipoDocumento,
    string Prefijo,
    string? Sufijo,
    ReinicioPeriodo ReinicioPeriodo) : IRequest<SerieResponse>;

public sealed class CrearSerieValidator : AbstractValidator<CrearSerieCommand>
{
    public CrearSerieValidator()
    {
        RuleFor(c => c.EmpresaId).NotEmpty();
        RuleFor(c => c.Prefijo).NotEmpty().MaximumLength(10);
        RuleFor(c => c.Sufijo!).MaximumLength(10).When(c => c.Sufijo is not null);
        RuleFor(c => c.TipoDocumento).IsInEnum();
        RuleFor(c => c.ReinicioPeriodo).IsInEnum();
    }
}

public sealed class CrearSerieHandler
    : IRequestHandler<CrearSerieCommand, SerieResponse>
{
    private readonly CompartidoDbContext _db;

    public CrearSerieHandler(CompartidoDbContext db) => _db = db;

    public async Task<SerieResponse> Handle(
        CrearSerieCommand command, CancellationToken cancellationToken)
    {
        // Verificar existencia de empresa.
        var empresaExiste = await _db.Empresas.AsNoTracking()
            .AnyAsync(e => e.Id == command.EmpresaId, cancellationToken);
        if (!empresaExiste)
        {
            throw new EntityNotFoundException(
                "EMPRESA_NO_ENCONTRADA",
                $"No existe empresa con id '{command.EmpresaId}'.");
        }

        var sufijoNorm = command.Sufijo;
        var duplicada = await _db.Series.AsNoTracking()
            .AnyAsync(s =>
                s.EmpresaId == command.EmpresaId
                && s.SucursalId == command.SucursalId
                && s.TipoDocumento == command.TipoDocumento
                && s.Prefijo == command.Prefijo
                && s.Sufijo == sufijoNorm,
                cancellationToken);
        if (duplicada)
        {
            throw new ConflictException(
                "SERIE_DUPLICADA",
                $"Ya existe una serie para (EmpresaId={command.EmpresaId}, " +
                $"SucursalId={command.SucursalId?.ToString() ?? "null"}, " +
                $"TipoDocumento={command.TipoDocumento}, Prefijo='{command.Prefijo}', " +
                $"Sufijo='{command.Sufijo ?? string.Empty}').");
        }

        var id = command.Id == Guid.Empty ? Guid.CreateVersion7() : command.Id;
        var serie = new Serie(
            id,
            command.EmpresaId,
            command.SucursalId,
            command.TipoDocumento,
            command.Prefijo,
            command.Sufijo,
            command.ReinicioPeriodo);

        _db.Series.Add(serie);
        await _db.SaveChangesAsync(cancellationToken);

        return Map(serie);
    }

    internal static SerieResponse Map(Serie s) => new(
        s.Id,
        s.EmpresaId,
        s.SucursalId,
        s.TipoDocumento,
        s.Prefijo,
        s.Sufijo,
        s.ReinicioPeriodo,
        s.Activa,
        s.Version);
}
