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
    ComercioExteriorDbContext ce, ComprasDbContext compras, ICurrentEmpresaContext empresaContext) : IPasivoComercioExteriorReadPort, IAnticipoCeCancelacionPort
{
    public async Task CancelarAnticipoAsync(Guid id, string motivo, CancellationToken ct)
    {
        var a = await ce.AnticiposAgente.SingleAsync(a => a.Id == id, ct);
        a.Cancelar(motivo);
        await ce.SaveChangesAsync(ct);
    }
    public async Task ExigirAnticipoVigenteAsync(Guid anticipoId, CancellationToken ct)
    {
        var a = await ce.AnticiposAgente.AsNoTracking().SingleAsync(a => a.Id == anticipoId, ct);
        if (a.MotivoCancelacion is not null) throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException("CE_ANTICIPO_CANCELADO", "La solicitud de anticipo está cancelada.");
    }
    public async Task ExigirSinComprobacionAsync(Guid anticipoId, CancellationToken ct)
    {
        var a = await ce.AnticiposAgente.AsNoTracking().SingleAsync(a => a.Id == anticipoId, ct);
        if (a.Aplicado is not null) throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException("CE_COMPROBACION_APROBADA", "No se puede revertir el pago: el anticipo ya tiene comprobación.");
    }
    public async Task<TipoOperacionComercioExterior?> TipoAsync(Guid facturaProveedorId, Guid? ordenCompraId, CancellationToken cancellationToken)
    {
        using var bypass = empresaContext.Bypass();
        var tipoAnticipo = await (from a in ce.AnticiposAgente.AsNoTracking()
            join e in ce.Expedientes.AsNoTracking() on a.ExpedienteId equals e.Id
            where a.Id == facturaProveedorId select (TipoExpediente?)e.Tipo).FirstOrDefaultAsync(cancellationToken);
        if (tipoAnticipo is not null) return tipoAnticipo == TipoExpediente.Importacion
            ? TipoOperacionComercioExterior.Importacion : TipoOperacionComercioExterior.Exportacion;
        // El servicio CE ya obtiene sus dos firmas en la OC; CxP valida su vigencia antes de liberar y pagar.
        if (ordenCompraId is Guid servicio && await compras.OrdenesCompra.AsNoTracking().AnyAsync(o => o.Id == servicio && o.EsServicioComercioExterior, cancellationToken))
            return null;
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
