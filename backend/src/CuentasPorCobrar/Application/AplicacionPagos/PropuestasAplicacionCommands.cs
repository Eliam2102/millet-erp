using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.AplicacionPagos;

// ============================================================================
// CXC-PR7: propuesta de aplicación de pago. CxC propone el matching
// depósito↔facturas desde el remittance; Ingresos confirma/rechaza
// (interino A2). La tolerancia no fiscal es parámetro por moneda
// (CuentasPorCobrar:AplicacionPagos) — el default MXN es PROVISIONAL
// hasta que fiscal cierre la definición (gate <$50 USD sin CFDI).
// ============================================================================

/// <summary>Tolerancias no fiscales por moneda (levantamiento §2.2).</summary>
public sealed class AplicacionPagosOptions
{
    public const string SectionName = "CuentasPorCobrar:AplicacionPagos";

    /// <summary>
    /// Tolerancia por moneda del depósito. USD=50 observado; MXN=1000 es
    /// aproximación provisional (gate fiscal pendiente). Moneda ausente ⇒
    /// tolerancia 0 (sin ajuste permitido).
    /// </summary>
    public Dictionary<string, decimal> ToleranciaNoFiscal { get; set; } = new()
    {
        ["USD"] = 50m,
        ["MXN"] = 1000m,
    };
}

public sealed record PropuestaFacturaResponse(
    Guid FacturaCarteraId,
    string FacturaUuid,
    string? Folio,
    decimal ImporteAplicado,
    int? NumParcialidad);

public sealed record PropuestaAplicacionResponse(
    Guid Id,
    Guid ClienteId,
    string DepositoRef,
    decimal MontoDeposito,
    string Moneda,
    string RemittanceRef,
    decimal AjusteNoFiscal,
    EstadoPropuestaAplicacion Estado,
    string? MotivoRechazo,
    Guid? ResueltaPor,
    DateTimeOffset? ResueltaEn,
    IReadOnlyList<PropuestaFacturaResponse> Facturas,
    int Version);

internal static class PropuestaAplicacionMapper
{
    /// <summary>
    /// <paramref name="folios"/>: FacturaCarteraId → Folio, para que la UI
    /// muestre el folio en vez del UUID fiscal (feedback 2026-07-14).
    /// </summary>
    public static PropuestaAplicacionResponse ToResponse(
        PropuestaAplicacionPago p, IReadOnlyDictionary<Guid, string> folios) =>
        new(p.Id, p.ClienteId, p.DepositoRef, p.MontoDeposito, p.Moneda, p.RemittanceRef,
            p.AjusteNoFiscal, p.Estado, p.MotivoRechazo, p.ResueltaPor, p.ResueltaEn,
            p.Facturas.Select(f => new PropuestaFacturaResponse(
                f.FacturaCarteraId, f.FacturaUuid,
                folios.TryGetValue(f.FacturaCarteraId, out var folio) ? folio : null,
                f.ImporteAplicado, f.NumParcialidad)).ToList(),
            p.Version);

    public static async Task<PropuestaAplicacionResponse> ToResponseAsync(
        CuentasPorCobrarDbContext db, PropuestaAplicacionPago p, CancellationToken ct) =>
        ToResponse(p, await CargarFoliosAsync(db, [p], ct));

    public static async Task<IReadOnlyDictionary<Guid, string>> CargarFoliosAsync(
        CuentasPorCobrarDbContext db,
        IReadOnlyList<PropuestaAplicacionPago> propuestas,
        CancellationToken ct)
    {
        var ids = propuestas
            .SelectMany(p => p.Facturas.Select(f => f.FacturaCarteraId))
            .Distinct()
            .ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        return await db.FacturasCartera.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Folio, ct);
    }
}

// --------------------------------------------------- Crear

public sealed record PropuestaFacturaLinea(string FacturaUuid, decimal ImporteAplicado, int? NumParcialidad);

public sealed record CrearPropuestaAplicacionCommand(
    Guid ClienteId,
    string DepositoRef,
    decimal MontoDeposito,
    string Moneda,
    string RemittanceRef,
    IReadOnlyList<PropuestaFacturaLinea> Facturas) : IRequest<PropuestaAplicacionResponse>;

public sealed class CrearPropuestaAplicacionValidator : AbstractValidator<CrearPropuestaAplicacionCommand>
{
    public CrearPropuestaAplicacionValidator()
    {
        RuleFor(c => c.ClienteId).NotEmpty();
        RuleFor(c => c.DepositoRef).NotEmpty().MaximumLength(80);
        RuleFor(c => c.MontoDeposito).GreaterThan(0);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.RemittanceRef).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Facturas).NotEmpty();
        RuleForEach(c => c.Facturas).ChildRules(f =>
        {
            f.RuleFor(x => x.FacturaUuid).NotEmpty().MaximumLength(36);
            f.RuleFor(x => x.ImporteAplicado).GreaterThan(0);
        });
    }
}

public sealed class CrearPropuestaAplicacionHandler
    : IRequestHandler<CrearPropuestaAplicacionCommand, PropuestaAplicacionResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly AplicacionPagosOptions _options;
    private readonly IClock _clock;

    public CrearPropuestaAplicacionHandler(
        CuentasPorCobrarDbContext db,
        IIntegrationEventPublisher eventos,
        ICurrentEmpresaContext currentEmpresa,
        IOptions<AplicacionPagosOptions> options,
        IClock clock)
    {
        _db = db; _eventos = eventos; _currentEmpresa = currentEmpresa;
        _options = options.Value; _clock = clock;
    }

    public async Task<PropuestaAplicacionResponse> Handle(
        CrearPropuestaAplicacionCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        // Matching contra la proyección de cartera: cada UUID del remittance
        // debe ser una factura viva del cliente en la moneda del depósito y
        // con saldo suficiente para el importe propuesto.
        var uuids = command.Facturas.Select(f => f.FacturaUuid).ToList();
        var facturas = await _db.FacturasCartera
            .Where(f => uuids.Contains(f.Uuid))
            .ToDictionaryAsync(f => f.Uuid, cancellationToken);

        var lineas = new List<(string, Guid, decimal, int?)>();
        foreach (var l in command.Facturas)
        {
            if (!facturas.TryGetValue(l.FacturaUuid, out var f))
                throw new EntityNotFoundException("PAP_FACTURA_NO_ENCONTRADA",
                    $"La factura {l.FacturaUuid} del remittance no está en cartera.");
            if (f.ClienteId != command.ClienteId)
                throw new BusinessRuleException("PAP_FACTURA_DE_OTRO_CLIENTE",
                    $"La factura {f.Folio} no pertenece al cliente de la propuesta.");
            if (f.Moneda != command.Moneda)
                throw new BusinessRuleException("PAP_MONEDA_DISTINTA",
                    $"La factura {f.Folio} está en {f.Moneda}, no en {command.Moneda} — no se mezclan monedas.");
            if (f.Estado is not (EstadoFacturaCartera.Abierta or EstadoFacturaCartera.Parcial))
                throw new BusinessRuleException("PAP_FACTURA_NO_COBRABLE",
                    $"La factura {f.Folio} no está cobrable (estado: {f.Estado}).");
            if (l.ImporteAplicado > f.SaldoPendiente)
                throw new BusinessRuleException("PAP_IMPORTE_EXCEDE_SALDO",
                    $"El importe {l.ImporteAplicado} excede el saldo pendiente ({f.SaldoPendiente}) de la factura {f.Folio}.");

            lineas.Add((l.FacturaUuid, f.Id, l.ImporteAplicado, l.NumParcialidad));
        }

        var tolerancia = _options.ToleranciaNoFiscal.GetValueOrDefault(command.Moneda, 0m);

        var propuesta = PropuestaAplicacionPago.Crear(
            empresaId: empresaId,
            clienteId: command.ClienteId,
            depositoRef: command.DepositoRef,
            montoDeposito: command.MontoDeposito,
            moneda: command.Moneda,
            remittanceRef: command.RemittanceRef,
            lineas: lineas,
            toleranciaNoFiscal: tolerancia);

        _db.PropuestasAplicacionPago.Add(propuesta);

        await _eventos.PublishAsync(new PropuestaAplicacionPagoCreadaEvent(
            EmpresaId: empresaId,
            OcurridoEn: _clock.UtcNow,
            PropuestaId: propuesta.Id,
            ClienteId: propuesta.ClienteId,
            DepositoRef: propuesta.DepositoRef,
            MontoDeposito: propuesta.MontoDeposito,
            Moneda: propuesta.Moneda,
            AjusteNoFiscal: propuesta.AjusteNoFiscal,
            NumeroFacturas: propuesta.Facturas.Count,
            // TES-PR7: FacturaVentaId (no FacturaCarteraId) — es el id que
            // Facturación entiende cuando Tesorería confirma el depósito.
            Facturas: propuesta.Facturas
                .Select(f =>
                {
                    var cartera = facturas[f.FacturaUuid];
                    return new PropuestaAplicacionFacturaDetalle(
                        cartera.FacturaVentaId, cartera.Folio, f.ImporteAplicado);
                })
                .ToList()), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return PropuestaAplicacionMapper.ToResponse(
            propuesta, facturas.Values.ToDictionary(f => f.Id, f => f.Folio));
    }
}

// --------------------------------------------------- Confirmar / Rechazar (interino A2)

public sealed record ConfirmarPropuestaAplicacionCommand(Guid Id, int VersionEsperada)
    : IRequest<PropuestaAplicacionResponse>;

public sealed class ConfirmarPropuestaAplicacionHandler
    : IRequestHandler<ConfirmarPropuestaAplicacionCommand, PropuestaAplicacionResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public ConfirmarPropuestaAplicacionHandler(
        CuentasPorCobrarDbContext db, ICurrentUserContext currentUser, IClock clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<PropuestaAplicacionResponse> Handle(
        ConfirmarPropuestaAplicacionCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");

        var p = await CargarAsync(_db, command.Id, cancellationToken);
        if (p.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(PropuestaAplicacionPago), p.Id);

        p.Confirmar(usuarioId, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return await PropuestaAplicacionMapper.ToResponseAsync(_db, p, cancellationToken);
    }

    internal static async Task<PropuestaAplicacionPago> CargarAsync(
        CuentasPorCobrarDbContext db, Guid id, CancellationToken ct) =>
        await db.PropuestasAplicacionPago
            .Include(x => x.Facturas)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new EntityNotFoundException("PAP_NO_ENCONTRADA",
            $"No se encontró la propuesta '{id}'.");
}

public sealed record RechazarPropuestaAplicacionCommand(Guid Id, int VersionEsperada, string Motivo)
    : IRequest<PropuestaAplicacionResponse>;

public sealed class RechazarPropuestaAplicacionValidator : AbstractValidator<RechazarPropuestaAplicacionCommand>
{
    public RechazarPropuestaAplicacionValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class RechazarPropuestaAplicacionHandler
    : IRequestHandler<RechazarPropuestaAplicacionCommand, PropuestaAplicacionResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public RechazarPropuestaAplicacionHandler(
        CuentasPorCobrarDbContext db, ICurrentUserContext currentUser, IClock clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<PropuestaAplicacionResponse> Handle(
        RechazarPropuestaAplicacionCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario.");

        var p = await ConfirmarPropuestaAplicacionHandler.CargarAsync(_db, command.Id, cancellationToken);
        if (p.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(PropuestaAplicacionPago), p.Id);

        p.Rechazar(usuarioId, command.Motivo, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return await PropuestaAplicacionMapper.ToResponseAsync(_db, p, cancellationToken);
    }
}

// --------------------------------------------------- Listar / Obtener

public sealed record ListarPropuestasAplicacionQuery(
    EstadoPropuestaAplicacion? Estado = null,
    Guid? ClienteId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<PropuestaAplicacionResponse>>;

public sealed class ListarPropuestasAplicacionHandler
    : IRequestHandler<ListarPropuestasAplicacionQuery, PagedResponse<PropuestaAplicacionResponse>>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ListarPropuestasAplicacionHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<PagedResponse<PropuestaAplicacionResponse>> Handle(
        ListarPropuestasAplicacionQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.PropuestasAplicacionPago.AsNoTracking().Include(p => p.Facturas).AsQueryable();
        if (query.Estado is EstadoPropuestaAplicacion e) q = q.Where(p => p.Estado == e);
        if (query.ClienteId is Guid c) q = q.Where(p => p.ClienteId == c);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        var folios = await PropuestaAplicacionMapper.CargarFoliosAsync(_db, items, cancellationToken);
        return new PagedResponse<PropuestaAplicacionResponse>(
            items.Select(p => PropuestaAplicacionMapper.ToResponse(p, folios)).ToList(),
            offset, limit, total);
    }
}

public sealed record ObtenerPropuestaAplicacionQuery(Guid Id) : IRequest<PropuestaAplicacionResponse>;

public sealed class ObtenerPropuestaAplicacionHandler
    : IRequestHandler<ObtenerPropuestaAplicacionQuery, PropuestaAplicacionResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ObtenerPropuestaAplicacionHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<PropuestaAplicacionResponse> Handle(
        ObtenerPropuestaAplicacionQuery query, CancellationToken cancellationToken)
    {
        var p = await ConfirmarPropuestaAplicacionHandler.CargarAsync(_db, query.Id, cancellationToken);
        return await PropuestaAplicacionMapper.ToResponseAsync(_db, p, cancellationToken);
    }
}
