using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>M-373: el cambio queda en la bitácora por <c>IAuditable</c> de <c>AdjuntoTipoDocumento</c>.</summary>
public sealed class TipoDocumentoCondicionalAdapter(CompartidoDbContext db) : ITipoDocumentoCondicionalPort
{
    public async Task CambiarCondicionalAsync(string tipoEntidad, Guid tipoDocumentoId, bool condicional, CancellationToken cancellationToken)
    {
        var tipo = await db.AdjuntoTiposDocumento.SingleOrDefaultAsync(t => t.Id == tipoDocumentoId && t.TipoEntidad == tipoEntidad, cancellationToken)
            ?? throw new EntityNotFoundException("ADJUNTO_TIPO_DOCUMENTO_NO_ENCONTRADO", "No existe el tipo de documento.");
        if (tipo.Condicional == condicional) return;
        tipo.CambiarCondicional(condicional);
        await db.SaveChangesAsync(cancellationToken);
    }
}
