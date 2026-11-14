using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.CartaPorte.Catalogos;

// ---- Vehículo ----

/// <summary>Da de alta un vehículo en el catálogo de Carta Porte (F8).</summary>
public sealed record CrearVehiculoCommand(
    string Placa,
    string ConfigVehicular,
    int AnioModelo,
    string? TipoPermisoSct,
    string? NumPermisoSct,
    string? Aseguradora,
    string? PolizaSeguro,
    // F12-PR3: peso bruto vehicular en toneladas (obligatorio al timbrar CP 3.1).
    decimal? PesoBrutoVehicular = null) : IRequest<CatalogoCreadoResponse>;

/// <summary>Validación estructural; el negocio vive en el dominio.</summary>
public sealed class CrearVehiculoValidator : AbstractValidator<CrearVehiculoCommand>
{
    public CrearVehiculoValidator()
    {
        RuleFor(c => c.Placa).NotEmpty().MaximumLength(20);
        RuleFor(c => c.ConfigVehicular).NotEmpty().MaximumLength(10);
        RuleFor(c => c.AnioModelo).InclusiveBetween(1990, 2100)
            .WithMessage("El año modelo debe estar entre 1990 y 2100.");
        RuleFor(c => c.TipoPermisoSct!).MaximumLength(10).When(c => c.TipoPermisoSct is not null);
        RuleFor(c => c.NumPermisoSct!).MaximumLength(50).When(c => c.NumPermisoSct is not null);
        RuleFor(c => c.Aseguradora!).MaximumLength(100).When(c => c.Aseguradora is not null);
        RuleFor(c => c.PolizaSeguro!).MaximumLength(50).When(c => c.PolizaSeguro is not null);
        RuleFor(c => c.PesoBrutoVehicular).GreaterThan(0)
            .When(c => c.PesoBrutoVehicular is not null)
            .WithMessage("El peso bruto vehicular debe ser mayor que cero (toneladas).");
    }
}

public sealed class CrearVehiculoHandler : IRequestHandler<CrearVehiculoCommand, CatalogoCreadoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;

    public CrearVehiculoHandler(FacturacionDbContext db, ICurrentEmpresaContext empresa)
    {
        _db = db;
        _empresa = empresa;
    }

    public async Task<CatalogoCreadoResponse> Handle(CrearVehiculoCommand command, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var vehiculo = Vehiculo.Crear(empresaId, command.Placa, command.ConfigVehicular, command.AnioModelo,
            command.TipoPermisoSct, command.NumPermisoSct, command.Aseguradora, command.PolizaSeguro,
            command.PesoBrutoVehicular);
        // H-U35: la placa es única por empresa (ix_vehiculo_placa). Se avisa con 409 antes de guardar y, si otra alta
        // gana la carrera, la violación del índice también se traduce a 409 (antes salía 500).
        if (await _db.Vehiculos.AnyAsync(v => v.Placa == vehiculo.Placa, cancellationToken))
            throw CatalogosCartaPorteErrores.PlacaDuplicada(vehiculo.Placa);
        _db.Vehiculos.Add(vehiculo);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CatalogosCartaPorteErrores.EsUnica(ex, "ix_vehiculo_placa"))
        {
            throw CatalogosCartaPorteErrores.PlacaDuplicada(vehiculo.Placa);
        }
        return new CatalogoCreadoResponse(vehiculo.Id);
    }
}

// ---- Operador ----

/// <summary>Da de alta un operador en el catálogo de Carta Porte (F8).</summary>
public sealed record CrearOperadorCommand(string Rfc, string Nombre, string NumLicencia) : IRequest<CatalogoCreadoResponse>;

/// <summary>Validación estructural; el negocio vive en el dominio.</summary>
public sealed class CrearOperadorValidator : AbstractValidator<CrearOperadorCommand>
{
    public CrearOperadorValidator()
    {
        RuleFor(c => c.Rfc).NotEmpty().MaximumLength(13);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.NumLicencia).NotEmpty().MaximumLength(50);
    }
}

public sealed class CrearOperadorHandler : IRequestHandler<CrearOperadorCommand, CatalogoCreadoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresa;

    public CrearOperadorHandler(FacturacionDbContext db, ICurrentEmpresaContext empresa)
    {
        _db = db;
        _empresa = empresa;
    }

    public async Task<CatalogoCreadoResponse> Handle(CrearOperadorCommand command, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "No hay empresa seleccionada en el contexto del request.");

        var operador = Operador.Crear(empresaId, command.Rfc, command.Nombre, command.NumLicencia);
        // H-U35: el RFC del operador es único por empresa (ix_operador_rfc): 409 en lugar de 500.
        if (await _db.Operadores.AnyAsync(o => o.Rfc == operador.Rfc, cancellationToken))
            throw CatalogosCartaPorteErrores.RfcDuplicado(operador.Rfc);
        _db.Operadores.Add(operador);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CatalogosCartaPorteErrores.EsUnica(ex, "ix_operador_rfc"))
        {
            throw CatalogosCartaPorteErrores.RfcDuplicado(operador.Rfc);
        }
        return new CatalogoCreadoResponse(operador.Id);
    }
}

public sealed record CatalogoCreadoResponse(Guid Id);

/// <summary>H-U35: duplicados del catálogo de Carta Porte como 409 con mensaje claro (no 500 del índice único).</summary>
internal static class CatalogosCartaPorteErrores
{
    public static ConflictException PlacaDuplicada(string placa) =>
        new("FAC_CP_VEHICULO_PLACA_DUPLICADA", $"Ya existe un vehículo con la placa {placa}. Búsquelo en el catálogo o capture otra placa.");

    public static ConflictException RfcDuplicado(string rfc) =>
        new("FAC_CP_OPERADOR_RFC_DUPLICADO", $"Ya existe un operador con el RFC {rfc}. Búsquelo en el catálogo o capture otro RFC.");

    public static bool EsUnica(DbUpdateException ex, string indice) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation } pg
        && string.Equals(pg.ConstraintName, indice, StringComparison.Ordinal);
}
