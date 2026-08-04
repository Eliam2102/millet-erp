using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CentrosCosto.Application.Asignaciones;

// ============================================================================
// Marcado de alcance (CECO-PR6, 01-diseno §7): el usuario marca CUALQUIER
// nivel del árbol de asignación (5 niveles) y el BACKEND expande a las Dim3
// vivas bajo el nodo — la expansión es transaccional aquí, nunca del FE (el
// árbol del FE es un snapshot; el predicado "viva" tiene un solo dueño).
// La regla se usa para calcular y SE TIRA: solo se guardan/borran filas
// (usuario_id, dim3_id). SIN re-evaluación en vivo — decisión de negocio
// cerrada: una máquina creada después NO entra sola al alcance de nadie.
//
// "Viva" = estatus != Inactivo — el MISMO predicado que la cascada de baja
// (ADR-0049) y que el denominador del tri-estado del árbol.
// ============================================================================

/// <summary>
/// Nivel del nodo marcado. Los grupos van ACOTADOS al padre (§7): un grupo
/// nunca se asigna global — "OPERACIONES bajo CONKAL" son solo las Dim2 de
/// esa Dim1 con ese grupo.
/// </summary>
public enum NivelAlcance
{
    Dim1 = 0,
    GrupoDim2BajoDim1 = 1,
    Dim2 = 2,
    GrupoDim3BajoDim2 = 3,
    Dim3 = 4,
}

/// <summary>
/// <c>NodoId</c> = id del nodo de NIVEL (Dim1/Dim2/Dim3 según
/// <c>Nivel</c>; en niveles de grupo es el PADRE que acota).
/// <c>GrupoId</c> solo aplica (y es obligatorio) en niveles de grupo.
/// <c>Asignar</c> true inserta las hojas faltantes; false borra las
/// presentes.
/// </summary>
public sealed record MarcarAlcanceCommand(
    Guid UsuarioId,
    NivelAlcance Nivel,
    Guid NodoId,
    Guid? GrupoId,
    bool Asignar) : IRequest<MarcarAlcanceResponse>;

/// <summary>
/// Conteos reales post-operación para que el FE reconcilie su pintado
/// optimista: <c>HojasResueltas</c> = Dim3 vivas bajo el nodo,
/// <c>Afectadas</c> = filas insertadas/borradas, <c>TotalUsuario</c> =
/// filas del usuario después de guardar.
/// </summary>
public sealed record MarcarAlcanceResponse(
    Guid UsuarioId,
    int HojasResueltas,
    int Afectadas,
    int TotalUsuario);

public sealed class MarcarAlcanceValidator : AbstractValidator<MarcarAlcanceCommand>
{
    public MarcarAlcanceValidator()
    {
        RuleFor(c => c.UsuarioId).NotEqual(Guid.Empty);
        RuleFor(c => c.Nivel).IsInEnum();
        RuleFor(c => c.NodoId).NotEqual(Guid.Empty);
        RuleFor(c => c.GrupoId)
            .NotNull()
            .NotEqual(Guid.Empty)
            .When(c => c.Nivel is NivelAlcance.GrupoDim2BajoDim1 or NivelAlcance.GrupoDim3BajoDim2)
            .WithMessage("GrupoId es obligatorio al marcar un nivel de grupo.");
        RuleFor(c => c.GrupoId)
            .Null()
            .When(c => c.Nivel is NivelAlcance.Dim1 or NivelAlcance.Dim2 or NivelAlcance.Dim3)
            .WithMessage("GrupoId solo aplica a niveles de grupo.");
    }
}

public sealed class MarcarAlcanceHandler : IRequestHandler<MarcarAlcanceCommand, MarcarAlcanceResponse>
{
    private readonly CentrosCostoDbContext _db;

    public MarcarAlcanceHandler(CentrosCostoDbContext db) => _db = db;

    public async Task<MarcarAlcanceResponse> Handle(
        MarcarAlcanceCommand request, CancellationToken cancellationToken)
    {
        var hojas = await ResolverHojasVivasAsync(request, cancellationToken);

        var existentes = await _db.Asignaciones
            .Where(a => a.UsuarioId == request.UsuarioId && hojas.Contains(a.Dim3Id))
            .ToListAsync(cancellationToken);

        int afectadas;
        if (request.Asignar)
        {
            var yaAsignadas = existentes.Select(a => a.Dim3Id).ToHashSet();
            var nuevas = hojas
                .Where(id => !yaAsignadas.Contains(id))
                .Select(id => Asignacion.Crear(request.UsuarioId, id))
                .ToList();
            _db.Asignaciones.AddRange(nuevas);
            afectadas = nuevas.Count;
        }
        else
        {
            _db.Asignaciones.RemoveRange(existentes);
            afectadas = existentes.Count;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var total = await _db.Asignaciones.AsNoTracking()
            .CountAsync(a => a.UsuarioId == request.UsuarioId, cancellationToken);

        return new MarcarAlcanceResponse(request.UsuarioId, hojas.Count, afectadas, total);
    }

    /// <summary>
    /// Expansión del nodo marcado a sus Dim3 vivas — misma forma que las
    /// queries de la cascada (ADR-0049), más el filtro de grupo acotado al
    /// padre. 0 hojas vivas = no-op válido (el response lo hace visible).
    /// </summary>
    private async Task<List<Guid>> ResolverHojasVivasAsync(
        MarcarAlcanceCommand request, CancellationToken ct)
    {
        switch (request.Nivel)
        {
            case NivelAlcance.Dim1:
            case NivelAlcance.GrupoDim2BajoDim1:
            {
                await VerificarExisteAsync(
                    _db.Dim1s.AnyAsync(d => d.Id == request.NodoId, ct),
                    "CECO_DIM1_NO_ENCONTRADA", "Dimensión 1", request.NodoId);

                var dim2s = _db.Dim2s.AsNoTracking()
                    .Where(d => d.Dim1Id == request.NodoId
                                && d.Estatus != EstatusCatalogo.Inactivo);

                if (request.Nivel == NivelAlcance.GrupoDim2BajoDim1)
                {
                    await VerificarExisteAsync(
                        _db.GruposDim2.AnyAsync(g => g.Id == request.GrupoId!.Value, ct),
                        "CECO_GRUPO_DIM2_NO_ENCONTRADO", "grupo de dimensión 2", request.GrupoId!.Value);
                    dim2s = dim2s.Where(d => d.GrupoDim2Id == request.GrupoId!.Value);
                }

                return await _db.Dim3s.AsNoTracking()
                    .Where(e => e.Estatus != EstatusCatalogo.Inactivo
                                && dim2s.Any(d => d.Id == e.Dim2Id))
                    .Select(e => e.Id)
                    .ToListAsync(ct);
            }

            case NivelAlcance.Dim2:
            case NivelAlcance.GrupoDim3BajoDim2:
            {
                await VerificarExisteAsync(
                    _db.Dim2s.AnyAsync(d => d.Id == request.NodoId, ct),
                    "CECO_DIM2_NO_ENCONTRADA", "Dimensión 2", request.NodoId);

                var dim3s = _db.Dim3s.AsNoTracking()
                    .Where(e => e.Dim2Id == request.NodoId
                                && e.Estatus != EstatusCatalogo.Inactivo);

                if (request.Nivel == NivelAlcance.GrupoDim3BajoDim2)
                {
                    await VerificarExisteAsync(
                        _db.GruposDim3.AnyAsync(g => g.Id == request.GrupoId!.Value, ct),
                        "CECO_GRUPO_DIM3_NO_ENCONTRADO", "grupo de dimensión 3", request.GrupoId!.Value);
                    dim3s = dim3s.Where(e => e.GrupoDim3Id == request.GrupoId!.Value);
                }

                return await dim3s.Select(e => e.Id).ToListAsync(ct);
            }

            case NivelAlcance.Dim3:
            {
                await VerificarExisteAsync(
                    _db.Dim3s.AnyAsync(e => e.Id == request.NodoId, ct),
                    "CECO_DIM3_NO_ENCONTRADA", "Dimensión 3", request.NodoId);

                return await _db.Dim3s.AsNoTracking()
                    .Where(e => e.Id == request.NodoId
                                && e.Estatus != EstatusCatalogo.Inactivo)
                    .Select(e => e.Id)
                    .ToListAsync(ct);
            }

            default:
                throw new BusinessRuleException("CECO_ALCANCE_NIVEL_INVALIDO",
                    $"Nivel de marcado no soportado: {request.Nivel}.");
        }
    }

    private static async Task VerificarExisteAsync(
        Task<bool> existe, string codigo, string etiqueta, Guid id)
    {
        if (!await existe)
            throw new EntityNotFoundException(codigo, $"No existe {etiqueta} con id '{id}'.");
    }
}
