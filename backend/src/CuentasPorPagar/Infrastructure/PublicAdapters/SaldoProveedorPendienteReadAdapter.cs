using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Ports;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;

/// <summary>Implementa <see cref="IPendientesMaestroReadPort"/> con las facturas del proveedor que aún tienen saldo por pagar.</summary>
public sealed class SaldoProveedorPendienteReadAdapter(CuentasPorPagarDbContext db, ICurrentEmpresaContext empresa)
    : IPendientesMaestroReadPort
{
    public async Task<IReadOnlyList<string>> ObtenerAsync(TipoMaestro tipo, Guid id, CancellationToken cancellationToken)
    {
        if (tipo != TipoMaestro.Proveedor) return [];
        using var bypass = empresa.Bypass();
        var facturas = await db.FacturasProveedor.AsNoTracking()
            .Where(f => f.ProveedorId == id && f.Estado != EstadoPasivo.Cancelada && f.Estado != EstadoPasivo.Pagada
                        && f.Total - f.AnticipoAplicadoTotal - f.NcAplicadasTotal - f.CargosAplicadosTotal - f.ImportePagado > 0)
            .Select(f => new
            {
                f.FolioProveedor,
                Saldo = f.Total - f.AnticipoAplicadoTotal - f.NcAplicadasTotal - f.CargosAplicadosTotal - f.ImportePagado,
                f.Moneda,
            }).ToListAsync(cancellationToken);
        return facturas.Select(f => $"Factura {f.FolioProveedor ?? "sin folio"} con saldo por pagar de {f.Saldo:N2} {f.Moneda}.").ToList();
    }
}
