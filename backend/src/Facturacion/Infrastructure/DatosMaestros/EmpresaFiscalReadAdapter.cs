using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.DatosMaestros;

/// <summary>
/// Adapter REAL de <see cref="IEmpresaFiscalReadPort"/> sobre
/// <c>compartido.empresas</c> y <c>compartido.sucursales</c> (dueño:
/// Administración). Lectura vía <see cref="CompartidoDbContext"/>, mismo
/// precedente que <see cref="ClientesReadAdapter"/> — Facturación ya
/// referencia Compartido; cero escritura.
/// </summary>
public sealed class EmpresaFiscalReadAdapter : IEmpresaFiscalReadPort
{
    private readonly CompartidoDbContext _db;

    public EmpresaFiscalReadAdapter(CompartidoDbContext db) => _db = db;

    public async Task<EmpresaFiscalLectura?> ObtenerAsync(
        Guid empresaId, CancellationToken cancellationToken)
    {
        return await _db.Empresas.AsNoTracking()
            .Where(e => e.Id == empresaId && e.Activa)
            .Select(e => new EmpresaFiscalLectura(e.Id, e.Rfc, e.RazonSocial, e.RegimenFiscal, e.TasaIvaDefault, e.CodigoPostal))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken cancellationToken)
    {
        var ids = await _db.Sucursales.AsNoTracking()
            .Where(s => s.Estatus == EstatusCatalogo.Activo)
            .Select(s => s.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        return ids.Count == 1 ? ids[0] : null;
    }
}
