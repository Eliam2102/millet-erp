using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Ports.Tesoreria;
using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Infrastructure.PublicAdapters;

/// <summary>
/// Adaptador de <see cref="IDepositosPorIdentificarReadPort"/> (C7.4 · CA7.9):
/// ingresos bancarios de cliente que siguen sin identificar. Tesorería es dueña
/// del movimiento; CxC solo lo lee por aquí. El filtro global limita a la
/// empresa del request.
///
/// <para>
/// Criterio: ingreso, no contramovimiento ni revertido, beneficiario tipo
/// Cliente (cliente opcional), no aplicado por completo, sin devolución
/// solicitada/autorizada/pagada y no ligado a una expectativa de caja o de
/// viáticos. <c>Disponible</c> = monto menos lo ya confirmado (el saldo a
/// favor de una confirmación previa queda disponible).
/// </para>
/// </summary>
public sealed class DepositosPorIdentificarReadAdapter(TesoreriaDbContext db) : IDepositosPorIdentificarReadPort
{
    public Task<IReadOnlyList<DepositoPorIdentificarDto>> ListarAsync(CancellationToken cancellationToken) =>
        ConsultarAsync(null, cancellationToken);

    public async Task<DepositoPorIdentificarDto?> ObtenerAsync(Guid movimientoBancarioId, CancellationToken cancellationToken) =>
        (await ConsultarAsync(movimientoBancarioId, cancellationToken)).SingleOrDefault();

    private async Task<IReadOnlyList<DepositoPorIdentificarDto>> ConsultarAsync(Guid? id, CancellationToken ct)
    {
        var q = db.MovimientosBancarios.AsNoTracking()
            .Where(m => m.Sentido == SentidoMovimiento.Ingreso
                && m.ContramovimientoDe == null
                && m.BeneficiarioTipo == BeneficiarioTipo.Cliente
                && m.EstadoAplicacion != EstadoAplicacionMovimiento.Aplicado
                && !db.MovimientosBancarios.Any(c => c.ContramovimientoDe == m.Id)
                && !db.DevolucionesCliente.Any(d => d.MovimientoOrigenId == m.Id
                    && (d.Estado == EstadoDevolucionCliente.Solicitada
                        || d.Estado == EstadoDevolucionCliente.Autorizada
                        || d.Estado == EstadoDevolucionCliente.Pagada))
                && !db.DepositosConfirmacion.Any(d => d.MovimientoId == m.Id
                    && d.Estado == EstadoDepositoConfirmacion.Confirmada
                    && (d.CajaSesionId != null || d.SolicitudViaticosId != null)));
        if (id is Guid movimientoId) q = q.Where(m => m.Id == movimientoId);

        var movimientos = await q.OrderBy(m => m.FechaValor).ThenBy(m => m.CreadoEn).ToListAsync(ct);
        if (movimientos.Count == 0) return [];

        var aplicado = await DepositoDisponible.AplicadoPorMovimientoAsync(db, movimientos, ct);
        var ids = movimientos.Select(m => (Guid?)m.Id).ToArray();
        var confirmadasCxc = await db.DepositosConfirmacion.AsNoTracking()
                .Where(d => d.Estado == EstadoDepositoConfirmacion.Confirmada && d.PropuestaCxcId != null && ids.Contains(d.MovimientoId))
                .Select(d => new { MovimientoId = d.MovimientoId!.Value, PropuestaId = d.PropuestaCxcId!.Value, d.ClienteId, d.ResueltaEn })
                .ToListAsync(ct);
        var confirmadas = confirmadasCxc
            .GroupBy(x => x.MovimientoId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.PropuestaId).ToList());
        // E9 · C7.4-b: si el ingreso llegó sin cliente, su sobrante es «sin identificar»
        // del cliente de la última propuesta confirmada contra él (sin CFDI ni anticipo).
        var clienteDeSobrante = confirmadasCxc
            .Where(x => x.ClienteId != null)
            .GroupBy(x => x.MovimientoId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ResueltaEn).First().ClienteId);
        var cuentaIds = movimientos.Select(m => m.CuentaBancariaId).Distinct().ToArray();
        var cuentas = await db.CuentasBancarias.AsNoTracking()
            .Where(c => cuentaIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => $"{c.Banco} {Enmascarar(c.NumeroCuenta)}", ct);

        return movimientos
            .Select(m => new DepositoPorIdentificarDto(
                m.Id, m.CuentaBancariaId, cuentas.GetValueOrDefault(m.CuentaBancariaId) ?? "",
                m.FechaValor, m.ReferenciaBancaria, m.Monto,
                DepositoDisponible.Disponible(m, aplicado.GetValueOrDefault(m.Id)),
                m.Moneda, m.SucursalId, m.BeneficiarioRef ?? clienteDeSobrante.GetValueOrDefault(m.Id),
                confirmadas.GetValueOrDefault(m.Id) ?? []))
            .Where(d => d.Disponible > 0)
            .ToList();
    }

    private static string Enmascarar(string numero) =>
        numero.Length <= 4 ? numero : $"****{numero[^4..]}";
}
