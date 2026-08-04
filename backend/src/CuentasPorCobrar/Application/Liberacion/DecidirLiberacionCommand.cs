using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
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
    DateTimeOffset DecididoEn);

internal static class DecisionLiberacionMapper
{
    public static DecisionLiberacionResponse ToResponse(DecisionLiberacion d) =>
        new(d.Id, d.PedidoRef, d.ClienteId, d.Moneda, d.MontoPedido,
            d.CreditoDisponibleSnapshot, d.Resultado, d.ReglaAplicada,
            d.OverrideId, d.DecididoPor, d.DecididoEn);
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
        var snapshot = await CalcularCreditoDisponibleAsync(
            command.ClienteId, command.Moneda, cancellationToken);

        // Cascada 1: regla por serie (match por prefijo más largo activo).
        var comportamiento = await ResolverComportamientoSerieAsync(
            command.PedidoRef, cancellationToken);

        ResultadoLiberacion resultado;
        ReglaAplicadaLiberacion regla;

        if (comportamiento == ComportamientoSerie.SiempreLibera)
        {
            resultado = ResultadoLiberacion.Liberado;
            regla = ReglaAplicadaLiberacion.Serie;
        }
        else
        {
            // Cascada 2: crédito (solo si la serie no bloquea).
            var liberaPorCredito = comportamiento == ComportamientoSerie.EvaluaCredito
                && snapshot.LineaActiva
                && snapshot.Disponible >= command.MontoPedido;

            if (liberaPorCredito)
            {
                resultado = ResultadoLiberacion.Liberado;
                regla = ReglaAplicadaLiberacion.Credito;
            }
            else if (command.OverrideId is Guid overrideId)
            {
                // Cascada 3: override consumible — se valida y consume abajo,
                // en la misma transacción que la decisión.
                resultado = ResultadoLiberacion.LiberadoConOverride;
                regla = ReglaAplicadaLiberacion.Override;
            }
            else
            {
                resultado = ResultadoLiberacion.Retenido;
                regla = comportamiento == ComportamientoSerie.NuncaLibera
                    ? ReglaAplicadaLiberacion.Serie
                    : ReglaAplicadaLiberacion.Credito;
            }
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
            decididoEn: ahora);

        if (resultado == ResultadoLiberacion.LiberadoConOverride)
        {
            var autorizacion = await _db.AutorizacionesCredito
                .FirstOrDefaultAsync(a => a.Id == command.OverrideId, cancellationToken)
                ?? throw new EntityNotFoundException("AC_NO_ENCONTRADA",
                    $"No se encontró la autorización '{command.OverrideId}'.");
            autorizacion.Consumir(decision.Id, usuarioId, ahora);
        }

        _db.DecisionesLiberacion.Add(decision);

        await _eventos.PublishAsync(new DecisionLiberacionEmitidaEvent(
            EmpresaId: empresaId,
            OcurridoEn: ahora,
            DecisionId: decision.Id,
            PedidoRef: decision.PedidoRef,
            ClienteId: decision.ClienteId,
            Moneda: decision.Moneda,
            MontoPedido: decision.MontoPedido,
            Resultado: decision.Resultado.ToString(),
            ReglaAplicada: decision.ReglaAplicada.ToString(),
            CreditoDisponibleSnapshot: decision.CreditoDisponibleSnapshot,
            OverrideId: decision.OverrideId,
            DecididoPor: decision.DecididoPor), cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        return DecisionLiberacionMapper.ToResponse(decision);
    }

    private async Task<(bool LineaActiva, decimal Disponible)> CalcularCreditoDisponibleAsync(
        Guid clienteId, string moneda, CancellationToken cancellationToken)
    {
        var linea = await _db.LineasCredito.AsNoTracking()
            .FirstOrDefaultAsync(l => l.ClienteId == clienteId
                                   && l.Moneda == moneda
                                   && l.Estado == EstadoLineaCredito.Activa,
                cancellationToken);
        if (linea is null) return (false, 0m);

        // Mismo cálculo que CreditoDisponibleQuery: saldo pendiente de las
        // facturas Abierta/Parcial. `liberado_sin_factura` sigue en 0
        // (PLATFORM-TODO(<CreditoLiberadoSinFactura>), gap G1).
        var facturado = await _db.FacturasCartera.AsNoTracking()
            .Where(f => f.ClienteId == clienteId
                     && f.Moneda == moneda
                     && (f.Estado == EstadoFacturaCartera.Abierta
                      || f.Estado == EstadoFacturaCartera.Parcial))
            .SumAsync(f => f.Total - f.MontoPagado - f.MontoNc, cancellationToken);

        return (true, linea.Limite - facturado);
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

        return new PagedResponse<DecisionLiberacionResponse>(
            items.Select(DecisionLiberacionMapper.ToResponse).ToList(),
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
        return DecisionLiberacionMapper.ToResponse(d);
    }
}
