namespace Millet.SharedKernel.Application.Correo;

/// <summary>Salida de documentos por el servicio de notificaciones; aceptación no equivale a entrega.</summary>
public interface ICorreoDocumentoService
{
    bool EsSimulado { get; }
    Task EnviarAsync(CorreoDocumento correo, CancellationToken cancellationToken);
}

public sealed record CorreoDocumento(Guid EmpresaId, string Destinatario, string Asunto, string NombreArchivo, byte[] Pdf);
