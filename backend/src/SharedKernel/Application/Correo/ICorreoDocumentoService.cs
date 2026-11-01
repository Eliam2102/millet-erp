namespace Millet.SharedKernel.Application.Correo;

/// <summary>Salida de documentos por el servicio de notificaciones; aceptación no equivale a entrega.</summary>
public interface ICorreoDocumentoService
{
    bool EsSimulado { get; }
    Task EnviarAsync(CorreoDocumento correo, CancellationToken cancellationToken);
}

/// <summary><paramref name="Modulo"/> y <paramref name="ReferenciaId"/> identifican el envío en <c>core.correo_envio</c>.</summary>
public sealed record CorreoDocumento(Guid EmpresaId, string Destinatario, string Asunto, string NombreArchivo, byte[] Pdf,
    string Modulo = "documentos", Guid? ReferenciaId = null);
