using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.CartaPorte.Catalogos;

// ---- List items ----

/// <summary>Renglón del catálogo de vehículos para la pantalla de administración.</summary>
public sealed record VehiculoListItem(
    Guid Id,
    string Placa,
    string ConfigVehicular,
    int AnioModelo,
    string? TipoPermisoSct,
    string? NumPermisoSct,
    string? Aseguradora,
    string? PolizaSeguro,
    decimal? PesoBrutoVehicular,
    bool Activo,
    int Version);

/// <summary>Renglón del catálogo de operadores para la pantalla de administración.</summary>
public sealed record OperadorListItem(
    Guid Id,
    string Rfc,
    string Nombre,
    string NumLicencia,
    bool Activo,
    int Version);

// ---- Listar vehículos ----

/// <summary>
/// Lista el catálogo de vehículos de la empresa en contexto (multi-tenancy
/// vía query filter, ADR-0011). Por default solo activos.
/// </summary>
public sealed record ListarVehiculosQuery(bool IncluirInactivos = false) : IRequest<IReadOnlyList<VehiculoListItem>>;

public sealed class ListarVehiculosHandler : IRequestHandler<ListarVehiculosQuery, IReadOnlyList<VehiculoListItem>>
{
    private readonly FacturacionDbContext _db;
    public ListarVehiculosHandler(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<VehiculoListItem>> Handle(ListarVehiculosQuery query, CancellationToken cancellationToken)
    {
        var q = _db.Vehiculos.AsNoTracking();
        if (!query.IncluirInactivos) q = q.Where(v => v.Activo);

        return await q
            .OrderBy(v => v.Placa)
            .Select(v => new VehiculoListItem(
                v.Id, v.Placa, v.ConfigVehicular, v.AnioModelo,
                v.TipoPermisoSct, v.NumPermisoSct, v.Aseguradora, v.PolizaSeguro,
                v.PesoBrutoVehicular, v.Activo, v.Version))
            .ToListAsync(cancellationToken);
    }
}

// ---- Listar operadores ----

/// <summary>
/// Lista el catálogo de operadores de la empresa en contexto. Por default
/// solo activos.
/// </summary>
public sealed record ListarOperadoresQuery(bool IncluirInactivos = false) : IRequest<IReadOnlyList<OperadorListItem>>;

public sealed class ListarOperadoresHandler : IRequestHandler<ListarOperadoresQuery, IReadOnlyList<OperadorListItem>>
{
    private readonly FacturacionDbContext _db;
    public ListarOperadoresHandler(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<OperadorListItem>> Handle(ListarOperadoresQuery query, CancellationToken cancellationToken)
    {
        var q = _db.Operadores.AsNoTracking();
        if (!query.IncluirInactivos) q = q.Where(o => o.Activo);

        return await q
            .OrderBy(o => o.Rfc)
            .Select(o => new OperadorListItem(o.Id, o.Rfc, o.Nombre, o.NumLicencia, o.Activo, o.Version))
            .ToListAsync(cancellationToken);
    }
}

// ---- Actualizar vehículo ----

/// <summary>
/// PATCH semántico sobre un vehículo del catálogo. Inmutable: <c>Placa</c>
/// (identidad operativa del vehículo). Si viene <c>ConfigVehicular</c> (campo
/// requerido del bloque de datos) se reemplazan TODOS los datos editables —
/// los opcionales enviados como <c>null</c> se limpian. Si viene
/// <c>Activo</c> se aplica Activar/Desactivar (idempotente). Ambos bloques
/// pueden viajar juntos o por separado.
/// </summary>
public sealed record ActualizarVehiculoCommand(
    Guid Id,
    string? ConfigVehicular = null,
    int? AnioModelo = null,
    string? TipoPermisoSct = null,
    string? NumPermisoSct = null,
    string? Aseguradora = null,
    string? PolizaSeguro = null,
    decimal? PesoBrutoVehicular = null,
    bool? Activo = null) : IRequest<VehiculoListItem>;

public sealed class ActualizarVehiculoValidator : AbstractValidator<ActualizarVehiculoCommand>
{
    public ActualizarVehiculoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.ConfigVehicular!).NotEmpty().MaximumLength(10)
            .When(c => c.ConfigVehicular is not null);
        RuleFor(c => c.AnioModelo).NotNull()
            .When(c => c.ConfigVehicular is not null)
            .WithMessage("El año modelo es obligatorio al actualizar los datos del vehículo.");
        RuleFor(c => c.AnioModelo).InclusiveBetween(1990, 2100)
            .When(c => c.AnioModelo is not null)
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

public sealed class ActualizarVehiculoHandler : IRequestHandler<ActualizarVehiculoCommand, VehiculoListItem>
{
    private readonly FacturacionDbContext _db;
    public ActualizarVehiculoHandler(FacturacionDbContext db) => _db = db;

    public async Task<VehiculoListItem> Handle(ActualizarVehiculoCommand command, CancellationToken cancellationToken)
    {
        var vehiculo = await _db.Vehiculos
            .FirstOrDefaultAsync(v => v.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("VEHICULO_NO_ENCONTRADO",
                $"No existe vehículo con id '{command.Id}'.");

        if (command.ConfigVehicular is not null)
        {
            vehiculo.Actualizar(command.ConfigVehicular, command.AnioModelo!.Value,
                command.TipoPermisoSct, command.NumPermisoSct, command.Aseguradora, command.PolizaSeguro,
                command.PesoBrutoVehicular);
        }

        if (command.Activo is bool activo)
        {
            if (activo) vehiculo.Activar();
            else vehiculo.Desactivar();
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new VehiculoListItem(
            vehiculo.Id, vehiculo.Placa, vehiculo.ConfigVehicular, vehiculo.AnioModelo,
            vehiculo.TipoPermisoSct, vehiculo.NumPermisoSct, vehiculo.Aseguradora, vehiculo.PolizaSeguro,
            vehiculo.PesoBrutoVehicular, vehiculo.Activo, vehiculo.Version);
    }
}

// ---- Actualizar operador ----

/// <summary>
/// PATCH semántico sobre un operador del catálogo. Inmutable: <c>Rfc</c>
/// (identidad fiscal del chofer). Si viene <c>Nombre</c> se actualizan
/// nombre y número de licencia (ambos requeridos juntos). Si viene
/// <c>Activo</c> se aplica Activar/Desactivar (idempotente).
/// </summary>
public sealed record ActualizarOperadorCommand(
    Guid Id,
    string? Nombre = null,
    string? NumLicencia = null,
    bool? Activo = null) : IRequest<OperadorListItem>;

public sealed class ActualizarOperadorValidator : AbstractValidator<ActualizarOperadorCommand>
{
    public ActualizarOperadorValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(254)
            .When(c => c.Nombre is not null);
        RuleFor(c => c.NumLicencia!).NotEmpty().MaximumLength(50)
            .When(c => c.Nombre is not null)
            .WithMessage("El número de licencia es obligatorio al actualizar los datos del operador.");
    }
}

public sealed class ActualizarOperadorHandler : IRequestHandler<ActualizarOperadorCommand, OperadorListItem>
{
    private readonly FacturacionDbContext _db;
    public ActualizarOperadorHandler(FacturacionDbContext db) => _db = db;

    public async Task<OperadorListItem> Handle(ActualizarOperadorCommand command, CancellationToken cancellationToken)
    {
        var operador = await _db.Operadores
            .FirstOrDefaultAsync(o => o.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("OPERADOR_NO_ENCONTRADO",
                $"No existe operador con id '{command.Id}'.");

        if (command.Nombre is not null)
            operador.Actualizar(command.Nombre, command.NumLicencia!);

        if (command.Activo is bool activo)
        {
            if (activo) operador.Activar();
            else operador.Desactivar();
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new OperadorListItem(
            operador.Id, operador.Rfc, operador.Nombre, operador.NumLicencia, operador.Activo, operador.Version);
    }
}
