using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.Cartera;

// ============================================================================
// CXC-FE-PR6: facturas vivas (Abierta/Parcial) de un cliente para el
// matching depósito ↔ facturas (05-frontend-diseno §4.1). Lee la
// proyección factura_cartera — el saldo es total − pagado − NC, mismo
// criterio que SaldoNetoClienteQuery.
// ============================================================================

public sealed record FacturaAbiertaDto(
    Guid FacturaCarteraId,
    string Uuid,
    string Folio,
    string Moneda,
    string MetodoPago,
    decimal Total,
    decimal MontoPagado,
    decimal MontoNc,
    decimal Saldo,
    DateTimeOffset FechaTimbrado,
    DateTimeOffset FechaVencimiento,
    // C7.4: «parcialidad siguiente» (PPD; null en PUE) y lo disponible para aplicar
    // (saldo − propuestas abiertas). En PPD el saldo y la parcialidad salen de
    // Facturación (mismo cálculo que FacturasCobrablesPpdQuery); las propuestas
    // abiertas de CxC (sin REP aún) suman a la parcialidad y restan del disponible.
    int? NumParcialidadSiguiente = null,
    decimal? Disponible = null);

public sealed record FacturasAbiertasClienteQuery(
    Guid ClienteId,
    string? Moneda = null) : IRequest<IReadOnlyList<FacturaAbiertaDto>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public string TipoDocumento => "factura_cartera";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class FacturasAbiertasClienteHandler
    : IRequestHandler<FacturasAbiertasClienteQuery, IReadOnlyList<FacturaAbiertaDto>>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly Millet.CuentasPorCobrar.Domain.Ports.Facturacion.IReppPendientesReadPort? _facturacion;
    public FacturasAbiertasClienteHandler(CuentasPorCobrarDbContext db,
        Millet.CuentasPorCobrar.Domain.Ports.Facturacion.IReppPendientesReadPort? facturacion = null)
    { _db = db; _facturacion = facturacion; }

    public async Task<IReadOnlyList<FacturaAbiertaDto>> Handle(
        FacturasAbiertasClienteQuery query, CancellationToken cancellationToken)
    {
        var q = _db.FacturasCartera
            .Where(x => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id)).AsNoTracking()
            .Where(f => f.ClienteId == query.ClienteId
                     && (f.Estado == EstadoFacturaCartera.Abierta
                      || f.Estado == EstadoFacturaCartera.Parcial));
        if (!string.IsNullOrWhiteSpace(query.Moneda))
            q = q.Where(f => f.Moneda == query.Moneda);

        // V61: de la más antigua a la más reciente (vencimiento, luego timbrado); el ERP solo sugiere.
        var facturas = await q
            .OrderBy(f => f.FechaVencimiento).ThenBy(f => f.FechaTimbrado)
            .ToListAsync(cancellationToken);
        var parcialidades = await AplicacionPagos.ParcialidadesCxc.CalcularAsync(_db, _facturacion, facturas, cancellationToken);
        return facturas
            .Select(f =>
            {
                var p = parcialidades[f.Id];
                return new FacturaAbiertaDto(
                    f.Id, f.Uuid, f.Folio, f.Moneda, f.MetodoPago,
                    f.Total, f.MontoPagado, f.MontoNc, p.Saldo,
                    f.FechaTimbrado, f.FechaVencimiento, p.NumParcialidadSiguiente, p.Disponible);
            })
            .ToList();
    }
}
