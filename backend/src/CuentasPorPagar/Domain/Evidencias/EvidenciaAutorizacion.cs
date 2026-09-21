using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Evidencias;

/// <summary>
/// Soporte digital de autorización informal (§4.10 del 00-levantamiento,
/// §3.bis.4 del 01-diseno). Polimórfico: apunta a 4 tipos de documento
/// vía <c>(TipoDocumento, DocumentoId)</c> con CHECK constraint en BD.
///
/// <para>
/// El binario vive en blob storage (ADR-0024); esta entidad solo guarda
/// la ref (<see cref="ArchivoBlobRef"/>), tipo de medio, comentario y
/// estado de firma física. Multi-tenant + fiscalmente relevante:
/// los query filters del BaseDbContext aplican empresa + soft-delete.
/// </para>
/// </summary>
public sealed class EvidenciaAutorizacion : BaseEntity, IPerteneceAEmpresa, IFiscalmenteRelevante, IAuditable
{
    public Guid EmpresaId { get; set; }

    public TipoDocumentoEvidencia TipoDocumento { get; private set; }
    public Guid DocumentoId { get; private set; }

    public TipoEvidencia Tipo { get; private set; }

    public string ArchivoBlobRef { get; private set; } = default!;
    public string NombreArchivo { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long? TamanioBytes { get; private set; }

    /// <summary>Quién autorizó (rol + nombre), cuándo, medio.</summary>
    public string Comentario { get; private set; } = default!;

    public EstadoFirmaFisica EstadoFirmaFisica { get; private set; }
    public DateOnly? FechaLimiteFirmaFisica { get; private set; }
    public DateTimeOffset? FechaRecepcionFirmaFisica { get; private set; }

    public Guid? CapturadoPor { get; private set; }
    public DateTimeOffset FechaCaptura { get; private set; }

    private EvidenciaAutorizacion() { }

    public static EvidenciaAutorizacion Adjuntar(
        Guid empresaId,
        TipoDocumentoEvidencia tipoDocumento,
        Guid documentoId,
        TipoEvidencia tipo,
        string archivoBlobRef,
        string nombreArchivo,
        string contentType,
        long? tamanioBytes,
        string comentario,
        EstadoFirmaFisica estadoFirmaFisica,
        DateOnly? fechaLimiteFirmaFisica,
        Guid? capturadoPor,
        DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(archivoBlobRef))
            throw new BusinessRuleException("EVIDENCIA_BLOB_REF_VACIA", "La referencia al archivo en blob es obligatoria.");
        if (string.IsNullOrWhiteSpace(nombreArchivo))
            throw new BusinessRuleException("EVIDENCIA_NOMBRE_VACIO", "El nombre del archivo es obligatorio.");
        if (string.IsNullOrWhiteSpace(comentario))
            throw new BusinessRuleException("EVIDENCIA_COMENTARIO_VACIO",
                "El comentario es obligatorio — quién autorizó (rol+nombre), cuándo y medio.");
        if (estadoFirmaFisica == EstadoFirmaFisica.Pendiente && fechaLimiteFirmaFisica is null)
            throw new BusinessRuleException("EVIDENCIA_FECHA_LIMITE_REQUERIDA",
                "Si la firma física está Pendiente, debe especificarse una fecha límite.");

        return new EvidenciaAutorizacion
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            TipoDocumento = tipoDocumento,
            DocumentoId = documentoId,
            Tipo = tipo,
            ArchivoBlobRef = archivoBlobRef,
            NombreArchivo = nombreArchivo,
            ContentType = contentType,
            TamanioBytes = tamanioBytes,
            Comentario = comentario,
            EstadoFirmaFisica = estadoFirmaFisica,
            FechaLimiteFirmaFisica = fechaLimiteFirmaFisica,
            CapturadoPor = capturadoPor,
            FechaCaptura = ahora,
        };
    }

    public void MarcarFirmaFisicaRecibida(DateTimeOffset ahora)
    {
        if (EstadoFirmaFisica != EstadoFirmaFisica.Pendiente)
        {
            throw new BusinessRuleException(
                "EVIDENCIA_FIRMA_NO_PENDIENTE",
                $"Solo evidencias con firma Pendiente pueden marcarse Recibidas (actual: {EstadoFirmaFisica}).");
        }
        EstadoFirmaFisica = EstadoFirmaFisica.Recibida;
        FechaRecepcionFirmaFisica = ahora;
    }
}
