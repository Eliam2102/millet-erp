using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Saldos;

// ============================================================================
// Consulta jerárquica de saldos (PR6, ADR-0047): "hijos del nodo X" con
// rollup por nodo. Carga perezosa — cada expansión del árbol en el FE es una
// llamada acotada que devuelve SOLO los hijos inmediatos, con su subtotal.
//
// Dos modos sobre la misma query:
//   - Modo ARTÍCULO (ArticuloId presente): todas las ramas filtran por el
//     artículo; la ubicación (N4) es hoja.
//   - Modo UBICACIÓN (sin ArticuloId): la ubicación expande a los ARTÍCULOS
//     que contiene (5º nivel hoja).
//
// El rollup suma Cantidad y ValorInventarioMxn (aditivos). El CPP NO se
// promedia hacia arriba (no es aditivo).
// ============================================================================

/// <summary>Nivel del nodo QUE SE EXPANDE (los hijos son el nivel siguiente).</summary>
public enum NivelNodoJerarquia
{
    Raiz = 0,       // hijos: sucursales (N1)
    Sucursal = 1,   // hijos: almacenes (N2)
    Almacen = 2,    // hijos: sub-almacenes (N3)
    SubAlmacen = 3, // hijos: ubicaciones (N4)
    Ubicacion = 4,  // hijos: artículos (solo modo ubicación)
}

/// <summary>
/// Un hijo inmediato del nodo expandido, con su subtotal agregado.
/// <c>Tipo</c> = "sucursal" | "almacen" | "subAlmacen" | "ubicacion" |
/// "articulo". <c>EsDefault</c> solo es significativo en ubicaciones (badge
/// ÚNICA). <c>EsHoja</c> lo fija el backend (estático por nivel+modo): el FE
/// no muestra chevron ni intenta expandir una hoja.
/// </summary>
public sealed record NodoJerarquiaDto(
    string Tipo,
    Guid Id,
    string Clave,
    string Nombre,
    decimal Cantidad,
    decimal ValorInventarioMxn,
    bool EsDefault,
    bool EsHoja);

public sealed record ObtenerHijosJerarquiaQuery(
    NivelNodoJerarquia NodoTipo,
    Guid? NodoId,
    Guid? ArticuloId,      // presente = modo artículo
    bool IncluirVacios) : IRequest<IReadOnlyList<NodoJerarquiaDto>>;

public sealed class ObtenerHijosJerarquiaHandler
    : IRequestHandler<ObtenerHijosJerarquiaQuery, IReadOnlyList<NodoJerarquiaDto>>
{
    private const int LimitMax = 500;

    private readonly AlmacenDbContext _db;
    private readonly ISucursalReadPort _sucursales;
    private readonly IArticuloReadPort _articulos;

    public ObtenerHijosJerarquiaHandler(
        AlmacenDbContext db,
        ISucursalReadPort sucursales,
        IArticuloReadPort articulos)
    {
        _db = db;
        _sucursales = sucursales;
        _articulos = articulos;
    }

    public async Task<IReadOnlyList<NodoJerarquiaDto>> Handle(
        ObtenerHijosJerarquiaQuery request, CancellationToken cancellationToken)
    {
        if (request.NodoTipo != NivelNodoJerarquia.Raiz && request.NodoId is null)
            throw new BusinessRuleException("JERARQUIA_NODO_REQUERIDO",
                "Expandir un nodo distinto de la raíz requiere nodoId.");
        if (request.NodoTipo == NivelNodoJerarquia.Ubicacion && request.ArticuloId is not null)
            throw new BusinessRuleException("JERARQUIA_ARTICULO_EN_HOJA",
                "En modo artículo la ubicación es hoja; no tiene hijos que expandir.");

        return request.NodoTipo switch
        {
            NivelNodoJerarquia.Raiz => await HijosDeRaizAsync(request, cancellationToken),
            NivelNodoJerarquia.Sucursal => await HijosDeSucursalAsync(request, cancellationToken),
            NivelNodoJerarquia.Almacen => await HijosDeAlmacenAsync(request, cancellationToken),
            NivelNodoJerarquia.SubAlmacen => await HijosDeSubAlmacenAsync(request, cancellationToken),
            NivelNodoJerarquia.Ubicacion => await HijosDeUbicacionAsync(request, cancellationToken),
            _ => throw new BusinessRuleException("JERARQUIA_NODO_INVALIDO",
                $"Tipo de nodo no soportado: {request.NodoTipo}."),
        };
    }

    /// <summary>
    /// Filtro base compartido por todas las ramas: modo artículo + toggle
    /// vacíos. Con IncluirVacios=false los nodos cuyo único contenido son
    /// filas-en-0 (racks asignados vacíos) desaparecen; con true aparecen con
    /// subtotal 0 — los ceros nunca alteran las sumas.
    /// </summary>
    private IQueryable<Domain.Saldos.SaldoInventario> SaldosFiltrados(
        ObtenerHijosJerarquiaQuery request)
    {
        var query = _db.SaldosInventario.AsNoTracking().AsQueryable();
        if (request.ArticuloId is Guid aid) query = query.Where(s => s.ArticuloId == aid);
        if (!request.IncluirVacios) query = query.Where(s => s.Cantidad > 0);
        return query;
    }

    private async Task<IReadOnlyList<NodoJerarquiaDto>> HijosDeRaizAsync(
        ObtenerHijosJerarquiaQuery request, CancellationToken ct)
    {
        var grupos = await (
            from s in SaldosFiltrados(request)
            join sa in _db.SubAlmacenes.AsNoTracking() on s.SubAlmacenId equals sa.Id
            join al in _db.Almacenes.AsNoTracking() on sa.AlmacenId equals al.Id
            group s by al.SucursalId into g
            orderby g.Key
            select new
            {
                SucursalId = g.Key,
                Cantidad = g.Sum(x => x.Cantidad),
                Valor = g.Sum(x => x.ValorInventarioMxn),
            })
            .Take(LimitMax)
            .ToListAsync(ct);

        // N1 vive en compartido: nombres vía read-port, por id (el puerto no
        // tiene batch; en la práctica son 1-2 sucursales). Fallback al id
        // crudo cuando el port no resuelve (misma convención ADR-0042).
        var nodos = new List<NodoJerarquiaDto>(grupos.Count);
        foreach (var g in grupos)
        {
            var sucursal = await _sucursales.ObtenerAsync(g.SucursalId, ct);
            nodos.Add(new NodoJerarquiaDto(
                "sucursal", g.SucursalId,
                sucursal?.Clave ?? g.SucursalId.ToString(),
                sucursal?.Nombre ?? string.Empty,
                g.Cantidad, g.Valor,
                EsDefault: false, EsHoja: false));
        }
        return nodos.OrderBy(n => n.Clave, StringComparer.Ordinal).ToList();
    }

    private async Task<IReadOnlyList<NodoJerarquiaDto>> HijosDeSucursalAsync(
        ObtenerHijosJerarquiaQuery request, CancellationToken ct)
    {
        var grupos = await (
            from s in SaldosFiltrados(request)
            join sa in _db.SubAlmacenes.AsNoTracking() on s.SubAlmacenId equals sa.Id
            join al in _db.Almacenes.AsNoTracking() on sa.AlmacenId equals al.Id
            where al.SucursalId == request.NodoId!.Value
            group s by new { al.Id, al.Clave, al.Nombre } into g
            orderby g.Key.Clave
            select new
            {
                g.Key.Id,
                g.Key.Clave,
                g.Key.Nombre,
                Cantidad = g.Sum(x => x.Cantidad),
                Valor = g.Sum(x => x.ValorInventarioMxn),
            })
            .Take(LimitMax)
            .ToListAsync(ct);

        return grupos
            .Select(g => new NodoJerarquiaDto(
                "almacen", g.Id, g.Clave, g.Nombre, g.Cantidad, g.Valor,
                EsDefault: false, EsHoja: false))
            .ToList();
    }

    private async Task<IReadOnlyList<NodoJerarquiaDto>> HijosDeAlmacenAsync(
        ObtenerHijosJerarquiaQuery request, CancellationToken ct)
    {
        // El SubAlmacenId denormalizado de saldos evita el join a ubicaciones.
        var grupos = await (
            from s in SaldosFiltrados(request)
            join sa in _db.SubAlmacenes.AsNoTracking() on s.SubAlmacenId equals sa.Id
            where sa.AlmacenId == request.NodoId!.Value
            group s by new { sa.Id, sa.Clave, sa.Nombre } into g
            orderby g.Key.Clave
            select new
            {
                g.Key.Id,
                g.Key.Clave,
                g.Key.Nombre,
                Cantidad = g.Sum(x => x.Cantidad),
                Valor = g.Sum(x => x.ValorInventarioMxn),
            })
            .Take(LimitMax)
            .ToListAsync(ct);

        return grupos
            .Select(g => new NodoJerarquiaDto(
                "subAlmacen", g.Id, g.Clave, g.Nombre, g.Cantidad, g.Valor,
                EsDefault: false, EsHoja: false))
            .ToList();
    }

    private async Task<IReadOnlyList<NodoJerarquiaDto>> HijosDeSubAlmacenAsync(
        ObtenerHijosJerarquiaQuery request, CancellationToken ct)
    {
        // En modo artículo la ubicación es hoja (el artículo ya está fijo);
        // en modo ubicación expande a artículos.
        var esHoja = request.ArticuloId is not null;

        var grupos = await (
            from s in SaldosFiltrados(request)
            join u in _db.Ubicaciones.AsNoTracking() on s.UbicacionId equals u.Id
            where s.SubAlmacenId == request.NodoId!.Value
            group s by new { u.Id, u.Clave, u.Nombre, u.EsDefault } into g
            orderby g.Key.Clave
            select new
            {
                g.Key.Id,
                g.Key.Clave,
                g.Key.Nombre,
                g.Key.EsDefault,
                Cantidad = g.Sum(x => x.Cantidad),
                Valor = g.Sum(x => x.ValorInventarioMxn),
            })
            .Take(LimitMax)
            .ToListAsync(ct);

        return grupos
            .Select(g => new NodoJerarquiaDto(
                "ubicacion", g.Id, g.Clave, g.Nombre, g.Cantidad, g.Valor,
                g.EsDefault, esHoja))
            .ToList();
    }

    private async Task<IReadOnlyList<NodoJerarquiaDto>> HijosDeUbicacionAsync(
        ObtenerHijosJerarquiaQuery request, CancellationToken ct)
    {
        // PK (ubicacion_id, articulo_id) → ya hay a lo sumo una fila por
        // artículo; no hace falta agrupar.
        var filas = await SaldosFiltrados(request)
            .Where(s => s.UbicacionId == request.NodoId!.Value)
            .OrderBy(s => s.ArticuloId)
            .Take(LimitMax)
            .Select(s => new { s.ArticuloId, s.Cantidad, s.ValorInventarioMxn })
            .ToListAsync(ct);

        // Clave/descripción del artículo en batch (ADR-0042); fallback al id.
        var articuloIds = filas.Select(f => f.ArticuloId).Distinct().ToArray();
        var articulos = articuloIds.Length > 0
            ? await _articulos.ObtenerPorIdsAsync(articuloIds, ct)
            : new Dictionary<Guid, ArticuloLectura>();

        return filas
            .Select(f => articulos.TryGetValue(f.ArticuloId, out var art)
                ? new NodoJerarquiaDto("articulo", f.ArticuloId,
                    art.Clave, art.Descripcion, f.Cantidad, f.ValorInventarioMxn,
                    EsDefault: false, EsHoja: true)
                : new NodoJerarquiaDto("articulo", f.ArticuloId,
                    f.ArticuloId.ToString(), string.Empty, f.Cantidad, f.ValorInventarioMxn,
                    EsDefault: false, EsHoja: true))
            .OrderBy(n => n.Clave, StringComparer.Ordinal)
            .ToList();
    }
}
