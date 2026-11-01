using System.Net;
using Millet.SharedKernel.Application.Correo;

namespace Millet.SharedKernel.Infrastructure.Correo;

/// <summary>
/// Documentos con PDF (OC al proveedor, F1-COM-10) por el transporte único
/// (86e3txv0n). <see cref="EsSimulado"/> sigue al proveedor: con Graph o
/// SandboxLocal la pantalla deja de decir «simulado». Un rechazo se lanza para
/// que el consumidor marque el envío como fallido.
/// </summary>
public sealed class CorreoDocumentoService(ICorreoSaliente transporte) : ICorreoDocumentoService
{
    public const string Tipo = "documento";

    public bool EsSimulado => transporte.EsSimulado;

    public async Task EnviarAsync(CorreoDocumento correo, CancellationToken cancellationToken)
    {
        var resultado = await transporte.EnviarAsync(Construir(correo), cancellationToken);
        if (resultado.Estado == EstadoCorreoSaliente.Error)
            throw new InvalidOperationException($"{resultado.Codigo}: {resultado.Motivo}");
    }

    public static MensajeCorreo Construir(CorreoDocumento d)
    {
        var asunto = WebUtility.HtmlEncode(d.Asunto);
        var html = "<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>" +
                   $"<h2>{asunto}</h2><p>Adjuntamos el documento <strong>{WebUtility.HtmlEncode(d.NombreArchivo)}</strong>.</p>" +
                   "<p>Si tiene alguna aclaración, responda a este correo.</p><p>Millet</p></div>";
        return new MensajeCorreo(d.EmpresaId, d.Modulo, Tipo, d.ReferenciaId, [d.Destinatario], d.Asunto, html,
            [new AdjuntoCorreo(d.NombreArchivo, "application/pdf", d.Pdf)]);
    }
}
