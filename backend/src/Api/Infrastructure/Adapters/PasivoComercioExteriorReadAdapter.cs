using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// Adapter de <see cref="IPasivoComercioExteriorReadPort"/> (I13.3). Vive en Api porque Tesorería no referencia
/// Comercio Exterior ni Compras. Bypass del filtro de empresa: lo invoca el listener de CxP, fuera de un request.
/// </summary>
public sealed class PasivoComercioExteriorReadAdapter(
    ComercioExteriorDbContext ce, ComprasDbContext compras, ICurrentEmpresaContext empresaContext) : IPasivoComercioExteriorReadPort
{
    public async Task<TipoOperacionComercioExterior?> TipoAsync(Guid facturaProveedorId, Guid? ordenCompraId, CancellationToken cancellationToken)
    {
        using var bypass = empresaContext.Bypass();
        var tipoAnticipo = await (from a in ce.AnticiposAgente.AsNoTracking()
            join e in ce.Expedientes.AsNoTracking() on a.ExpedienteId equals e.Id
            where a.Id == facturaProveedorId select (TipoExpediente?)e.Tipo).FirstOrDefaultAsync(cancellationToken);
        if (tipoAnticipo is not null) return tipoAnticipo == TipoExpediente.Importacion
            ? TipoOperacionComercioExterior.Importacion : TipoOperacionComercioExterior.Exportacion;
        var tipo = await (from g in ce.Set<GastoExpediente>().AsNoTracking()
                          join e in ce.Expedientes.AsNoTracking() on g.ExpedienteId equals e.Id
                          where g.FacturaProveedorId == facturaProveedorId && e.Estado == EstadoExpediente.Abierto
                          select (TipoExpediente?)e.Tipo).FirstOrDefaultAsync(cancellationToken);
        if (tipo is not null)
            return tipo == TipoExpediente.Exportacion ? TipoOperacionComercioExterior.Exportacion : TipoOperacionComercioExterior.Importacion;
        if (ordenCompraId is Guid oc && await compras.OrdenesCompra.AsNoTracking().AnyAsync(o => o.Id == oc && o.EsImportacion, cancellationToken))
            return TipoOperacionComercioExterior.Importacion;
        return null;
    }
}
