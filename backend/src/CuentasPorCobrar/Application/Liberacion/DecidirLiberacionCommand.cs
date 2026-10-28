using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Liberacion;

// ============================================================================
// CXC-PR4: decisión de liberación de pedidos con cascada
//   serie → crédito → override
// (levantamiento §2). Reglas:
//   1. Serie SiempreLibera (5000/7000)  → Liberado (regla Serie), sin evaluar
//      crédito.
//   2. Serie NuncaLibera (3000/4000/8000) → Retenido (regla Serie), salvo
//      override consumible → LiberadoConOverride.
//   3. EvaluaCredito (default): línea Activa del (cliente, moneda) con
//      disponible ≥ monto del pedido → Liberado (regla Credito); si no,
//      Retenido salvo override → LiberadoConOverride.
//   C7.3 (ClickUp 86e3jvv6n) parte la regla 3 por COBERTURA (disponible ÷
//   monto, ver CoberturaCredito): ≥100 % libera; 50–99.99 % queda
//   PendienteAutorizacion (regla Cobertura) salvo override vigente del
//   MISMO pedido; <50 % retiene y RECHAZA el override (supuesto C7.3 d —
//   solo en la regla de crédito; la serie NuncaLibera sigue aceptándolo).
//   Sin línea activa o línea bloqueada → Retenido (supuesto e) y SIN
//   aceptar override (CXC_SIN_LINEA_ACTIVA). Las series NuncaLibera
//   aceptan override con o sin línea (C7.2 sin cambio).
//   E10: el override es POR PEDIDO — su ClienteOPedidoRef debe ser el
//   folio exacto del pedido (AutorizacionCredito.Consumir).
// La decisión SIEMPRE persiste el snapshot del crédito disponible; el
// override se consume en la MISMA transacción (§4.2). Sin write-back a
// A+W (CXC-PR9).
// ============================================================================

public sealed record DecisionLiberacionResponse(
    Guid Id,
    string PedidoRef,
    Guid ClienteId,
    string Moneda,
    decimal MontoPedido,
    decimal CreditoDisponibleSnapshot,
    ResultadoLiberacion Resultado,
    ReglaAplicadaLiberacion ReglaAplicada,
    Guid? OverrideId,
    Guid DecididoPor,
    DateTimeOffset DecididoEn,
    decimal? PorcentajeCobertura = null,
    Guid? DecisionOrigenId = null,
    Guid? ResolucionId = null,
    string? Motivo = null);

internal static class DecisionLiberacionMapper
{
    /// <param name="resolucionId">C7.3: decisión que resolvió esta pendiente (si la hay).</param>
    public static DecisionLiberacionResponse ToResponse(DecisionLiberacion d, Guid? resolucionId = null) =>
        new(d.Id, d.PedidoRef, d.ClienteId, d.Moneda, d.MontoPedido,
            d.CreditoDisponibleSnapshot, d.Resultado, d.ReglaAplicada,
            d.OverrideId, d.DecididoPor, d.DecididoEn,
            d.PorcentajeCobertura, d.DecisionOrigenId, resolucionId, d.Motivo);

    public static DecisionLiberacionEmitidaEvent ToEvent(DecisionLiberacion d) =>
        new(EmpresaId: d.EmpresaId,
            OcurridoEn: d.DecididoEn,
            DecisionId: d.Id,
            PedidoRef: d.PedidoRef,
            ClienteId: d.ClienteId,
            Moneda: d.Moneda,
            MontoPedido: d.MontoPedido,
            Resultado: d.Resultado.ToString(),
            ReglaAplicada: d.ReglaAplicada.ToString(),
            CreditoDisponibleSnapshot: d.CreditoDisponibleSnapshot,
            OverrideId: d.OverrideId,
            DecididoPor: d.DecididoPor,
            PorcentajeCobertura: d.PorcentajeCobertura,
            DecisionOrigenId: d.DecisionOrigenId,
            Motivo: d.Motivo);

    /// <summary>Mapa decisión pendiente → decisión que la resolvió (C7.3).</summary>
    public static async Task<Dictionary<Guid, Guid>> ResolucionesAsync(
        CuentasPorCobrarDbContext db, IReadOnlyCollection<Guid> pendientes, CancellationToken ct)
    {
        if (pendientes.Count == 0) return [];
        var pares = await db.DecisionesLiberacion.AsNoTracking()
            .Where(x => x.DecisionOrigenId != null && pendientes.Contains(x.DecisionOrigenId.Value))
            .Select(x => new { Origen = x.DecisionOrigenId!.Value, x.Id })
            .ToListAsync(ct);
        return pares.ToDictionary(p => p.Origen, p => p.Id);
    }
}

// --------------------------------------------------- Decidir

public sealed record DecidirLiberacionCommand(
    string PedidoRef,
    Guid ClienteId,
    string Moneda,
    decimal MontoPedido,
    Guid? OverrideId) : IRequest<DecisionLiberacionResponse>;

public sealed class DecidirLiberacionValidator : AbstractValidator<DecidirLiberacionCommand>
{
    public DecidirLiberacionValidator()
    {
        RuleFor(c => c.PedidoRef).NotEmpty().MaximumLength(40);
        RuleFor(c => c.ClienteId).NotEmpty();
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.MontoPedido).GreaterThan(0);
    }
}

public sealed class DecidirLiberacionHandler : IRequestHandler<DecidirLiberacionCommand, DecisionLiberacionResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly IIntegrationEventPublisher _eventos;
    private readonly ICurrentEmpresaContext _currentEmpresa;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public DecidirLiberacionHandler(
        CuentasPorCobrarDbContext db,
        IIntegrationEventPublisher eventos,
        ICurrentEmpresaContext currentEmpresa,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _db = db; _eventos = eventos; _currentEmpresa = currentEmpresa;
        _currentUser = currentUser; _clock = clock;
    }

    public async Task<DecisionLiberacionResponse> Handle(
        DecidirLiberacionCommand command, CancellationToken cancellationToken)
    {
        if (_currentEmpresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "El usuario no tiene una empresa seleccionada.");
        if (_currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO",
                "No se pudo identificar al usuario que decide.");

        var ahora = _clock.UtcNow;

        // Snapshot de crédito: SIEMPRE se calcula, incluso si la serie decide
        // sola — la auditoría de "cuánto crédito había" vale en ambos casos.
        var snapshot = await CreditoLiberacion.CalcularAsync(
            _db, command.ClienteId, command.Moneda, cancellationToken);

        // Cascada 1: regla por serie (match por prefijo más largo activo).
        var comportamiento = await ResolverComportamientoSerieAsync(
            command.PedidoRef, cancellationToken);

        ResultadoLiberacion resultado;
        ReglaAplicadaLiberacion regla;
        decimal? porcentaje = null;

        if (comportamiento == ComportamientoSerie.SiempreLibera)
        {
            resultado = ResultadoLiberacion.Liberado;
            regla = ReglaAplicadaLiberacion.Serie;
        }
        else if (comportamiento == ComportamientoSerie.EvaluaCredito && snapshot.LineaActiva)
        {
            // Cascada 2 con cobertura (C7.3).
            porcentaje = CoberturaCredito.Porcentaje(snapshot.Disponible, command.MontoPedido);
            var tramo = CoberturaCredito.Evaluar(snapshot.Disponible, command.MontoPedido);

            if (tramo == TramoCobertura.Plena)
            {
                resultado = ResultadoLiberacion.Liberado;
                regla = ReglaAplicadaLiberacion.Credito;
            }
            else if (tramo == TramoCobertura.Insuficiente)
            {
                // CA7.3 + supuesto (d): con <50 % no se ofrece autorización
                // parcial; un override explícito se rechaza (no se consume).
                if (command.OverrideId is not null)
                    throw new BusinessRuleException("CXC_COBERTURA_INSUFICIENTE",
                        $"La línea de crédito solo cubre el {CoberturaCredito.PorcentajeEntero(porcentaje.Value)} % del pedido; " +
                        "con menos del 50 % no procede la autorización de CxC y el pedido queda retenido.");
                resultado = ResultadoLiberacion.Retenido;
                regla = ReglaAplicadaLiberacion.Credito;
            }
            else if (command.OverrideId is not null)
            {
                resultado = ResultadoLiberacion.LiberadoConOverride;
                regla = ReglaAplicadaLiberacion.Override;
            }
            else
            {
                resultado = ResultadoLiberacion.PendienteAutorizacion;
                regla = ReglaAplicadaLiberacion.Cobertura;
            }
        }
        else if (comportamiento == ComportamientoSerie.EvaluaCredito)
        {
            // Supuesto (e): sin línea activa (o bloqueada) se retiene y no
            // procede autorización de CxC; el override no se consume.
            if (command.OverrideId is not null)
                throw new BusinessRuleException("CXC_SIN_LINEA_ACTIVA",
                    $"El cliente no tiene línea de crédito activa en {command.Moneda} (no existe o está bloqueada); " +
                    "el pedido queda retenido y no procede la autorización de CxC.");
            resultado = ResultadoLiberacion.Retenido;
            regla = ReglaAplicadaLiberacion.Credito;
        }
        else if (command.OverrideId is not null)
        {
            // Serie NuncaLibera: override consumible con o sin línea
            // (supuesto d: la regla de serie no cambia con C7.3).
            resultado = ResultadoLiberacion.LiberadoConOverride;
            regla = ReglaAplicadaLiberacion.Override;
        }
        else
        {
            resultado = ResultadoLiberacion.Retenido;
            regla = ReglaAplicadaLiberacion.Serie;
        }

        var decision = DecisionLiberacion.Emitir(
            empresaId: empresaId,
            pedidoRef: command.PedidoRef,
            clienteId: command.ClienteId,
            moneda: command.Moneda,
            montoPedido: command.MontoPedido,
            creditoDisponibleSnapshot: snapshot.Disponible,
            resultado: resultado,
            reglaAplicada: regla,
            overrideId: resultado == ResultadoLiberacion.LiberadoConOverride ? command.OverrideId : null,
            decididoPor: usuarioId,
            decididoEn: ahora,
            porcentajeCobertura: porcentaje);

        if (resultado == ResultadoLiberacion.LiberadoConOverride)
        {
            var autorizacion = await _db.AutorizacionesCredito
                .FirstOrDefaultAsync(a => a.Id == command.OverrideId, cancellationToken)
                ?? throw new EntityNotFoundException("AC_NO_ENCONTRADA",
                    $"No se encontró la autorización '{command.OverrideId}'.");
            autorizacion.Consumir(decision.Id, usuarioId, ahora, command.PedidoRef);
        }

        _db.DecisionesLiberacion.Add(decision);

        await _eventos.PublishAsync(DecisionLiberacionMapper.ToEvent(decision), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return DecisionLiberacionMapper.ToResponse(decision);
    }

    private async Task<ComportamientoSerie> ResolverComportamientoSerieAsync(
        string pedidoRef, CancellationToken cancellationToken)
    {
        var folio = pedidoRef.Trim();
        var reglas = await _db.ReglasLiberacionSerie.AsNoTracking()
            .Where(r => r.Activo)
            .ToListAsync(cancellationToken);

        var match = reglas
            .Where(r => folio.StartsWith(r.Prefijo, StringComparison.Ordinal))
            .OrderByDescending(r => r.Prefijo.Length)
            .FirstOrDefault();

        return match?.Comportamiento ?? ComportamientoSerie.EvaluaCredito;
    }
}

// --------------------------------------------------- Listar / Obtener

public sealed record ListarDecisionesLiberacionQuery(
    string? PedidoRef = null,
    Guid? ClienteId = null,
    ResultadoLiberacion? Resultado = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<DecisionLiberacionResponse>>;

public sealed class ListarDecisionesLiberacionHandler
    : IRequestHandler<ListarDecisionesLiberacionQuery, PagedResponse<DecisionLiberacionResponse>>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ListarDecisionesLiberacionHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<PagedResponse<DecisionLiberacionResponse>> Handle(
        ListarDecisionesLiberacionQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.DecisionesLiberacion.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.PedidoRef)) q = q.Where(d => d.PedidoRef == query.PedidoRef);
        if (query.ClienteId is Guid cliente) q = q.Where(d => d.ClienteId == cliente);
        if (query.Resultado is ResultadoLiberacion r) q = q.Where(d => d.Resultado == r);

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(d => d.DecididoEn)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        var resoluciones = await DecisionLiberacionMapper.ResolucionesAsync(
            _db,
            items.Where(d => d.Resultado == ResultadoLiberacion.PendienteAutorizacion).Select(d => d.Id).ToList(),
            cancellationToken);

        return new PagedResponse<DecisionLiberacionResponse>(
            items.Select(d => DecisionLiberacionMapper.ToResponse(
                d, resoluciones.TryGetValue(d.Id, out var r) ? r : null)).ToList(),
            offset, limit, total);
    }
}

public sealed record ObtenerDecisionLiberacionQuery(Guid Id) : IRequest<DecisionLiberacionResponse>;

public sealed class ObtenerDecisionLiberacionHandler
    : IRequestHandler<ObtenerDecisionLiberacionQuery, DecisionLiberacionResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ObtenerDecisionLiberacionHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<DecisionLiberacionResponse> Handle(
        ObtenerDecisionLiberacionQuery query, CancellationToken cancellationToken)
    {
        var d = await _db.DecisionesLiberacion.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("DL_NO_ENCONTRADA",
                $"No se encontró la decisión '{query.Id}'.");
        var resoluciones = d.Resultado == ResultadoLiberacion.PendienteAutorizacion
            ? await DecisionLiberacionMapper.ResolucionesAsync(_db, new[] { d.Id }, cancellationToken)
            : new Dictionary<Guid, Guid>();
        return DecisionLiberacionMapper.ToResponse(
            d, resoluciones.TryGetValue(d.Id, out var r) ? r : null);
    }
}
