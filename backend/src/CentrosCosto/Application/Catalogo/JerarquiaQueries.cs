using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CentrosCosto.Application.Catalogo;

// ============================================================================
// Consulta jerárquica del catálogo (árbol de CONFIGURACIÓN, 3 niveles):
// "hijos del nodo X" con carga perezosa — copia estructural del molde de
// Almacén (ADR-0047 PR6) SIN rollups de saldo: el agregado por nodo es el
// CONTEO de descendientes vivos.
//
// "Vivo" = estatus != Inactivo (EnRevision cuenta) — el MISMO predicado que
// la cascada de baja (ADR-0049): el aviso del modal y los conteos del
// command de cascada deben coincidir exactamente (test de coherencia).
// Con la separación total (CECO-PR4) todo es local al esquema — sin
// read-ports ni batch. El árbol de ASIGNACIÓN (5 niveles, tri-estado)
// llega en CECO-PR6.
// ============================================================================

/// <summary>Nivel del nodo QUE SE EXPANDE (los hijos son el nivel siguiente). Dim3 es hoja: no se expande.</summary>
public enum NivelNodoCeCo
{
    Raiz = 0, // hijos: Dim1
    Dim1 = 1, // hijos: Dim2
    Dim2 = 2, // hijos: Dim3 (hoja)
}

/// <summary>
/// Un hijo inmediato del nodo expandido. <c>Tipo</c> = "dim1" | "dim2" |
/// "dim3" (el FE traduce a la etiqueta contextual — 05-frontend §0).
/// <c>Grupo</c> = nombre del GrupoDim2 en nodos dim2, del GrupoDim3 en
/// dim3, null en dim1 (chip del FE). <c>EsHoja</c> lo fija el backend.
/// Los conteos SIEMPRE cuentan vivos (estatus != Inactivo), independiente
/// de IncluirInactivos: <c>Dim2Vivas</c> solo aplica a nodos dim1;
/// <c>Dim3Vivas</c> en dim1 son los nietos, en dim2 sus hijos.
/// </summary>
public sealed record NodoCeCoDto(
    string Tipo,
    Guid Id,
    string Clave,
    string Nombre,
    EstatusCatalogo Estatus,
    string? Grupo,
    bool EsHoja,
    int Dim2Vivas,
    int Dim3Vivas);

public sealed record ObtenerHijosJerarquiaCeCoQuery(
    NivelNodoCeCo NodoTipo,
    Guid? NodoId,
    bool IncluirInactivos) : IRequest<IReadOnlyList<NodoCeCoDto>>;

public sealed class ObtenerHijosJerarquiaCeCoHandler
    : IRequestHandler<ObtenerHijosJerarquiaCeCoQuery, IReadOnlyList<NodoCeCoDto>>
{
    private const int LimitMax = 500;

    private readonly CentrosCostoDbContext _db;

    public ObtenerHijosJerarquiaCeCoHandler(CentrosCostoDbContext db) => _db = db;

    public async Task<IReadOnlyList<NodoCeCoDto>> Handle(
        ObtenerHijosJerarquiaCeCoQuery request, CancellationToken cancellationToken)
    {
        if (request.NodoTipo != NivelNodoCeCo.Raiz && request.NodoId is null)
            throw new BusinessRuleException("CECO_JERARQUIA_NODO_REQUERIDO",
                "Expandir un nodo distinto de la raíz requiere nodoId.");

        return request.NodoTipo switch
        {
            NivelNodoCeCo.Raiz => await HijosDeRaizAsync(request, cancellationToken),
            NivelNodoCeCo.Dim1 => await HijosDeDim1Async(request, cancellationToken),
            NivelNodoCeCo.Dim2 => await HijosDeDim2Async(request, cancellationToken),
            _ => throw new BusinessRuleException("CECO_JERARQUIA_NODO_INVALIDO",
                $"Tipo de nodo no soportado: {request.NodoTipo}."),
        };
    }

    private async Task<IReadOnlyList<NodoCeCoDto>> HijosDeRaizAsync(
        ObtenerHijosJerarquiaCeCoQuery request, CancellationToken ct)
    {
        // Todo local al esquema (separación total): clave y nombre son
        // columnas de dim1 — sin puertos, sin batch.
        return await _db.Dim1s.AsNoTracking()
            .Where(d => request.IncluirInactivos || d.Estatus != EstatusCatalogo.Inactivo)
            .OrderBy(d => d.Clave)
            .Take(LimitMax)
            .Select(d => new NodoCeCoDto(
                "dim1", d.Id, d.Clave, d.Nombre, d.Estatus,
                null,
                false,
                _db.Dim2s.Count(x =>
                    x.Dim1Id == d.Id && x.Estatus != EstatusCatalogo.Inactivo),
                // Nietos en dos saltos: lo que la cascada desactivaría
                // (mismo predicado de vivo — ADR-0049).
                _db.Dim3s.Count(e =>
                    e.Estatus != EstatusCatalogo.Inactivo
                    && _db.Dim2s.Any(x =>
                        x.Id == e.Dim2Id
                        && x.Dim1Id == d.Id
                        && x.Estatus != EstatusCatalogo.Inactivo))))
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<NodoCeCoDto>> HijosDeDim1Async(
        ObtenerHijosJerarquiaCeCoQuery request, CancellationToken ct)
    {
        return await (
            from d in _db.Dim2s.AsNoTracking()
            join g in _db.GruposDim2.AsNoTracking() on d.GrupoDim2Id equals g.Id
            where d.Dim1Id == request.NodoId!.Value
                && (request.IncluirInactivos || d.Estatus != EstatusCatalogo.Inactivo)
            orderby d.Clave
            select new NodoCeCoDto(
                "dim2", d.Id, d.Clave, d.Nombre, d.Estatus,
                g.Nombre,
                false,
                0,
                _db.Dim3s.Count(e =>
                    e.Dim2Id == d.Id && e.Estatus != EstatusCatalogo.Inactivo)))
            .Take(LimitMax)
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<NodoCeCoDto>> HijosDeDim2Async(
        ObtenerHijosJerarquiaCeCoQuery request, CancellationToken ct)
    {
        return await (
            from e in _db.Dim3s.AsNoTracking()
            join s in _db.GruposDim3.AsNoTracking() on e.GrupoDim3Id equals s.Id
            where e.Dim2Id == request.NodoId!.Value
                && (request.IncluirInactivos || e.Estatus != EstatusCatalogo.Inactivo)
            orderby e.Clave
            select new NodoCeCoDto(
                "dim3", e.Id, e.Clave, e.Nombre, e.Estatus,
                s.Nombre,
                true,
                0,
                0))
            .Take(LimitMax)
            .ToListAsync(ct);
    }
}
