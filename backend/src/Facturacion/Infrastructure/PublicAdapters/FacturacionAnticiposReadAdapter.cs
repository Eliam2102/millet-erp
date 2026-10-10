using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter público de <see cref="IFacturacionAnticiposReadPort"/>
/// (CXC-PR3 — cierra el gap G-anticipos-port del levantamiento de CxC:
/// promoción de <c>AnticipoSaldoDetalle</c> a contrato consumible).
///
/// <para>Patrón de puerto inverso cross-módulo (mismo que
/// <c>CxpFacturasTrazabilidadProvider</c> en CxP): la interfaz vive en el
/// módulo consumidor (CxC), el adapter en el módulo dueño del dato
/// (Facturación). Lectura <c>AsNoTracking</c> sobre
/// <c>facturacion.anticipos</c> — CxC nunca toca este esquema.</para>
/// </summary>
public sealed class FacturacionAnticiposReadAdapter : IFacturacionAnticiposReadPort
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public FacturacionAnticiposReadAdapter(
        FacturacionDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyList<AnticipoSaldoClienteDto>> ListarPorClienteAsync(
        Guid clienteId, CancellationToken cancellationToken)
    {

        var anticipos = await _db.Anticipos.AsNoTracking()
            .Include(a => a.Vinculaciones)
            .Where(a => a.ClienteId == clienteId && a.Estado != EstadoAnticipo.Cancelado)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var comprobantes = anticipos.Select(x => x.FacturaAnticipoId).ToArray();
        var sucursales = await _db.Comprobantes.AsNoTracking().Where(x => comprobantes.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.SucursalId, cancellationToken);
        return anticipos.Select(a => new AnticipoSaldoClienteDto(
            AnticipoId: a.Id,
            ClienteId: a.ClienteId,
            Estado: a.Estado.ToString(),
            MontoCobrado: a.MontoCobrado,
            MontoAmortizado: a.MontoAmortizado,
            Saldo: a.Saldo,
            SaldoDisponible: a.SaldoDisponible,
            Moneda: a.Moneda,
            PedidoOrigenRef: a.PedidoOrigenRef,
            SucursalId: sucursales.TryGetValue(a.FacturaAnticipoId, out var sucursal) ? sucursal : null)).ToList();
    }
}
