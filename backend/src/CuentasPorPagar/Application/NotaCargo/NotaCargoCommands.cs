using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.NotaCargo.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.NotaCargo;

// ============================================================================
// F6-PR2: comandos del ciclo NotaCargo (Borrador → Autorizada → Aplicada).
// Formalización con NC fiscal del proveedor entra en F6-PR3 cuando cierre
// el ciclo de devolución con Almacén.
// ============================================================================

public sealed record CrearNotaCargoCommand(
    Guid ProveedorId,
    Guid? SucursalId,
    string Concepto,
    Guid? ConceptoContableId,
    decimal Monto,
    string Moneda,
    decimal? TipoCambio,
    Guid? FacturaOrigenId,
    Guid? DevolucionAProveedorId) : IRequest<CrearNotaCargoResponse>;

public sealed record CrearNotaCargoResponse(Guid Id, string Folio, EstadoNotaCargo Estado, int Version);

public sealed class CrearNotaCargoValidator : AbstractValidator<CrearNotaCargoCommand>
{
    public CrearNotaCargoValidator()
    {
        RuleFor(c => c.ProveedorId).NotEmpty();
        RuleFor(c => c.Concepto).NotEmpty().MaximumLength(400);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.Monto).GreaterThan(0);
    }
}

public sealed class CrearNotaCargoHandler : IRequestHandler<CrearNotaCargoCommand, CrearNotaCargoResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public CrearNotaCargoHandler(
        CuentasPorPagarDbContext db, ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser, IClock clock)
    {
        _db = db; _currentEmpresa = currentEmpresa; _currentUser = currentUser; _clock = clock;
    }

    public async Task<CrearNotaCargoResponse> Handle(CrearNotaCargoCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
        {
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada en el JWT actual.");
        }

        var ahora = _clock.UtcNow;
        var anio = (short)ahora.UtcDateTime.Year;

        // Secuencia atómica: upsert sobre folio_secuencias_nota_cargo
        // por (empresa, año). Mismo patrón que folio_secuencias en
        // Compras (RQ + OC). Garantiza unicidad bajo concurrencia.
        var siguiente = await _db.Database
            .SqlQuery<int>($@"
                INSERT INTO cuentas_por_pagar.folio_secuencias_nota_cargo (empresa_id, anio, siguiente)
                VALUES ({empresaId}, {anio}, 2)
                ON CONFLICT (empresa_id, anio) DO UPDATE
                  SET siguiente = cuentas_por_pagar.folio_secuencias_nota_cargo.siguiente + 1
                RETURNING (siguiente - 1)::int AS ""Value""
            ")
            .ToListAsync(cancellationToken)
            .ContinueWith(t => t.Result.Single(), cancellationToken);

        var folio = FolioInternoNotaCargo.FromAnioSecuencial(anio, siguiente);

        var nota = global::Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo.Crear(
            empresaId: empresaId,
            folio: folio,
            proveedorId: command.ProveedorId,
            sucursalId: command.SucursalId,
            concepto: command.Concepto,
            conceptoContableId: command.ConceptoContableId,
            monto: command.Monto,
            moneda: command.Moneda,
            tipoCambio: command.TipoCambio,
            facturaOrigenId: command.FacturaOrigenId,
            devolucionAProveedorId: command.DevolucionAProveedorId,
            creadoPor: _currentUser.UserId,
            ahora: ahora);

        _db.NotasCargo.Add(nota);
        await _db.SaveChangesAsync(cancellationToken);

        return new CrearNotaCargoResponse(nota.Id, nota.Folio.Valor, nota.Estado, nota.Version);
    }
}

// -----------------------------------------------------------------

public sealed record AutorizarNotaCargoCommand(Guid Id, int VersionEsperada) : IRequest<AutorizarNotaCargoResponse>;

public sealed record AutorizarNotaCargoResponse(Guid Id, EstadoNotaCargo Estado, int Version);

public sealed class AutorizarNotaCargoValidator : AbstractValidator<AutorizarNotaCargoCommand>
{
    public AutorizarNotaCargoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

public sealed class AutorizarNotaCargoHandler : IRequestHandler<AutorizarNotaCargoCommand, AutorizarNotaCargoResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public AutorizarNotaCargoHandler(
        CuentasPorPagarDbContext db, ICurrentUserContext currentUser,
        IMediator mediator, IClock clock)
    {
        _db = db; _currentUser = currentUser; _mediator = mediator; _clock = clock;
    }

    public async Task<AutorizarNotaCargoResponse> Handle(AutorizarNotaCargoCommand command, CancellationToken cancellationToken)
    {
        var nota = await _db.NotasCargo.FirstOrDefaultAsync(n => n.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "NCG_NO_ENCONTRADA",
                $"No se encontró la nota de cargo '{command.Id}'.");
        if (nota.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(NotaCargo), nota.Id);

        var ahora = _clock.UtcNow;
        nota.Autorizar(_currentUser.UserId, ahora);

        await _mediator.Publish(new NotaCargoAutorizadaDomainEvent(
            EmpresaId: nota.EmpresaId,
            NotaCargoId: nota.Id,
            ProveedorId: nota.ProveedorId,
            Monto: nota.Monto,
            FacturaOrigenId: nota.FacturaOrigenId,
            OcurridoEn: ahora), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return new AutorizarNotaCargoResponse(nota.Id, nota.Estado, nota.Version);
    }
}

// -----------------------------------------------------------------

public sealed record AplicarNotaCargoCommand(Guid Id, int VersionEsperada) : IRequest<AplicarNotaCargoResponse>;

public sealed record AplicarNotaCargoResponse(Guid Id, EstadoNotaCargo Estado, int Version);

public sealed class AplicarNotaCargoValidator : AbstractValidator<AplicarNotaCargoCommand>
{
    public AplicarNotaCargoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
    }
}

public sealed class AplicarNotaCargoHandler : IRequestHandler<AplicarNotaCargoCommand, AplicarNotaCargoResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public AplicarNotaCargoHandler(CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IClock clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<AplicarNotaCargoResponse> Handle(AplicarNotaCargoCommand command, CancellationToken cancellationToken)
    {
        var nota = await _db.NotasCargo.FirstOrDefaultAsync(n => n.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "NCG_NO_ENCONTRADA",
                $"No se encontró la nota de cargo '{command.Id}'.");
        if (nota.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(NotaCargo), nota.Id);

        nota.Aplicar(_currentUser.UserId, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return new AplicarNotaCargoResponse(nota.Id, nota.Estado, nota.Version);
    }
}
