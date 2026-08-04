using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;

namespace Millet.CentrosCosto.Application.Asignaciones;

// ============================================================================
// Árbol de ASIGNACIÓN (CECO-PR6, 01-diseno §7): 5 niveles — Dim1 →
// GrupoDim2 → Dim2 → GrupoDim3 → Dim3 — con los grupos ACOTADOS al padre
// (misma data que el árbol de configuración de PR-3, otra forma).
//
// FULL-TREE deliberado (no lazy): el tri-estado se calcula desde las hojas
// hacia arriba, así que pintar una Dim1 EXIGE conocer sus ~336 Dim3 — un
// árbol perezoso pelearía contra su propio requisito. Volumen total ~475
// renglones: 4 queries de conjunto FIJAS (dim1, dim2+grupo, dim3+grupo,
// asignaciones del usuario) y composición en memoria. Cero N+1.
//
// Tri-estado NUNCA almacenado; denominador = hojas VIVAS (!= Inactivo, el
// mismo predicado de la cascada ADR-0049 y de la expansión del marcado).
// Consecuencia aceptada (§7): quitar a propósito y faltar por
// desactualización pintan igual ("parcial") — no distingue intención.
// Solo nodos vivos (sin toggle de inactivos: una Dim3 muerta no cuenta ni
// se pinta — no es asignable).
// ============================================================================

public enum TriEstado : short
{
    Ninguno = 0,
    Parcial = 1,
    Todo = 2,
}

public sealed record Dim3AsignacionDto(
    Guid Id, string Clave, string Nombre, bool Asignada);

public sealed record GrupoDim3AsignacionDto(
    Guid Id, string Nombre, TriEstado Estado, int Dim3Vivas, int Dim3Asignadas,
    IReadOnlyList<Dim3AsignacionDto> Dim3s);

public sealed record Dim2AsignacionDto(
    Guid Id, string Clave, string Nombre, TriEstado Estado, int Dim3Vivas, int Dim3Asignadas,
    IReadOnlyList<GrupoDim3AsignacionDto> Grupos);

public sealed record GrupoDim2AsignacionDto(
    Guid Id, string Nombre, TriEstado Estado, int Dim3Vivas, int Dim3Asignadas,
    IReadOnlyList<Dim2AsignacionDto> Dim2s);

public sealed record Dim1AsignacionDto(
    Guid Id, string Clave, string Nombre, TriEstado Estado, int Dim3Vivas, int Dim3Asignadas,
    IReadOnlyList<GrupoDim2AsignacionDto> Grupos);

/// <summary>Barra resumen por dimensión (05-frontend §4.2): "Dim1: 2/5 completas · Dim3: 118/361 asignadas".</summary>
public sealed record ResumenAsignacion(
    int Dim1Vivas, int Dim1Completas,
    int Dim2Vivas, int Dim2Completas,
    int Dim3Vivas, int Dim3Asignadas);

public sealed record ArbolAsignacionResponse(
    Guid UsuarioId,
    ResumenAsignacion Resumen,
    IReadOnlyList<Dim1AsignacionDto> Dim1s,
    // El usuario SELECCIONADO tiene alcance total (centros_costo.dim3.leer-todos):
    // asignar no aporta (ve todo igual). Lo resuelve el ENDPOINT vía
    // IPermissionLoader — el módulo NO referencia Identidad (#620); el handler
    // devuelve false y el endpoint lo sobreescribe con `with`.
    bool EsAlcanceTotal = false);

public sealed record ObtenerArbolAsignacionQuery(Guid UsuarioId)
    : IRequest<ArbolAsignacionResponse>;

public sealed class ObtenerArbolAsignacionValidator : AbstractValidator<ObtenerArbolAsignacionQuery>
{
    public ObtenerArbolAsignacionValidator()
    {
        RuleFor(q => q.UsuarioId).NotEqual(Guid.Empty);
    }
}

public sealed class ObtenerArbolAsignacionHandler
    : IRequestHandler<ObtenerArbolAsignacionQuery, ArbolAsignacionResponse>
{
    private readonly CentrosCostoDbContext _db;

    public ObtenerArbolAsignacionHandler(CentrosCostoDbContext db) => _db = db;

    private sealed record Dim2Plano(Guid Id, Guid Dim1Id, string Clave, string Nombre, Guid GrupoId, string GrupoNombre);
    private sealed record Dim3Plano(Guid Id, Guid Dim2Id, string Clave, string Nombre, Guid GrupoId, string GrupoNombre);

    public async Task<ArbolAsignacionResponse> Handle(
        ObtenerArbolAsignacionQuery request, CancellationToken cancellationToken)
    {
        // Las 4 queries de conjunto — todo lo demás es memoria.
        var dim1s = await _db.Dim1s.AsNoTracking()
            .Where(d => d.Estatus != EstatusCatalogo.Inactivo)
            .OrderBy(d => d.Clave)
            .Select(d => new { d.Id, d.Clave, d.Nombre })
            .ToListAsync(cancellationToken);

        var dim2s = await (
            from d in _db.Dim2s.AsNoTracking()
            join g in _db.GruposDim2.AsNoTracking() on d.GrupoDim2Id equals g.Id
            where d.Estatus != EstatusCatalogo.Inactivo
            select new Dim2Plano(d.Id, d.Dim1Id, d.Clave, d.Nombre, g.Id, g.Nombre))
            .ToListAsync(cancellationToken);

        var dim3s = await (
            from e in _db.Dim3s.AsNoTracking()
            join s in _db.GruposDim3.AsNoTracking() on e.GrupoDim3Id equals s.Id
            where e.Estatus != EstatusCatalogo.Inactivo
            select new Dim3Plano(e.Id, e.Dim2Id, e.Clave, e.Nombre, s.Id, s.Nombre))
            .ToListAsync(cancellationToken);

        var asignadas = (await _db.Asignaciones.AsNoTracking()
            .Where(a => a.UsuarioId == request.UsuarioId)
            .Select(a => a.Dim3Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        // Hojas VIVAS del padre vivo: una dim3 viva bajo una dim2 inactiva
        // no cuenta (coincide con la expansión del marcado y la cascada).
        var dim2Vivas = dim2s.Select(d => d.Id).ToHashSet();
        var dim3PorDim2 = dim3s
            .Where(e => dim2Vivas.Contains(e.Dim2Id))
            .ToLookup(e => e.Dim2Id);
        var dim2PorDim1 = dim2s.ToLookup(d => d.Dim1Id);

        var arbol = new List<Dim1AsignacionDto>(dim1s.Count);
        foreach (var d1 in dim1s)
        {
            var gruposDim2 = dim2PorDim1[d1.Id]
                .GroupBy(d => (d.GrupoId, d.GrupoNombre))
                .OrderBy(g => g.Key.GrupoNombre, StringComparer.Ordinal)
                .Select(g2 =>
                {
                    var dim2Dtos = g2
                        .OrderBy(d => d.Clave, StringComparer.Ordinal)
                        .Select(d2 =>
                        {
                            var gruposDim3 = dim3PorDim2[d2.Id]
                                .GroupBy(e => (e.GrupoId, e.GrupoNombre))
                                .OrderBy(g => g.Key.GrupoNombre, StringComparer.Ordinal)
                                .Select(g3 =>
                                {
                                    var hojas = g3
                                        .OrderBy(e => e.Clave, StringComparer.Ordinal)
                                        .Select(e => new Dim3AsignacionDto(
                                            e.Id, e.Clave, e.Nombre, asignadas.Contains(e.Id)))
                                        .ToList();
                                    var (vivas, marcadas) = (hojas.Count, hojas.Count(h => h.Asignada));
                                    return new GrupoDim3AsignacionDto(
                                        g3.Key.GrupoId, g3.Key.GrupoNombre,
                                        Estado(vivas, marcadas), vivas, marcadas, hojas);
                                })
                                .ToList();
                            var (v, m) = Acumular(gruposDim3.Select(g => (g.Dim3Vivas, g.Dim3Asignadas)));
                            return new Dim2AsignacionDto(
                                d2.Id, d2.Clave, d2.Nombre, Estado(v, m), v, m, gruposDim3);
                        })
                        .ToList();
                    var (v2, m2) = Acumular(dim2Dtos.Select(d => (d.Dim3Vivas, d.Dim3Asignadas)));
                    return new GrupoDim2AsignacionDto(
                        g2.Key.GrupoId, g2.Key.GrupoNombre, Estado(v2, m2), v2, m2, dim2Dtos);
                })
                .ToList();

            var (v1, m1) = Acumular(gruposDim2.Select(g => (g.Dim3Vivas, g.Dim3Asignadas)));
            arbol.Add(new Dim1AsignacionDto(
                d1.Id, d1.Clave, d1.Nombre, Estado(v1, m1), v1, m1, gruposDim2));
        }

        var dim2Completas = arbol
            .SelectMany(a => a.Grupos)
            .SelectMany(g => g.Dim2s)
            .Count(d => d.Estado == TriEstado.Todo && d.Dim3Vivas > 0);

        var resumen = new ResumenAsignacion(
            arbol.Count,
            arbol.Count(a => a.Estado == TriEstado.Todo && a.Dim3Vivas > 0),
            dim2s.Count,
            dim2Completas,
            arbol.Sum(a => a.Dim3Vivas),
            arbol.Sum(a => a.Dim3Asignadas));

        return new ArbolAsignacionResponse(request.UsuarioId, resumen, arbol);
    }

    /// <summary>Tri-estado desde las hojas: 0 asignadas = Ninguno (incluye 0 vivas), todas = Todo.</summary>
    private static TriEstado Estado(int vivas, int asignadas) =>
        asignadas == 0 ? TriEstado.Ninguno
        : asignadas == vivas ? TriEstado.Todo
        : TriEstado.Parcial;

    private static (int Vivas, int Asignadas) Acumular(IEnumerable<(int Vivas, int Asignadas)> hijos)
    {
        int v = 0, a = 0;
        foreach (var (vivas, asignadas) in hijos)
        {
            v += vivas;
            a += asignadas;
        }

        return (v, a);
    }
}
