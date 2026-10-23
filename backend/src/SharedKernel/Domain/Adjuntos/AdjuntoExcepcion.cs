using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Domain.Adjuntos;

/// <summary>
/// Excepción explícita a un documento obligatorio del expediente (86e3pdmee ·
/// ADM-01, R-ADM-04): con permiso y motivo, el expediente cuenta el tipo como
/// cubierto. Se revoca, no se borra. Tabla <c>compartido.adjunto_excepciones</c>.
/// </summary>
public sealed class AdjuntoExcepcion : BaseEntity, IAuditable
{
    public string TipoEntidad { get; private set; } = string.Empty;
    public Guid EntidadId { get; private set; }
    public Guid TipoDocumentoId { get; private set; }
    public string Motivo { get; private set; } = string.Empty;
    public Guid? OtorgadaPorId { get; private set; }
    public string? OtorgadaPorNombre { get; private set; }
    public DateTimeOffset OtorgadaEn { get; private set; }
    public DateTimeOffset? RevocadaEn { get; private set; }
    public Guid? RevocadaPorId { get; private set; }

    public bool Vigente => RevocadaEn is null;

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
        OtorgadaPorId = usuarioId;
        OtorgadaPorNombre = usuarioNombre;
        OtorgadaEn = ahora;
    }

    public void Revocar(Guid? usuarioId, DateTimeOffset ahora)
    {
        if (RevocadaEn is not null)
            throw new BusinessRuleException("ADJUNTO_EXCEPCION_YA_REVOCADA", "La excepción ya estaba revocada.");
        RevocadaEn = ahora;
        RevocadaPorId = usuarioId;
    }
}
