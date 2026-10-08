using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Empresas;

/// <summary>
/// PATCH parcial sobre una empresa existente (F-Admin-PR2.3). Convención
/// del repo: campos <c>null</c> en el command = no tocar el atributo;
/// para limpiar un nullable se usa su flag <c>LimpiarX</c> = <c>true</c>
/// (<see cref="LimpiarNombreComercial"/>, <see cref="LimpiarTasaIvaDefault"/>).
///
/// <para>404 si la empresa no existe.</para>
/// </summary>
public sealed record ActualizarEmpresaCommand(
    Guid Id,
    string? RazonSocial,
    string? NombreComercial,
    string? RegimenFiscal,
    bool LimpiarNombreComercial,
    decimal? TasaIvaDefault = null,
    bool LimpiarTasaIvaDefault = false,
    string? CodigoPostal = null,
    int? VersionEsperada = null,
    string? Calle = null,
    string? NumeroExterior = null,
    string? NumeroInterior = null,
    string? Colonia = null,
    string? Ciudad = null,
    string? Municipio = null,
    string? Estado = null,
    string? Pais = null,
    bool LimpiarNumeroInterior = false) : IRequest<EmpresaResponse>;

public sealed class ActualizarEmpresaValidator : AbstractValidator<ActualizarEmpresaCommand>
{
    public ActualizarEmpresaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).NotNull().GreaterThanOrEqualTo(0);
        RuleFor(c => c.Calle!).NotEmpty().MaximumLength(254)
            .When(c => c.Calle is not null);
        RuleFor(c => c.NumeroExterior!).NotEmpty().MaximumLength(20)
            .When(c => c.NumeroExterior is not null);
        RuleFor(c => c.NumeroInterior!).MaximumLength(20)
            .When(c => c.NumeroInterior is not null);
        RuleFor(c => c.Colonia!).NotEmpty().MaximumLength(254)
            .When(c => c.Colonia is not null);
        RuleFor(c => c.Ciudad!).NotEmpty().MaximumLength(100)
            .When(c => c.Ciudad is not null);
        RuleFor(c => c.Municipio!).NotEmpty().MaximumLength(100)
            .When(c => c.Municipio is not null);
        RuleFor(c => c.Estado!).NotEmpty().MaximumLength(100)
            .When(c => c.Estado is not null);
        RuleFor(c => c.Pais!).NotEmpty().MaximumLength(100)
            .When(c => c.Pais is not null);

        RuleFor(c => c.RazonSocial!).NotEmpty().MaximumLength(254)
            .When(c => c.RazonSocial is not null);
        RuleFor(c => c.NombreComercial!).MaximumLength(254)
            .When(c => c.NombreComercial is not null);
        RuleFor(c => c.RegimenFiscal!).NotEmpty().MaximumLength(10)
            .When(c => c.RegimenFiscal is not null);
        RuleFor(c => c.TasaIvaDefault!.Value).InclusiveBetween(0m, 1m)
            .When(c => c.TasaIvaDefault is not null)
            .WithMessage("La tasa de IVA default debe ser fracción entre 0 y 1 (p.ej. 0.16).");
        RuleFor(c => c.CodigoPostal!).Matches(@"^\d{5}$")
            .When(c => c.CodigoPostal is not null)
            .WithMessage("El código postal fiscal debe ser de 5 dígitos.");
    }
}

public sealed class ActualizarEmpresaHandler
    : IRequestHandler<ActualizarEmpresaCommand, EmpresaResponse>
{
    private readonly CompartidoDbContext _db;

    public ActualizarEmpresaHandler(CompartidoDbContext db) => _db = db;

    public async Task<EmpresaResponse> Handle(
        ActualizarEmpresaCommand command, CancellationToken cancellationToken)
    {
        var empresa = await _db.Empresas
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "EMPRESA_NO_ENCONTRADA",
                $"No existe empresa con id '{command.Id}'.");

        if (empresa.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Empresa), empresa.Id);

        empresa.ActualizarDatos(
            razonSocial: command.RazonSocial,
            nombreComercial: command.NombreComercial,
            regimenFiscal: command.RegimenFiscal,
            limpiarNombreComercial: command.LimpiarNombreComercial,
            tasaIvaDefault: command.TasaIvaDefault,
            limpiarTasaIvaDefault: command.LimpiarTasaIvaDefault,
            codigoPostal: command.CodigoPostal,
            calle: command.Calle,
            numeroExterior: command.NumeroExterior,
            numeroInterior: command.NumeroInterior,
            colonia: command.Colonia,
            ciudad: command.Ciudad,
            municipio: command.Municipio,
            estado: command.Estado,
            pais: command.Pais,
            limpiarNumeroInterior: command.LimpiarNumeroInterior);

        await _db.SaveChangesAsync(cancellationToken);

        return new EmpresaResponse(
            empresa.Id,
            empresa.Rfc,
            empresa.RazonSocial,
            empresa.NombreComercial,
            empresa.RegimenFiscal,
            empresa.TasaIvaDefault,
            empresa.CodigoPostal,
            empresa.Activa,
            empresa.Version,
            empresa.Calle, empresa.NumeroExterior, empresa.NumeroInterior, empresa.Colonia, empresa.Ciudad, empresa.Municipio, empresa.Estado, empresa.Pais);
    }
}
