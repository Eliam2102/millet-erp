using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Puerto out-going que notifica al Glass Agent, vía POST server-to-server,
/// que el PDF de A+W de una <see cref="EntidadExterna"/> quedó adjunto y
/// disponible. El Agent persiste <c>pdf_url</c> al instante (sin depender de
/// que la pestaña del vendedor esté abierta) — entrega "live".
///
/// <para>
/// <b>Best-effort por diseño</b>: la implementación NO debe lanzar
/// excepciones. El PDF ya está persistido en el ERP y el cron del reconcile
/// del Agent es el respaldo garantizado; un fallo del webhook solo pierde la
/// inmediatez, no el dato. Loguear warning y seguir.
/// </para>
///
/// <para>
/// Se invoca UNA sola vez por PDF: el <c>AwDocumentSyncWorker</c> lo llama
/// dentro de <c>AdjuntarPdfAsync</c>, que solo corre en el primer adjuntado
/// (<c>PdfBlobUrl is null</c>). Del lado Agent la persistencia es idempotente.
/// </para>
/// </summary>
public interface IAgentDocumentoWebhook
{
    Task NotifyPdfListoAsync(EntidadExterna entidad, CancellationToken cancellationToken);
}
