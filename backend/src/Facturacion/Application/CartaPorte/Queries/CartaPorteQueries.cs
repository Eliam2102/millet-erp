using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.CartaPorte.Queries;

// ---- Bandeja (B11) ----

/// <summary>Bandeja de Carta Porte (B11, FE-F8).</summary>
public sealed record BandejaCartaPorteQuery(EstadoTimbrado? Estado, int Offset, int Limit) : IRequest<IReadOnlyList<CartaPorteBandejaItem>>;

public sealed record CartaPorteBandejaItem(
    Guid Id, string Folio, string Estado, string? Uuid, string Tipo, string Tramo, DateTimeOffset FechaSalida);

public sealed class BandejaCartaPorteHandler : IRequestHandler<BandejaCartaPorteQuery, IReadOnlyList<CartaPorteBandejaItem>>
{
    private readonly FacturacionDbContext _db;
    public BandejaCartaPorteHandler(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<CartaPorteBandejaItem>> Handle(BandejaCartaPorteQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var q = _db.CartasPorte.AsNoTracking();
        if (query.Estado is { } estado) q = q.Where(c => c.Estado == estado);

        var rows = await q
            .OrderByDescending(c => c.FolioNumero)
            .Skip(Math.Max(0, query.Offset)).Take(limit)
            .Select(c => new { c.Id, c.Folio, c.Estado, c.Uuid, c.Tipo, c.Origen, c.Destino, c.FechaSalida })
            .ToListAsync(cancellationToken);

        return rows.Select(c => new CartaPorteBandejaItem(
            c.Id, c.Folio, c.Estado.ToString(), c.Uuid, TipoCfdi(c.Tipo), $"{c.Origen} → {c.Destino}", c.FechaSalida)).ToList();
    }

    internal static string TipoCfdi(TipoComprobante t) => t == TipoComprobante.Traslado ? "T" : "I";
}

// ---- Detalle (B11) ----

/// <summary>Detalle de una Carta Porte con vehículo, operador, mercancías y tramo previo (B11).</summary>
public sealed record CartaPorteDetalleQuery(Guid Id) : IRequest<CartaPorteDetalleResponse>;

public sealed record CartaPorteDetalleResponse(
    Guid Id, string Folio, string Estado, string? Uuid, string Tipo,
    string Origen, string Destino, decimal DistanciaKm, DateTimeOffset FechaSalida, DateTimeOffset FechaLlegadaEstimada,
    Guid? CartaPortePreviaId, decimal Total,
    CartaPorteVehiculoDetalle? Vehiculo, CartaPorteOperadorDetalle? Operador,
    IReadOnlyList<CartaPorteMercanciaDetalle> Mercancias,
    // Error del último intento de timbrado + folio del PAC (PR-B reintento).
    string? TimbradoErrorCodigo = null,
    string? TimbradoErrorMensaje = null,
    string? FolioPac = null);

public sealed record CartaPorteVehiculoDetalle(Guid Id, string Placa, string ConfigVehicular, int AnioModelo);
public sealed record CartaPorteOperadorDetalle(Guid Id, string Rfc, string Nombre, string NumLicencia);
public sealed record CartaPorteMercanciaDetalle(string Descripcion, string BienesTransp, string ClaveUnidad, decimal Cantidad, decimal PesoEnKg, bool MaterialPeligroso);

public sealed class CartaPorteDetalleHandler : IRequestHandler<CartaPorteDetalleQuery, CartaPorteDetalleResponse>
{
    private readonly FacturacionDbContext _db;
    public CartaPorteDetalleHandler(FacturacionDbContext db) => _db = db;

    public async Task<CartaPorteDetalleResponse> Handle(CartaPorteDetalleQuery query, CancellationToken cancellationToken)
    {
        var cp = await _db.CartasPorte.AsNoTracking()
            .Include(c => c.Mercancias)
            .FirstOrDefaultAsync(c => c.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CARTA_PORTE_NO_ENCONTRADA", $"No existe la Carta Porte '{query.Id}'.");

        var vehiculo = await _db.Vehiculos.AsNoTracking()
            .Where(v => v.Id == cp.VehiculoId)
            .Select(v => new CartaPorteVehiculoDetalle(v.Id, v.Placa, v.ConfigVehicular, v.AnioModelo))
            .FirstOrDefaultAsync(cancellationToken);
        var operador = await _db.Operadores.AsNoTracking()
            .Where(o => o.Id == cp.OperadorId)
            .Select(o => new CartaPorteOperadorDetalle(o.Id, o.Rfc, o.Nombre, o.NumLicencia))
            .FirstOrDefaultAsync(cancellationToken);

        var mercancias = cp.Mercancias
            .Select(m => new CartaPorteMercanciaDetalle(m.Descripcion, m.BienesTransp, m.ClaveUnidad, m.Cantidad, m.PesoEnKg, m.MaterialPeligroso))
            .ToList();

        return new CartaPorteDetalleResponse(
            cp.Id, cp.Folio, cp.Estado.ToString(), cp.Uuid, BandejaCartaPorteHandler.TipoCfdi(cp.Tipo),
            cp.Origen, cp.Destino, cp.DistanciaKm, cp.FechaSalida, cp.FechaLlegadaEstimada,
            cp.CartaPortePreviaId, cp.Total, vehiculo, operador, mercancias,
            cp.TimbradoErrorCodigo, cp.TimbradoErrorMensaje, cp.FolioPac);
    }
}
