import { FileText, FileAudio, FileImage, Mail, Paperclip } from 'lucide-react';
import { useEvidenciasFactura } from '@/features/cxp/api/useRevision';
import {
  EstadoFirmaFisica,
  EstadoFirmaFisicaLabels,
  TipoEvidencia,
  TipoEvidenciaLabels,
  type Evidencia,
} from '@/features/cxp/api/types';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;EvidenciasList/&gt;</c> — lista de evidencias adjuntas a una
 * factura, ordenadas de la más reciente a la más vieja. Cada fila
 * muestra tipo, nombre archivo, comentario, estado firma física y
 * fechas relevantes.
 *
 * <para>PLATFORM-TODO(&lt;EvidenciaBlobDownload&gt;): backend aún no
 * expone <c>GET /facturas/{id}/evidencias/{evidenciaId}/blob</c>; el
 * <c>archivoBlobRef</c> se muestra pero no es clickable hasta que el
 * endpoint exista.</para>
 */
export interface EvidenciasListProps {
  facturaId: string;
}

export function EvidenciasList({ facturaId }: EvidenciasListProps) {
  const query = useEvidenciasFactura(facturaId);

  if (query.isLoading) {
    return (
      <p className="text-xs text-muted-foreground">Cargando evidencias…</p>
    );
  }
  if (query.isError) {
    return (
      <p className="text-xs text-destructive">
        Error al cargar evidencias. Reintenta.
      </p>
    );
  }
  const items = query.data ?? [];
  if (items.length === 0) {
    return (
      <p className="rounded-md border border-dashed px-3 py-4 text-center text-xs text-muted-foreground">
        Sin evidencias adjuntas todavía.
      </p>
    );
  }

  return (
    <ul className="space-y-2">
      {items.map((e) => (
        <EvidenciaCard key={e.id} evidencia={e} />
      ))}
    </ul>
  );
}

function EvidenciaCard({ evidencia }: { evidencia: Evidencia }) {
  return (
    <li className="rounded-md border bg-background p-3">
      <div className="flex items-start gap-3">
        <IconoPorTipo tipo={evidencia.tipo} />
        <div className="min-w-0 flex-1 space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-medium">
              {TipoEvidenciaLabels[evidencia.tipo]}
            </span>
            <FirmaChip estado={evidencia.estadoFirmaFisica} />
            <time className="text-xs text-muted-foreground">
              {new Date(evidencia.fechaCaptura).toLocaleString('es-MX')}
            </time>
          </div>
          <p className="text-sm">{evidencia.comentario}</p>
          <p className="font-mono text-xs text-muted-foreground" title={evidencia.archivoBlobRef}>
            {evidencia.nombreArchivo}
            {evidencia.tamanioBytes != null && (
              <> · {(evidencia.tamanioBytes / 1024).toFixed(1)} KB</>
            )}
          </p>
          {evidencia.estadoFirmaFisica === EstadoFirmaFisica.Pendiente &&
            evidencia.fechaLimiteFirmaFisica && (
              <p className="text-xs text-amber-700">
                Firma física pendiente, límite{' '}
                <strong>{evidencia.fechaLimiteFirmaFisica}</strong>
              </p>
            )}
          {evidencia.estadoFirmaFisica === EstadoFirmaFisica.Recibida &&
            evidencia.fechaRecepcionFirmaFisica && (
              <p className="text-xs text-emerald-700">
                Firma física recibida el{' '}
                {new Date(
                  evidencia.fechaRecepcionFirmaFisica,
                ).toLocaleDateString('es-MX')}
              </p>
            )}
        </div>
      </div>
    </li>
  );
}

function IconoPorTipo({ tipo }: { tipo: TipoEvidencia }) {
  const className = 'h-5 w-5 shrink-0 text-muted-foreground';
  switch (tipo) {
    case TipoEvidencia.CapturaWhatsapp:
      return <FileImage className={className} />;
    case TipoEvidencia.Audio:
      return <FileAudio className={className} />;
    case TipoEvidencia.Email:
      return <Mail className={className} />;
    case TipoEvidencia.FirmaEscaneada:
      return <FileText className={className} />;
    default:
      return <Paperclip className={className} />;
  }
}

function FirmaChip({ estado }: { estado: EstadoFirmaFisica }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-1.5 py-0.5 text-[10px] font-medium',
        estado === EstadoFirmaFisica.NoAplica && 'bg-slate-100 text-slate-700',
        estado === EstadoFirmaFisica.Pendiente && 'bg-amber-100 text-amber-800',
        estado === EstadoFirmaFisica.Recibida &&
          'bg-emerald-100 text-emerald-800',
      )}
    >
      Firma: {EstadoFirmaFisicaLabels[estado]}
    </span>
  );
}
