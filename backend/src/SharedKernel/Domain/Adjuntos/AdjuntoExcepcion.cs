using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Domain.Adjuntos;

/// <summary>
/// Excepción explícita a un documento obligatorio del expediente (86e3pdmee ·
/// ADM-01, R-ADM-04). Desde 86e3yzemu (ADM-proveedor-10) se pide
/// (<see cref="EstadoExcepcionAdjunto.Pendiente"/>) y otra persona la resuelve
/// (otorgada o negada, con usuario y fecha): solo la otorgada cuenta el tipo como
/// cubierto. Se revoca, no se borra. Tabla <c>compartido.adjunto_excepciones</c>.
/// </summary>
public sealed class AdjuntoExcepcion : BaseEntity, IAuditable
{
    public string TipoEntidad { get; private set; } = string.Empty;
    public Guid EntidadId { get; private set; }
    public Guid TipoDocumentoId { get; private set; }
    public string Motivo { get; private set; } = string.Empty;
    public EstadoExcepcionAdjunto Estado { get; private set; }
    public Guid? SolicitadaPorId { get; private set; }
    public string? SolicitadaPorNombre { get; private set; }
    public DateTimeOffset SolicitadaEn { get; private set; }
    public Guid? ResueltaPorId { get; private set; }
    public string? ResueltaPorNombre { get; private set; }
    public DateTimeOffset? ResueltaEn { get; private set; }
    /// <summary>Obligatorio al negar; opcional al otorgar.</summary>
    public string? MotivoResolucion { get; private set; }
    public DateTimeOffset? RevocadaEn { get; private set; }
    public Guid? RevocadaPorId { get; private set; }

    /// <summary>Otorgada y sin revocar: es la única que cubre el documento.</summary>
    public bool Vigente => Estado == EstadoExcepcionAdjunto.Otorgada && RevocadaEn is null;

    private AdjuntoExcepcion() { }

    public AdjuntoExcepcion(
        string tipoEntidad, Guid entidadId, Guid tipoDocumentoId, string motivo,
        Guid? usuarioId, string? usuarioNombre, DateTimeOffset ahora) : base(Guid.CreateVersion7())
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length is < 10 or > 500)
            throw new BusinessRuleException("ADJUNTO_EXCEPCION_MOTIVO_INVALIDO",
                "Explica la excepción en 10 a 500 caracteres.");
        TipoEntidad = tipoEntidad;
        EntidadId = entidadId;
        TipoDocumentoId = tipoDocumentoId;
        Motivo = motivo.Trim();
        Estado = EstadoExcepcionAdjunto.Pendiente;
        SolicitadaPorId = usuarioId;
        SolicitadaPorNombre = usuarioNombre;
        SolicitadaEn = ahora;
    }

    public void Otorgar(Guid? usuarioId, string? usuarioNombre, DateTimeOffset ahora, string? motivoResolucion = null) =>
        Resolver(EstadoExcepcionAdjunto.Otorgada, usuarioId, usuarioNombre, ahora, motivoResolucion);

    public void Negar(Guid? usuarioId, string? usuarioNombre, DateTimeOffset ahora, string? motivoResolucion) =>
        Resolver(EstadoExcepcionAdjunto.Negada, usuarioId, usuarioNombre, ahora, motivoResolucion);

    private void Resolver(EstadoExcepcionAdjunto resultado, Guid? usuarioId, string? usuarioNombre, DateTimeOffset ahora, string? motivoResolucion)
    {
        if (Estado != EstadoExcepcionAdjunto.Pendiente || RevocadaEn is not null)
            throw new BusinessRuleException("ADJUNTO_EXCEPCION_NO_PENDIENTE", "La excepción ya fue resuelta.");
        if (usuarioId is not null && usuarioId == SolicitadaPorId)
            throw new BusinessRuleException("ADJUNTO_EXCEPCION_MISMO_USUARIO", "Otra persona debe resolver la excepción que pediste.");
        var limpio = string.IsNullOrWhiteSpace(motivoResolucion) ? null : motivoResolucion.Trim();
        if (limpio is { Length: > 500 } || (resultado == EstadoExcepcionAdjunto.Negada && limpio is not { Length: >= 5 }))
            throw new BusinessRuleException("ADJUNTO_EXCEPCION_RESOLUCION_INVALIDA",
                "Al negar la excepción explica el motivo (de 5 a 500 caracteres).");
        Estado = resultado;
        ResueltaPorId = usuarioId;
        ResueltaPorNombre = usuarioNombre;
        ResueltaEn = ahora;
        MotivoResolucion = limpio;
    }

    public void Revocar(Guid? usuarioId, DateTimeOffset ahora)
    {
        if (RevocadaEn is not null)
            throw new BusinessRuleException("ADJUNTO_EXCEPCION_YA_REVOCADA", "La excepción ya estaba revocada.");
        if (Estado != EstadoExcepcionAdjunto.Otorgada)
            throw new BusinessRuleException("ADJUNTO_EXCEPCION_NO_OTORGADA", "Solo se revoca una excepción otorgada.");
        RevocadaEn = ahora;
        RevocadaPorId = usuarioId;
    }
}
