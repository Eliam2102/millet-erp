import { Mail } from 'lucide-react';

/**
 * <c>&lt;UploadCorreoAutorizacion/&gt;</c> — slot para adjuntar el
 * correo/PDF de autorización cuando el modo del Sheet "Nueva OC" es
 * <b>"Sin RQ previa"</b> (FOC11). El comprador captura el motivo +
 * adjunta evidencia de la autorización paralela (correo del
 * solicitante, cotización aprobada, etc.).
 *
 * <para><b>Stub UF2-PR1</b>: el componente real
 * <c>&lt;AdjuntosManager/&gt;</c> cross-módulo (FOC2) se promueve a
 * <c>components/erp/adjuntos/</c> en UF3-PR2 con drag-and-drop +
 * preview de PDFs + tipos de documento. Por ahora el panel es un
 * placeholder visible que aclara el flujo: el comprador SÍ debería
 * tener el documento listo para adjuntar al crear, pero la subida
 * real entra después.</para>
 */
export function UploadCorreoAutorizacion() {
  return (
    <div
      className="rounded-md border border-dashed border-amber-300 bg-amber-50 p-3 text-xs text-amber-900"
      data-component="upload-correo-autorizacion-stub"
    >
      <div className="mb-1 flex items-center gap-1.5 font-medium">
        <Mail className="h-3.5 w-3.5" aria-hidden="true" />
        Adjunto del correo de autorización
      </div>
      <p>
        Como esta OC va sin requisición previa, el flujo final pedirá adjuntar
        evidencia (correo del solicitante, cotización aprobada). El uploader
        real llega en <strong>UF3-PR2</strong> con
        <code className="mx-1 font-mono">{'<AdjuntosManager>'}</code>; por
        ahora solo capturamos el motivo de aquí abajo. Adjunta la evidencia al
        detalle de la OC tras crearla.
      </p>
    </div>
  );
}
