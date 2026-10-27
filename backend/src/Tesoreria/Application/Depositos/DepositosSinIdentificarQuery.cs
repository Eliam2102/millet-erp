using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Ports.Tesoreria;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Depositos;

// ============================================================================
// C7.4 (ficha 26-oct, CA7.9): la bandeja «Sin identificar» también la ve
// Tesorería. Reusa el mismo criterio del puerto que lee CxC
// (DepositosPorIdentificarReadAdapter) y descuenta las propuestas de CxC que ya
// esperan confirmación ligadas al ingreso (su proyección Pendiente con
// movimiento sugerido), sin leer tablas de CxC. Mismo contrato JSON que la
// bandeja de CxC. Alcance por sucursal del ingreso (ADR-0051): el operativo ve
// los de sus sucursales; el ingreso sin sucursal solo lo ve el corporativo.
// ============================================================================

public sealed record DepositoSinIdentificarResponse(
    Guid MovimientoBancarioId,
    string Cuenta,
    DateOnly FechaValor,
    string? Referencia,
    decimal Monto,
    decimal Disponible,
    string Moneda,
    Guid? SucursalId,
    Guid? ClienteId,
    string? ClienteNombre);

public sealed record DepositosSinIdentificarQuery
    : IRequest<IReadOnlyList<DepositoSinIdentificarResponse>>, Millet.SharedKernel.Application.ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "tesoreria.documentos.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class DepositosSinIdentificarHandler(
    TesoreriaDbContext db, IDepositosPorIdentificarReadPort depositos, IClienteReadPort clientes)
    : IRequestHandler<DepositosSinIdentificarQuery, IReadOnlyList<DepositoSinIdentificarResponse>>
{
    public async Task<IReadOnlyList<DepositoSinIdentificarResponse>> Handle(
        DepositosSinIdentificarQuery request, CancellationToken cancellationToken)
    {
        var lista = await depositos.ListarAsync(cancellationToken);
        if (request.SucursalesPermitidas is { } permitidas)
            lista = lista.Where(d => d.SucursalId is Guid s && permitidas.Contains(s)).ToList();
        if (lista.Count == 0) return [];

        var ids = lista.Select(d => (Guid?)d.MovimientoBancarioId).ToList();
        var pendientes = (await db.DepositosConfirmacion.AsNoTracking()
                .Where(d => d.Estado == EstadoDepositoConfirmacion.Pendiente && d.PropuestaCxcId != null
                    && ids.Contains(d.MovimientoId))
                .Select(d => new { MovimientoId = d.MovimientoId!.Value, d.MontoEsperado })
                .ToListAsync(cancellationToken))
            .GroupBy(d => d.MovimientoId)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.MontoEsperado ?? 0m));

        var libres = lista
            .Select(d => (Deposito: d, Libre: d.Disponible - pendientes.GetValueOrDefault(d.MovimientoBancarioId)))
            .Where(x => x.Libre > 0)
            .ToList();
        var clienteIds = libres.Where(x => x.Deposito.ClienteId != null).Select(x => x.Deposito.ClienteId!.Value).Distinct().ToList();
        var nombres = clienteIds.Count == 0
            ? (IReadOnlyDictionary<Guid, ClienteRefDto>)new Dictionary<Guid, ClienteRefDto>()
            : await clientes.ObtenerVariosAsync(clienteIds, cancellationToken);

        return libres
            .Select(x => new DepositoSinIdentificarResponse(
                x.Deposito.MovimientoBancarioId, x.Deposito.Cuenta, x.Deposito.FechaValor, x.Deposito.Referencia,
                x.Deposito.Monto, x.Libre, x.Deposito.Moneda, x.Deposito.SucursalId, x.Deposito.ClienteId,
                x.Deposito.ClienteId is Guid c && nombres.TryGetValue(c, out var n) ? n.RazonSocial : null))
            .ToList();
    }
}
