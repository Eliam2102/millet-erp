import {
  type ChangeEvent,
  type DragEvent,
  useRef,
  useState,
} from 'react';
import { FileText, Loader2, Upload, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { useSubirEvidenciaBlob } from '@/features/almacen/api/useDevolucionesProveedor';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { cn } from '@/lib/utils';

const MAX_BYTES = 20 * 1024 * 1024;
const MIME_WHITELIST = [
  'application/pdf',
  'image/jpeg',
  'image/png',
  'image/webp',
  'image/heic',
  'image/heif',
  'video/mp4',
  'video/quicktime',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'application/vnd.ms-excel',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/msword',
  'message/rfc822',
  'text/plain',
] as const;

export interface EvidenciaUploadValue {
  blobRef: string;
  nombreArchivo: string;
  tamanoBytes: number;
  contentType: string;
}

export interface EvidenciaUploadProps {
  value: EvidenciaUploadValue | null;
  onChange: (value: EvidenciaUploadValue | null) => void;
  disabled?: boolean;
  error?: string;
}

/**
 * <c>&lt;EvidenciaUpload/&gt;</c> — drag-and-drop + file picker para
 * evidencias de devolución a proveedor (FE-F4-PR1). Endpoint backend
 * PR #260. Acepta evidencias amplias (fotos HEIC, video corto, .eml).
 */
export function EvidenciaUpload({
  value,
  onChange,
  disabled,
  error,
}: EvidenciaUploadProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [isDraggingOver, setIsDraggingOver] = useState(false);
  const idempotencyKey = useFormIdempotencyKey();
  const subir = useSubirEvidenciaBlob();

  async function procesarArchivo(file: File) {
    if (file.size === 0) {
      toast.error('El archivo está vacío.');
      return;
    }
    if (file.size > MAX_BYTES) {
      toast.error(`El archivo excede el tope de ${MAX_BYTES / (1024 * 1024)} MB.`);
      return;
    }
    if (!MIME_WHITELIST.includes(file.type as (typeof MIME_WHITELIST)[number])) {
      toast.error(
        `Tipo de archivo no permitido (${file.type || 'desconocido'}).`,
      );
      return;
    }

    try {
      const result = await subir.mutateAsync({ archivo: file, idempotencyKey });
      onChange(result);
      toast.success(`Evidencia subida: ${result.nombreArchivo}`);
    } catch (err) {
      if (esApiError(err)) {
        toast.error(err.problem.title, {
          description: err.traceId ? `Código: ${err.traceId}` : undefined,
        });
      } else {
        toast.error('Error inesperado al subir el archivo.');
      }
    }
  }

  function handleDrop(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setIsDraggingOver(false);
    if (disabled) return;
    const file = e.dataTransfer.files[0];
    if (file) procesarArchivo(file);
  }

  function handleDragOver(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    if (!disabled) setIsDraggingOver(true);
  }

  function handleDragLeave(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setIsDraggingOver(false);
  }

  function handleFileChange(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (file) procesarArchivo(file);
    e.target.value = '';
  }

  if (value) {
    return (
      <div className="flex items-center justify-between gap-3 rounded-md border bg-muted/30 px-3 py-2">
        <div className="flex min-w-0 items-center gap-2">
          <FileText className="h-5 w-5 shrink-0 text-primary" />
          <div className="min-w-0">
            <p className="truncate text-sm font-medium">{value.nombreArchivo}</p>
            <p className="text-xs text-muted-foreground">
              {formatearBytes(value.tamanoBytes)} · {value.contentType}
            </p>
          </div>
        </div>
        {!disabled && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => onChange(null)}
            aria-label="Quitar evidencia"
          >
            <X className="h-4 w-4" />
          </Button>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-1.5">
      <div
        onDrop={handleDrop}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        className={cn(
          'rounded-md border-2 border-dashed px-4 py-6 text-center transition-colors',
          isDraggingOver
            ? 'border-primary bg-primary/5'
            : 'border-muted-foreground/30',
          (disabled || subir.isPending) && 'opacity-60 pointer-events-none',
          error && 'border-rose-400',
        )}
      >
        {subir.isPending ? (
          <div className="flex flex-col items-center gap-2 text-muted-foreground">
            <Loader2 className="h-6 w-6 animate-spin" />
            <p className="text-sm">Subiendo evidencia…</p>
          </div>
        ) : (
          <>
            <Upload className="mx-auto h-6 w-6 text-muted-foreground" />
            <p className="mt-2 text-sm text-muted-foreground">
              Arrastra una evidencia aquí, o
              <button
                type="button"
                className="ml-1 font-medium text-primary hover:underline"
                onClick={() => inputRef.current?.click()}
                disabled={disabled}
              >
                selecciona un archivo
              </button>
            </p>
            <p className="mt-1 text-xs text-muted-foreground">
              Foto (incluye HEIC), video corto, PDF, Office o email · máx 20 MB
            </p>
          </>
        )}
        <input
          ref={inputRef}
          type="file"
          className="sr-only"
          accept={MIME_WHITELIST.join(',')}
          onChange={handleFileChange}
          disabled={disabled || subir.isPending}
        />
      </div>
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}

function formatearBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}
