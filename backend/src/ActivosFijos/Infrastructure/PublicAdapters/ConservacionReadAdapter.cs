using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ActivosFijos.Infrastructure.PublicAdapters;
public sealed class ConservacionReadAdapter(CompartidoDbContext db) : IConservacionReadPort
{
    public async Task ValidarAdjuntoAsync(Guid activo, DocumentoActivoDatos datos, CancellationToken ct)
    {
        var tipo = Guid.Parse($"0000000f-0003-0000-0000-{(int)datos.Tipo + 1:000000000000}");
        if (!await db.Adjuntos.AnyAsync(x => x.Id == datos.AdjuntoId && x.TipoEntidad == ActivoAdjuntoPropietario.Entidad && x.EntidadId == activo && x.TipoDocumentoId == tipo && x.BajaEn == null, ct))
            throw new BusinessRuleException("AF_ADJUNTO_INVALIDO", "Suba un archivo del tipo indicado a este activo.");
    }
    public async Task ValidarProveedorAsync(Guid proveedor, CancellationToken ct)
    {
        if (!await db.Proveedores.AnyAsync(x => x.Id == proveedor && x.Estatus == Millet.Catalogos.Domain.EstatusCatalogo.Activo, ct))
            throw new BusinessRuleException("AF_PROVEEDOR_INVALIDO", "Seleccione un proveedor activo.");
    }
    public Task<string> EmpresaAsync(Guid empresa, CancellationToken ct) => db.Empresas.Where(x => x.Id == empresa).Select(x => x.RazonSocial).SingleAsync(ct);
}
