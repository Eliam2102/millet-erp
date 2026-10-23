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
                c.MetodoPagoDefault, c.MonedaDefault, c.EsGenerico, c.Estatus == EstatusCatalogo.Activo))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ClienteFiscalLectura?> ResolverPorReferenciaAsync(
        string referenciaExterna, CancellationToken cancellationToken)
    {
        // Un cliente unificado (RFC repetido) resuelve a su principal: los pedidos nuevos van al principal (un solo salto).
        return await _db.Clientes.AsNoTracking()
            .Where(c => c.ReferenciaExterna == referenciaExterna)
            .Join(_db.Clientes.AsNoTracking(), s => s.UnificadoEnClienteId ?? s.Id, c => c.Id, (s, c) => c)
            .Select(c => new ClienteFiscalLectura(
                c.Id, c.Rfc, c.RazonSocial, c.RegimenFiscal,
                c.CodigoPostalFiscal, c.UsoCfdiDefault, c.FormaPagoDefault,
                c.MetodoPagoDefault, c.MonedaDefault, c.EsGenerico, c.Estatus == EstatusCatalogo.Activo))
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
            .Select(AItem)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClienteBusquedaItem>> BuscarPorPalabrasAsync(
        string texto, int limit, CancellationToken cancellationToken)
    {
        var palabras = BusquedaPorPalabras.Normalizar(texto);
        IQueryable<Cliente> q = _db.Clientes.AsNoTracking()
            .Where(c => c.Estatus == EstatusCatalogo.Activo);

#pragma warning disable CA1304, CA1311, CA1862
        foreach (var palabra in palabras)
        {
            q = q.Where(c =>
                PostgresFunctions.Translate(c.RazonSocial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino).Contains(palabra)
                || (c.Rfc != null && c.Rfc.ToLower().Contains(palabra))
                || c.Clave.ToLower().Contains(palabra));
        }

        var primera = palabras.Count > 0 ? palabras[0] : string.Empty;
        return await q
            .OrderByDescending(c => PostgresFunctions.Translate(c.RazonSocial.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino).StartsWith(primera))
            .ThenBy(c => c.RazonSocial)
            .Take(limit)
            .Select(AItem)
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862
    }

    private static readonly System.Linq.Expressions.Expression<Func<Cliente, ClienteBusquedaItem>> AItem =
        c => new ClienteBusquedaItem(
                c.Id, c.Clave, c.RazonSocial, c.Rfc, c.RegimenFiscal,
                c.CodigoPostalFiscal, c.UsoCfdiDefault, c.FormaPagoDefault,
                c.MetodoPagoDefault, c.MonedaDefault, c.EsGenerico,
                c.NumRegIdTrib, c.PaisResidencia, c.DomicilioExtranjeroCalle,
                c.DomicilioExtranjeroEstado, c.DomicilioExtranjeroCodigoPostal);
}
