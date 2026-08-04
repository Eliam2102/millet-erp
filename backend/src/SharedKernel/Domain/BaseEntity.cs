namespace Millet.SharedKernel.Domain;

/// <summary>
/// Base para toda entidad de dominio. Define la metadata transversal que
/// el BaseDbContext gestiona automáticamente (concurrency token, auditoría
/// temporal, soft delete).
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Identificador único. GUID v7 (ordenable temporalmente). Asignado
    /// en el constructor de la entidad concreta vía <c>Guid.CreateVersion7()</c>.
    /// </summary>
    public Guid Id { get; protected set; }

    /// <summary>
    /// Concurrency token. Incrementado automáticamente por el BaseDbContext
    /// en cada <c>SaveChanges</c>. Configurado como <c>IsConcurrencyToken</c>
    /// en EF Core. Ver ADR-0012.
    /// </summary>
    public int Version { get; protected internal set; }

    public DateTimeOffset CreatedAt { get; protected internal set; }

    public DateTimeOffset UpdatedAt { get; protected internal set; }

    public string? CreatedBy { get; protected internal set; }

    public string? UpdatedBy { get; protected internal set; }

    /// <summary>
    /// Fecha de borrado lógico. NULL si la entidad está activa. Aplica solo
    /// a entidades que implementan <see cref="IFiscalmenteRelevante"/>; para
    /// las demás queda siempre NULL. El query filter del BaseDbContext
    /// excluye automáticamente las entidades con <c>DeletedAt</c> != null.
    /// Ver ADR-0008.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; protected internal set; }

    /// <summary>Constructor para EF Core. Las entidades concretas usan el constructor con <see cref="Guid"/>.</summary>
    protected BaseEntity()
    {
    }

    protected BaseEntity(Guid id)
    {
        Id = id;
    }
}
