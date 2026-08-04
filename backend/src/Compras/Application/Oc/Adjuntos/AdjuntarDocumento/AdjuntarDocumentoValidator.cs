using FluentValidation;

namespace Millet.Compras.Application.Oc.Adjuntos.AdjuntarDocumento;

public sealed class AdjuntarDocumentoValidator : AbstractValidator<AdjuntarDocumentoCommand>
{
    public AdjuntarDocumentoValidator()
    {
        RuleFor(c => c.OrdenCompraId).NotEmpty().WithErrorCode("OC_ID_REQUERIDA");
        RuleFor(c => c.TipoDocumentoId).NotEmpty().WithErrorCode("TIPO_DOCUMENTO_REQUERIDO");
        RuleFor(c => c.NombreArchivo)
            .NotEmpty().WithErrorCode("NOMBRE_ARCHIVO_REQUERIDO")
            .MaximumLength(255).WithErrorCode("NOMBRE_ARCHIVO_DEMASIADO_LARGO");
        RuleFor(c => c.BlobUrl)
            .NotEmpty().WithErrorCode("BLOB_URL_REQUERIDA");
        RuleFor(c => c.ContentType)
            .NotEmpty().WithErrorCode("CONTENT_TYPE_REQUERIDO")
            .MaximumLength(120).WithErrorCode("CONTENT_TYPE_DEMASIADO_LARGO");
        RuleFor(c => c.TamañoBytes)
            .GreaterThan(0).WithErrorCode("TAMANO_INVALIDO");
    }
}
