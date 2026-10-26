using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Almacen.Domain.Ports;
namespace Millet.ActivosFijos.Infrastructure.PublicAdapters;
/// <summary>Capitalizado = ya no está por capitalizar (salvo baja por anulación previa) o tiene póliza de alta; facturado = conciliado con factura de proveedor.</summary>
public sealed class ActivoRecepcionReadAdapter(ActivosFijosDbContext db) : IActivoRecepcionReadPort
{
    public async Task<EstadoActivosRecepcion> ConsultarAsync(Guid empresaId, Guid recepcionId, Guid lineaOcId, CancellationToken cancellationToken)
    {
        var activos = await db.Activos.AsNoTracking().Where(x => x.EmpresaId == empresaId && x.RecepcionId == recepcionId && x.LineaOcId == lineaOcId)
            .Select(x => new { x.Estado, x.PolizaAltaId, x.FacturaProveedorId, x.MotivoBaja }).ToListAsync(cancellationToken);
        // Un activo dado de baja con motivo ya fue anulado (reintento); uno dado de baja sin motivo salió por el flujo de baja de AF.
        return new EstadoActivosRecepcion(
            activos.Count(x => x.Estado == EstadoActivo.Activo || x.PolizaAltaId != null || (x.Estado == EstadoActivo.DadoDeBaja && x.MotivoBaja == null)),
            activos.Count(x => x.FacturaProveedorId != null && x.Estado == EstadoActivo.PorCapitalizar));
    }
}
