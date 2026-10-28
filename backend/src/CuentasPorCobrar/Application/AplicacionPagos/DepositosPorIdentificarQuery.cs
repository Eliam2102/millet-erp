using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Domain.Ports.Tesoreria;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.AplicacionPagos;

// ============================================================================
// F1-CXC-03 · C7.4 · CA7.9 (regla R13): bandeja «Sin identificar». Un depósito
// que llega sin detalle (remittance) queda sin identificar y NO se aplica hasta
// que el cliente manda el detalle; entonces CxC lo identifica proponiendo la
// aplicación (CrearPropuestaAplicacionCommand con MovimientoBancarioId) y
// Tesorería confirma (R12). Fuente: IDepositosPorIdentificarReadPort de
// Tesorería; aquí solo se descuentan las propuestas de CxC que esperan
// confirmación contra el mismo depósito. La alta automática desde el banco
// sigue pendiente: PLATFORM-TODO(<DepositoBancarioFeed>) — hoy el depósito lo
// registra Tesorería a mano (POST /api/v1/tesoreria/movimientos).
// ============================================================================

public sealed record DepositoPorIdentificarResponse(
    Guid MovimientoBancarioId,
    string Cuenta,
    DateOnly FechaValor,
    string? Referencia,
    decimal Monto,
    // Por identificar: disponible en Tesorería menos propuestas pendientes de CxC.
    decimal Disponible,
    string Moneda,
    Guid? SucursalId,
    Guid? ClienteId,
    string? ClienteNombre);

/// <summary>
/// ADR-0051: un usuario operativo ve los depósitos de sus sucursales; el
/// corporativo (bypass <c>cuentas_por_cobrar.cartera.leer-todas-sucursales</c>)
/// ve todos, incluidos los registrados sin sucursal.
/// </summary>
public sealed record DepositosPorIdentificarQuery
    : IRequest<IReadOnlyList<DepositoPorIdentificarResponse>>, Millet.SharedKernel.Application.ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class DepositosPorIdentificarHandler(
    CuentasPorCobrarDbContext db,
    IDepositosPorIdentificarReadPort depositos,
    IClienteReadPort? clientes = null)
    : IRequestHandler<DepositosPorIdentificarQuery, IReadOnlyList<DepositoPorIdentificarResponse>>
{
    public async Task<IReadOnlyList<DepositoPorIdentificarResponse>> Handle(
        DepositosPorIdentificarQuery query, CancellationToken cancellationToken)
    {
        var lista = await depositos.ListarAsync(cancellationToken);
        if (query.SucursalesPermitidas is { } permitidas)
            lista = lista.Where(d => d.SucursalId is Guid s && permitidas.Contains(s)).ToList();
        if (lista.Count == 0) return [];

        var ids = lista.Select(d => (Guid?)d.MovimientoBancarioId).ToList();
        var confirmadas = lista.SelectMany(d => d.PropuestasConfirmadas ?? []).ToHashSet();
        var reservado = (await db.PropuestasAplicacionPago.AsNoTracking()
                .Where(p => p.Estado == EstadoPropuestaAplicacion.Propuesta && ids.Contains(p.MovimientoBancarioId))
                .Select(p => new { p.Id, MovimientoId = p.MovimientoBancarioId!.Value, p.MontoDeposito })
                .ToListAsync(cancellationToken))
            .Where(p => !confirmadas.Contains(p.Id)) // ya descontadas por Tesorería
            .GroupBy(p => p.MovimientoId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.MontoDeposito));

        var porIdentificar = lista
            .Select(d => (Deposito: d, Libre: d.Disponible - reservado.GetValueOrDefault(d.MovimientoBancarioId)))
            .Where(x => x.Libre > 0)
            .ToList();

        var clienteIds = porIdentificar.Where(x => x.Deposito.ClienteId != null)
            .Select(x => x.Deposito.ClienteId!.Value).Distinct().ToList();
        var nombres = clientes is null || clienteIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await clientes.BuscarAsync(null, null, clienteIds, clienteIds.Count, cancellationToken))
                .ToDictionary(c => c.Id, c => c.RazonSocial);

        return porIdentificar
            .Select(x => new DepositoPorIdentificarResponse(
                x.Deposito.MovimientoBancarioId, x.Deposito.Cuenta, x.Deposito.FechaValor, x.Deposito.Referencia,
                x.Deposito.Monto, x.Libre, x.Deposito.Moneda, x.Deposito.SucursalId, x.Deposito.ClienteId,
                x.Deposito.ClienteId is Guid c ? nombres.GetValueOrDefault(c) : null))
            .ToList();
    }
}
