using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Domain.Cfdi;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;

namespace Millet.Integraciones.Fiscal.Infrastructure.Cfdi;

/// <summary>
/// Adapter EF de <see cref="ICfdiRepositorioPort"/> sobre
/// <see cref="IntegracionesFiscalDbContext"/> (F2-PR1). Persiste el archivo
/// crudo del CFDI emitido. <see cref="GuardarAsync"/> es idempotente por UUID
/// (re-guardar el mismo UUID devuelve el id existente — el <c>EmpresaId</c> lo
/// asigna el interceptor desde el contexto del request).
/// </summary>
public sealed class CfdiArchivoRepository : ICfdiRepositorioPort
{
    private readonly IntegracionesFiscalDbContext _db;

    public CfdiArchivoRepository(IntegracionesFiscalDbContext db) => _db = db;

    public async Task<Guid> GuardarAsync(CfdiArchivoNuevo archivo, CancellationToken cancellationToken)
    {
        var existenteId = await _db.CfdisArchivo
            .AsNoTracking()
            .Where(a => a.Uuid == archivo.Uuid)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existenteId is Guid id)
        {
            return id;
        }

        var entidad = CfdiArchivo.Crear(
            uuid: archivo.Uuid,
            xmlContenido: archivo.XmlContenido,
            pdfContenido: archivo.PdfContenido,
            selloCfdi: archivo.SelloCfdi,
            selloSat: archivo.SelloSat,
            noCertificadoSat: archivo.NoCertificadoSat,
            rfcProveedorCertificacion: archivo.RfcProveedorCertificacion,
            fechaTimbrado: archivo.FechaTimbrado,
            xmlHashSha256: archivo.XmlHashSha256);

        _db.CfdisArchivo.Add(entidad);
        await _db.SaveChangesAsync(cancellationToken);
        return entidad.Id;
    }

    public async Task<CfdiArchivoLeido?> ObtenerAsync(Guid cfdiArchivoId, CancellationToken cancellationToken)
    {
        var a = await _db.CfdisArchivo
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == cfdiArchivoId, cancellationToken);
        return a is null ? null : Map(a);
    }

    public async Task<CfdiArchivoLeido?> ObtenerPorUuidAsync(string uuid, CancellationToken cancellationToken)
    {
        var a = await _db.CfdisArchivo
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Uuid == uuid, cancellationToken);
        return a is null ? null : Map(a);
    }

    private static CfdiArchivoLeido Map(CfdiArchivo a) =>
        new(a.Id, a.Uuid, a.XmlContenido, a.PdfContenido, a.SelloSat, a.FechaTimbrado);
}
