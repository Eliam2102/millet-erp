using Microsoft.EntityFrameworkCore;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CentrosCosto.Application.Asignaciones.Alcance;

/// <summary>
/// Implementación de <see cref="IAlcanceDim3Evaluator"/> sobre
/// <c>centros_costo.asignaciones</c> (molde <c>AlcanceCajaEvaluator</c>:
/// bypass por permiso primero, luego el set del usuario).
///
/// <para>
/// SIN re-evaluación en vivo (§7, decisión de negocio cerrada): esto lee
/// las filas congeladas tal cual — una máquina creada después de marcar
/// NO aparece aquí hasta que alguien re-marque. No "mejorar" este
/// evaluador expandiendo reglas al vuelo: rompería esa decisión (el test
/// explícito de CECO-PR6 la protege).
/// </para>
/// </summary>
public sealed class AlcanceDim3Evaluator : IAlcanceDim3Evaluator
{
    /// <summary>
    /// Espejo de <c>PermisosCanonicos.CentrosCostoDim3LeerTodos</c>
    /// (Identidad). El módulo no referencia Identidad; el código canónico
    /// es contrato estable (ADR-0007, seed 0000000c-0003-…-0001).
    /// </summary>
    internal const string PermisoLeerTodos = "centros_costo.dim3.leer-todos";

    private readonly CentrosCostoDbContext _db;
    private readonly ICurrentUserContext _user;
    private readonly ICurrentUserPermissions _permisos;

    public AlcanceDim3Evaluator(
        CentrosCostoDbContext db,
        ICurrentUserContext user,
        ICurrentUserPermissions permisos)
    {
        _db = db;
        _user = user;
        _permisos = permisos;
    }

    public async Task<AlcanceDim3> ResolverAsync(CancellationToken cancellationToken)
    {
        if (await _permisos.TieneAsync(PermisoLeerTodos, cancellationToken))
            return AlcanceDim3.Total();

        if (_user.UserId is not Guid usuarioId)
            return AlcanceDim3.Ninguno();

        var ids = await _db.Asignaciones.AsNoTracking()
            .Where(a => a.UsuarioId == usuarioId)
            .Select(a => a.Dim3Id)
            .ToListAsync(cancellationToken);

        return AlcanceDim3.De(ids.ToHashSet());
    }
}
