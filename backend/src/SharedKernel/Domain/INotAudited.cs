namespace Millet.SharedKernel.Domain;

/// <summary>
/// Marker explícito para entidades que NO se auditan: outbox, eventos
/// procesados, caches temporales, etc. Antagonista de <see cref="IAuditable"/>:
/// un test/linter de CI exige que toda entidad derivada de <see cref="BaseEntity"/>
/// declare uno de los dos, evitando olvidos por descuido.
/// Ver ADR-0008.
/// </summary>
public interface INotAudited
{
}
