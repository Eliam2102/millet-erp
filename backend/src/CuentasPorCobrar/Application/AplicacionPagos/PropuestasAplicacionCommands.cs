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
// contra el banco. La tolerancia no fiscal es parámetro por moneda
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
    int Version,
    Guid? PropuestoPor = null,
    decimal SaldoAFavorPorIdentificar = 0);

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
            p.Version, p.PropuestoPor, p.SaldoAFavorPorIdentificar);

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
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public CrearPropuestaAplicacionHandler(
        CuentasPorCobrarDbContext db,
        IIntegrationEventPublisher eventos,
        ICurrentEmpresaContext currentEmpresa,
        IOptions<AplicacionPagosOptions> options,
        IClock clock, ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
        _db = db; _eventos = eventos; _currentEmpresa = currentEmpresa;
        _options = options.Value; _clock = clock;
    }

    public async Task<PropuestaAplicacionResponse> Handle(
        CrearPropuestaAplicacionCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");

        if (_currentUser.UserId is not Guid usuarioId || usuarioId == Guid.Empty)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar a quien propone el cobro.");

        // Serializa las reservas concurrentes de las mismas facturas en PostgreSQL.
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (_db.Database.IsRelational())
            foreach (var uuid in command.Facturas.Select(f => f.FacturaUuid).Distinct().Order())
                await _db.FacturasCartera.FromSqlInterpolated(
                    $"SELECT * FROM cuentas_por_cobrar.factura_cartera WHERE empresa_id = {empresaId} AND uuid = {uuid} FOR UPDATE")
                    .ToListAsync(cancellationToken);

        // Matching contra la proyección de cartera: cada UUID del remittance
        // debe ser una factura viva del cliente en la moneda del depósito y
        // con saldo suficiente para el importe propuesto.
        var uuids = command.Facturas.Select(f => f.FacturaUuid).ToList();
        var facturas = await _db.FacturasCartera
            .Where(f => uuids.Contains(f.Uuid))
            .ToDictionaryAsync(f => f.Uuid, cancellationToken);

        var reservas = await _db.PropuestasAplicacionPago
            .Where(p => p.Estado == EstadoPropuestaAplicacion.Propuesta ||
                (p.Estado == EstadoPropuestaAplicacion.Confirmada && !p.ReppTimbrado))
            .SelectMany(p => p.Facturas)
            .Where(f => uuids.Contains(f.FacturaUuid))
            .GroupBy(f => f.FacturaCarteraId)
            .Select(g => new { Id = g.Key, Importe = g.Sum(f => f.ImporteAplicado) })
            .ToDictionaryAsync(x => x.Id, x => x.Importe, cancellationToken);

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
            var disponible = f.SaldoPendiente - reservas.GetValueOrDefault(f.Id);
            if (l.ImporteAplicado > disponible)
                throw new BusinessRuleException("PAP_IMPORTE_EXCEDE_SALDO",
                    $"El importe {l.ImporteAplicado} excede el saldo disponible ({disponible}) después de otras propuestas de la factura {f.Folio}.");

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
            toleranciaNoFiscal: tolerancia, propuestoPor: usuarioId);

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
            PropuestoPor: usuarioId,
            SaldoAFavorPorIdentificar: propuesta.SaldoAFavorPorIdentificar,
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
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return PropuestaAplicacionMapper.ToResponse(
            propuesta, facturas.Values.ToDictionary(f => f.Id, f => f.Folio));
    }
}

// --------------------------------------------------- Listar / Obtener

public sealed record ListarPropuestasAplicacionQuery(
    EstadoPropuestaAplicacion? Estado = null,
    Guid? ClienteId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<PropuestaAplicacionResponse>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public string TipoDocumento => "propuesta_cxc";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

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

        var q = _db.PropuestasAplicacionPago
            .Where(x => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id)).AsNoTracking().Include(p => p.Facturas).AsQueryable();
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
        var p = await _db.PropuestasAplicacionPago.Include(p => p.Facturas)
            .FirstOrDefaultAsync(p => p.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PAP_NO_ENCONTRADA", "No se encontró la propuesta.");
        return await PropuestaAplicacionMapper.ToResponseAsync(_db, p, cancellationToken);
    }
}
