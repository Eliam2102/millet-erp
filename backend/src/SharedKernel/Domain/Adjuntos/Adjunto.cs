using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Domain.Adjuntos;

/// <summary>
/// Metadatos de un archivo adjunto genérico (F1-ADM-11 G1.2): sirve a
/// cualquier entidad dueña identificada por <see cref="TipoEntidad"/> +
/// <see cref="EntidadId"/> (sin FK física: es genérico). El archivo vive en
/// blob storage bajo <see cref="BlobRef"/>, que es interno y jamás se
/// expone en DTOs. Tabla <c>compartido.adjuntos</c>.
///
/// <para>
/// Baja lógica: <see cref="DarDeBaja"/> exige motivo (5-500), es
/// irreversible y no toca el blob. La vigencia (<see cref="VigenteHasta"/>)
/// es una fecha; el estado se deriva con <see cref="EstadoEn"/> recibiendo
/// "hoy" por parámetro (el dominio no lee reloj).
/// </para>
///
/// <para>
/// <see cref="EmpresaId"/> nulo = la entidad dueña es cross-empresa (p. ej.
/// Proveedor). No implementa <see cref="IPerteneceAEmpresa"/>: el
/// aislamiento lo resuelve el propietario de la entidad, no un query filter.
/// </para>
/// </summary>
public sealed partial class Adjunto : BaseEntity, IAuditable, IBelongsToAggregate
{
    /// <summary>Días antes del vencimiento en que pasa a <see cref="EstadoAdjunto.PorVencer"/>.</summary>
    public const int DiasAvisoVencimiento = 30;

    public const int MotivoBajaMin = 5;
    public const int MotivoBajaMax = 500;

    public string TipoEntidad { get; private set; } = string.Empty;

    public Guid EntidadId { get; private set; }

    /// <summary>Agrupa el histórico de auditoría con la entidad dueña.</summary>
    public Guid AggregateRootId => EntidadId;

    public Guid? EmpresaId { get; private set; }

    public Guid TipoDocumentoId { get; private set; }

    public string NombreArchivo { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long TamanoBytes { get; private set; }

    /// <summary>SHA-256 del contenido, hexadecimal minúsculas (64).</summary>
    public string HashSha256 { get; private set; } = string.Empty;

    /// <summary>Clave interna del blob. Nunca se expone en DTOs.</summary>
    public string BlobRef { get; private set; } = string.Empty;

    public DateOnly? VigenteHasta { get; private set; }

    public Guid SubidoPorId { get; private set; }

    public DateTimeOffset SubidoEn { get; private set; }

    public DateTimeOffset? BajaEn { get; private set; }

    public Guid? BajaPorId { get; private set; }

    public string? BajaMotivo { get; private set; }

    public bool EstaDeBaja => BajaEn is not null;

    private Adjunto() { }

    public Adjunto(
        Guid id,
        string tipoEntidad,
        Guid entidadId,
        Guid? empresaId,
        Guid tipoDocumentoId,
        string nombreArchivo,
        string contentType,
        long tamanoBytes,
        string hashSha256,
        string blobRef,
        DateOnly? vigenteHasta,
        Guid subidoPorId,
        DateTimeOffset subidoEn) : base(id)
    {
        if (string.IsNullOrWhiteSpace(tipoEntidad) || tipoEntidad.Length > 60)
        {
            throw new BusinessRuleException(
                "ADJUNTO_TIPO_ENTIDAD_INVALIDO", "El tipo de entidad debe tener 1-60 caracteres.");
        }
        if (entidadId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "ADJUNTO_ENTIDAD_REQUERIDA", "El adjunto debe pertenecer a una entidad.");
        }
        if (tipoDocumentoId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "ADJUNTO_TIPO_REQUERIDO", "El tipo de documento es requerido.");
        }
        if (subidoPorId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "ADJUNTO_USUARIO_REQUERIDO", "El usuario que sube el adjunto es requerido.");
        }
        if (string.IsNullOrWhiteSpace(nombreArchivo)
            || nombreArchivo.Length > 255
            || nombreArchivo.AsSpan().IndexOfAny('/', '\\') >= 0
            || nombreArchivo.Contains('\0'))
        {
            throw new BusinessRuleException(
                "ADJUNTO_NOMBRE_INVALIDO",
                "El nombre del archivo debe tener 1-255 caracteres y no incluir ruta.");
        }
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 120)
        {
            throw new BusinessRuleException(
                "ADJUNTO_CONTENT_TYPE_INVALIDO", "El content type debe tener 1-120 caracteres.");
        }
        if (tamanoBytes <= 0)
        {
            throw new BusinessRuleException(
                "ADJUNTO_TAMANO_INVALIDO", "El tamaño del archivo debe ser mayor a cero.");
        }
        if (!HashRegex().IsMatch(hashSha256 ?? string.Empty))
        {
            throw new BusinessRuleException(
                "ADJUNTO_HASH_INVALIDO", "El hash SHA-256 debe ser de 64 caracteres hexadecimales en minúsculas.");
        }
        if (string.IsNullOrWhiteSpace(blobRef) || blobRef.Length > 500)
        {
            throw new BusinessRuleException(
                "ADJUNTO_BLOB_REF_REQUERIDA", "La referencia del blob es requerida (máx. 500 caracteres).");
        }

        TipoEntidad = tipoEntidad;
        EntidadId = entidadId;
        EmpresaId = empresaId;
        TipoDocumentoId = tipoDocumentoId;
        NombreArchivo = nombreArchivo;
        ContentType = contentType;
        TamanoBytes = tamanoBytes;
        HashSha256 = hashSha256!;
        BlobRef = blobRef;
        VigenteHasta = vigenteHasta;
        SubidoPorId = subidoPorId;
        SubidoEn = subidoEn;
    }

    /// <summary>
    /// Baja lógica irreversible. El blob se conserva. Una sola vez: una
    /// segunda baja es error (<c>ADJUNTO_YA_DADO_DE_BAJA</c>).
    /// </summary>
    public void DarDeBaja(string motivo, Guid usuarioId, DateTimeOffset cuando)
    {
        if (EstaDeBaja)
        {
            throw new BusinessRuleException(
                "ADJUNTO_YA_DADO_DE_BAJA", "El adjunto ya fue dado de baja.");
        }
        var limpio = motivo?.Trim() ?? string.Empty;
        if (limpio.Length < MotivoBajaMin || limpio.Length > MotivoBajaMax)
        {
            throw new BusinessRuleException(
                "ADJUNTO_MOTIVO_BAJA_INVALIDO",
                $"El motivo de baja debe tener entre {MotivoBajaMin} y {MotivoBajaMax} caracteres.");
        }
        if (usuarioId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "ADJUNTO_USUARIO_REQUERIDO", "El usuario que da de baja el adjunto es requerido.");
        }

        BajaEn = cuando;
        BajaPorId = usuarioId;
        BajaMotivo = limpio;
    }

    /// <summary>
    /// Estado a la fecha <paramref name="hoy"/>. Vigente "al 31-oct" sigue
    /// vigente el 31-oct y aparece vencido el 1-nov.
    /// </summary>
    public EstadoAdjunto EstadoEn(DateOnly hoy)
    {
        if (EstaDeBaja) return EstadoAdjunto.Baja;
        if (VigenteHasta is not { } hasta) return EstadoAdjunto.SinVigencia;
        if (hasta < hoy) return EstadoAdjunto.Vencido;
        return hasta <= hoy.AddDays(DiasAvisoVencimiento)
            ? EstadoAdjunto.PorVencer
            : EstadoAdjunto.Vigente;
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HashRegex();
}
