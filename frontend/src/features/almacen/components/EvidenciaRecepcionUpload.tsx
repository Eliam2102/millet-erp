import { type ChangeEvent, useRef } from 'react';
import { FileText, Loader2, Upload, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { useSubirPackingListBlob } from '@/features/almacen/api/useRecepciones';
import { esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';

const MAX_BYTES = 20 * 1024 * 1024; // alineado al backend
const ACEPTA = 'image/jpeg,image/png,image/webp,application/pdf';

/**
 * G-10: foto o documento que respalda lo recibido dañado o rechazado en el acto. Sube a Almacén con el
 * mismo endpoint de blobs de la recepción (mismo permiso) y devuelve la referencia que viaja en `evidenciaBlobRef`.
 */
export function EvidenciaRecepcionUpload({
  nombre,
  onChange,
  error,
  etiqueta,
}: {
  /** Nombre del archivo ya subido, o null si todavía no hay. */
  nombre: string | null | undefined;
  onChange: (valor: { blobRef: string; nombre: string } | null) => void;
  error?: string;
  etiqueta: string;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const idempotencyKey = useFormIdempotencyKey();
  const subir = useSubirPackingListBlob();

  async function elegir(e: ChangeEvent<HTMLInputElement>) {
    const archivo = e.target.files?.[0];
    e.target.value = '';
    if (!archivo) return;
    if (archivo.size === 0 || archivo.size > MAX_BYTES) {
      toast.error('El archivo está vacío o excede 20 MB.');
      return;
    }
    try {
      const r = await subir.mutateAsync({ archivo, idempotencyKey });
      onChange({ blobRef: r.blobRef, nombre: r.nombreArchivo });
    } catch (err) {
      if (esApiError(err)) toast.error(err.problem.title, { description: codigoSoporte(err) });
      else toast.error('Error inesperado al subir la evidencia.');
    }
  }

  return (
    <div className="space-y-1">
      {nombre ? (
        <div className="flex items-center justify-between gap-2 rounded-md border bg-surface-muted/30 px-3 py-2">
          <span className="flex min-w-0 items-center gap-2 text-sm">
            <FileText className="h-4 w-4 shrink-0 text-brand" />
            <span className="truncate">{nombre}</span>
          </span>
          <Button type="button" variant="ghost" size="sm" onClick={() => onChange(null)} aria-label={`Quitar ${etiqueta}`}>
            <X className="h-4 w-4" />
          </Button>
        </div>
      ) : (
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={subir.isPending}
          onClick={() => inputRef.current?.click()}
          aria-label={etiqueta}
        >
          {subir.isPending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Upload className="mr-2 h-4 w-4" />}
          Adjuntar foto o documento
        </Button>
      )}
      <input
        ref={inputRef}
        type="file"
        className="sr-only"
        accept={ACEPTA}
        onChange={elegir}
        tabIndex={-1}
        aria-label={`Archivo de ${etiqueta}`}
      />
      {error && (
        <p role="alert" className="text-xs text-danger-fg">
          {error}
        </p>
      )}
    </div>
  );
}
