using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Aw.Domain;

/// <summary>
/// Último LSN de CDC de A+W ya procesado por entidad ("Cliente"/"Producto"). Telemetría operativa, sin auditoría.
/// Solo avanza tras aplicar el lote completo: si algo falla, el siguiente ciclo relee (aplicar es idempotente).
/// </summary>
public sealed class AwCdcWatermark : BaseEntity, INotAudited
{
    public string Entidad { get; private set; } = string.Empty;
    public string Lsn { get; private set; } = string.Empty;
    public DateTimeOffset ActualizadoEnUtc { get; private set; }

    private AwCdcWatermark() { } // EF Core

    public static AwCdcWatermark Crear(string entidad, string lsn, DateTimeOffset ahora) =>
        new() { Id = Guid.CreateVersion7(), Entidad = entidad, Lsn = lsn, ActualizadoEnUtc = ahora };

    public void Avanzar(string lsn, DateTimeOffset ahora)
    {
        Lsn = lsn;
        ActualizadoEnUtc = ahora;
    }
}
