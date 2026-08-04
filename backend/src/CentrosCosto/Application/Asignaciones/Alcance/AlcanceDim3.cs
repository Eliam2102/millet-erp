using Millet.CentrosCosto.Domain;

namespace Millet.CentrosCosto.Application.Asignaciones.Alcance;

/// <summary>Tipo del alcance resuelto para el usuario actual (§7).</summary>
public enum TipoAlcanceDim3 : short
{
    /// <summary>Permiso <c>centros_costo.dim3.leer-todos</c>: sin filtro (Contabilidad).</summary>
    Total = 1,

    /// <summary>Set congelado de Dim3 asignadas al usuario.</summary>
    Ids = 2,

    /// <summary>Sin filas de asignación (o sin usuario): selector vacío.</summary>
    Ninguno = 3,
}

/// <summary>
/// Alcance de máquinas resuelto. La regla vive una sola vez: los queries
/// del selector hacen <c>q = alcance.AplicarA(q)</c> (molde
/// <c>AlcanceCajas</c>, sin comodines — el set ya está congelado).
/// </summary>
public sealed class AlcanceDim3
{
    public TipoAlcanceDim3 Tipo { get; }
    public IReadOnlySet<Guid> Dim3Ids { get; }

    public bool EsTotal => Tipo == TipoAlcanceDim3.Total;

    private AlcanceDim3(TipoAlcanceDim3 tipo, IReadOnlySet<Guid> dim3Ids)
    {
        Tipo = tipo;
        Dim3Ids = dim3Ids;
    }

    public static AlcanceDim3 Total() => new(TipoAlcanceDim3.Total, new HashSet<Guid>());

    public static AlcanceDim3 Ninguno() => new(TipoAlcanceDim3.Ninguno, new HashSet<Guid>());

    /// <summary>Alcance por set; un set vacío degrada a <see cref="TipoAlcanceDim3.Ninguno"/>.</summary>
    public static AlcanceDim3 De(IReadOnlySet<Guid> dim3Ids) =>
        dim3Ids.Count == 0 ? Ninguno() : new(TipoAlcanceDim3.Ids, dim3Ids);

    public IQueryable<Dim3> AplicarA(IQueryable<Dim3> query) => Tipo switch
    {
        TipoAlcanceDim3.Total => query,
        TipoAlcanceDim3.Ninguno => query.Where(_ => false),
        _ => query.Where(e => Dim3Ids.Contains(e.Id)),
    };
}
