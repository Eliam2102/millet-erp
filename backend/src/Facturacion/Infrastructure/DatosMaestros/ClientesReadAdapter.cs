using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.DatosMaestros;

/// <summary>
/// Adapter REAL de <see cref="IClientesReadPort"/> sobre el master de
/// clientes de DatosMaestros (<c>compartido.clientes</c>, ADR-0048 D6).
/// Cierra PLATFORM-TODO(&lt;DatosMaestrosFiscal&gt;) para clientes. Lectura vía
/// <see cref="CompartidoDbContext"/> (mismo precedente que
/// <c>CompartidoCatalogosSatReadAdapter</c> — Facturación ya referencia
/// Compartido; cero escritura).
/// </summary>
public sealed class ClientesReadAdapter : IClientesReadPort
{
    private readonly CompartidoDbContext _db;

    public ClientesReadAdapter(CompartidoDbContext db) => _db = db;

    public async Task<ClienteFiscalLectura?> ObtenerAsync(
        Guid clienteId, CancellationToken cancellationToken)
    {
        return await _db.Clientes.AsNoTracking()
            .Where(c => c.Id == clienteId)
            .Select(c => new ClienteFiscalLectura(
                c.Id, c.Rfc, c.RazonSocial, c.RegimenFiscal,
                c.CodigoPostalFiscal, c.UsoCfdiDefault, c.FormaPagoDefault,
                c.MetodoPagoDefault, c.MonedaDefault, c.EsGenerico))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ClienteFiscalLectura?> ResolverPorReferenciaAsync(
        string referenciaExterna, CancellationToken cancellationToken)
    {
        return await _db.Clientes.AsNoTracking()
            .Where(c => c.ReferenciaExterna == referenciaExterna)
            .Select(c => new ClienteFiscalLectura(
                c.Id, c.Rfc, c.RazonSocial, c.RegimenFiscal,
                c.CodigoPostalFiscal, c.UsoCfdiDefault, c.FormaPagoDefault,
                c.MetodoPagoDefault, c.MonedaDefault, c.EsGenerico))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClienteBusquedaItem>> BuscarAsync(
        string? rfc, string? razonSocial, int limit, CancellationToken cancellationToken)
    {
        IQueryable<Cliente> q = _db.Clientes.AsNoTracking()
            .Where(c => c.Estatus == EstatusCatalogo.Activo);

        // Filtros excluyentes al estilo ADR-0045 (RFC tiene precedencia).
#pragma warning disable CA1304, CA1311, CA1862
        if (!string.IsNullOrWhiteSpace(rfc))
        {
            q = q.Where(c => c.Rfc != null && c.Rfc.ToLower().Contains(rfc.ToLower()));
        }
        else if (!string.IsNullOrWhiteSpace(razonSocial))
        {
            q = q.Where(c =>
                PostgresFunctions.Translate(c.RazonSocial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                    .Contains(PostgresFunctions.Translate(razonSocial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)));
        }
#pragma warning restore CA1304, CA1311, CA1862

        return await q
            .OrderBy(c => c.Clave)
            .Take(limit)
            .Select(c => new ClienteBusquedaItem(
                c.Id, c.Clave, c.RazonSocial, c.Rfc, c.RegimenFiscal,
                c.CodigoPostalFiscal, c.UsoCfdiDefault, c.FormaPagoDefault,
                c.MetodoPagoDefault, c.MonedaDefault, c.EsGenerico,
                c.NumRegIdTrib, c.PaisResidencia, c.DomicilioExtranjeroCalle,
                c.DomicilioExtranjeroEstado, c.DomicilioExtranjeroCodigoPostal))
            .ToListAsync(cancellationToken);
    }
}
