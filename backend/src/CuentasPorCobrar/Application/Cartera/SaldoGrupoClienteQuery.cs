using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.Cartera;

// ============================================================================
// 86e3x4g77: saldo del grupo de clientes unificados por RFC (acuerdo Millet
// 27-oct). Cada miembro conserva su propio saldo por moneda (mismo criterio
// que SaldoNetoClienteQuery: nunca se convierte ni se suma al principal).
// Cliente sin grupo → Miembros vacío.
// ============================================================================

public sealed record SaldoMiembroGrupoResponse(
    Guid ClienteId, string Clave, string RazonSocial, bool EsPrincipal,
    IReadOnlyList<SaldoNetoMonedaResponse> Monedas);

public sealed record SaldoGrupoClienteResponse(
    Guid ClienteId, IReadOnlyList<SaldoMiembroGrupoResponse> Miembros);

public sealed record SaldoGrupoClienteQuery(Guid ClienteId) : IRequest<SaldoGrupoClienteResponse>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public string TipoDocumento => "factura_cartera";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class SaldoGrupoClienteHandler(CuentasPorCobrarDbContext db, IClienteReadPort clientes)
    : IRequestHandler<SaldoGrupoClienteQuery, SaldoGrupoClienteResponse>
{
    public async Task<SaldoGrupoClienteResponse> Handle(SaldoGrupoClienteQuery query, CancellationToken cancellationToken)
    {
        var grupo = await clientes.ObtenerGrupoAsync(query.ClienteId, cancellationToken);
        if (grupo.Count == 0) return new(query.ClienteId, []);

        var ids = grupo.Select(m => (Guid?)m.Id).ToList();
        var filas = await db.FacturasCartera
            .Where(x => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id)).AsNoTracking()
            .Where(f => ids.Contains(f.ClienteId) && f.Estado != EstadoFacturaCartera.Cancelada)
            .GroupBy(f => new { f.ClienteId, f.Moneda })
            .Select(g => new
            {
                g.Key.ClienteId,
                Saldo = new SaldoNetoMonedaResponse(
                    g.Key.Moneda,
                    g.Sum(f => f.Total),
                    g.Sum(f => f.MontoPagado),
                    g.Sum(f => f.MontoNc),
                    g.Sum(f => f.Total - f.MontoPagado - f.MontoNc),
                    g.Count(f => f.Estado == EstadoFacturaCartera.Abierta || f.Estado == EstadoFacturaCartera.Parcial)),
            })
            .ToListAsync(cancellationToken);

        return new(query.ClienteId, grupo.Select(m => new SaldoMiembroGrupoResponse(
            m.Id, m.Clave, m.RazonSocial, m.EsPrincipal,
            filas.Where(f => f.ClienteId == m.Id).Select(f => f.Saldo).OrderBy(s => s.Moneda).ToList())).ToList());
    }
}
