import { FileText, ImageIcon, FileQuestion } from 'lucide-react';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;AdjuntoPreview/&gt;</c> — preview compacto de un archivo
 * adjunto. Cross-módulo: solo necesita <c>blobUrl</c> +
 * <c>contentType</c>; consumible desde cualquier módulo que tenga
 * adjuntos (UF3-PR2 lo wira en OC; CxP/Activos consumirán lo mismo).
 *
 * <list>
 *   <item><b>PDFs</b>: muestra la primera página vía <c>&lt;embed&gt;</c>
 *   nativo del navegador (sin dependencias extra como pdf.js — el
 *   doc UF3-PR2 explícitamente lo descartó).</item>
 *   <item><b>Imágenes</b> (jpg/png/webp/gif): thumbnail con
 *   <c>&lt;img&gt;</c>.</item>
 *   <item><b>Otros</b>: ícono genérico + nombre del archivo.</item>
 * </list>
 *
 * <para>El preview es passive — no fetchea el blob por sí mismo. El
 * caller debe asegurar que el navegador puede acceder a la URL (CORS
 * o blob storage público). En dev con stub filesystem, las URLs
 * <c>file:///...</c> NO se renderizan en navegadores: cae al ícono
 * genérico, lo cual es esperado.</para>
 */
export interface AdjuntoPreviewProps {
  /** URL del blob storage (Azure / filesystem stub). */
  blobUrl: string;
  /** MIME type del archivo. */
  contentType: string;
  /** Nombre original del archivo (para alt + tooltip). */
  nombreArchivo: string;
  /** Tamaño en pixels del preview (lado más grande). Default 96. */
  size?: number;
  className?: string;
}

export function AdjuntoPreview({
  blobUrl,
  contentType,
  nombreArchivo,
  size = 96,
  className,
}: AdjuntoPreviewProps) {
  const styles = {
    width: `${size}px`,
    height: `${size}px`,
  };

  const isPdf = contentType === 'application/pdf';
  const isImage = contentType.startsWith('image/');

  // Renderable directo en el browser: http(s) (Azure/SAS en prod) o un
  // object URL `blob:` (contenido bajado por el caller vía endpoint
  // autenticado, ADR-0024). El blobUrl crudo del storage (file:// en dev)
  // NO es navegable: el caller debe pasar el object URL ya resuelto.
  // Mientras carga (o sin resolver) llega vacío → cae al ícono genérico.
  const renderableInBrowser =
    blobUrl.startsWith('http://') ||
    blobUrl.startsWith('https://') ||
    blobUrl.startsWith('blob:');

  if (isPdf && renderableInBrowser) {
    return (
      <div
        className={cn(
          'overflow-hidden rounded-md border bg-muted',
          className,
        )}
        style={styles}
        title={nombreArchivo}
        data-component="adjunto-preview-pdf"
      >
        <embed
          src={`${blobUrl}#toolbar=0&navpanes=0&scrollbar=0`}
          type="application/pdf"
          width={size}
          height={size}
          className="block"
        />
      </div>
    );
  }

  if (isImage && renderableInBrowser) {
    return (
      <img
        src={blobUrl}
        alt={nombreArchivo}
        title={nombreArchivo}
        loading="lazy"
        className={cn(
          'rounded-md border object-cover',
          className,
        )}
        style={styles}
        data-component="adjunto-preview-img"
      />
    );
  }

  // Fallback: ícono según content-type.
  const Icon = isPdf
    ? FileText
    : isImage
      ? ImageIcon
      : FileQuestion;

  return (
    <div
      className={cn(
        'flex flex-col items-center justify-center rounded-md border bg-muted text-muted-foreground',
        className,
      )}
      style={styles}
      title={nombreArchivo}
      data-component="adjunto-preview-icon"
    >
      <Icon className="h-1/2 w-1/2" aria-hidden="true" />
      <span className="mt-1 line-clamp-1 px-1 text-[9px]">
        {nombreArchivo}
      </span>
    </div>
  );
}
