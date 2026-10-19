import { Check, TriangleAlert } from 'lucide-react';
import { Button } from '@/components/ui/button';
import type {
  DestinoRevisionOc,
  RevisionPreviaOc as Revision,
} from '@/features/compras/ordenes/lib/revision-previa-oc';

export interface RevisionPreviaOcProps {
  revision: Revision;
  /** Cambia a la pestaña del detalle donde se corrige el requisito. */
  onIr: (tab: DestinoRevisionOc) => void;
}

/**
 * Tarjeta «Revisión previa» (DS 4.2): una fila por requisito de
 * «Transmitir a autorización», con círculo <c>success</c> (listo) o
 * <c>warning</c> (falta) y el botón «Ir a…» que lleva a la pestaña donde se
 * corrige.
 */
export function RevisionPreviaOc({ revision, onIr }: RevisionPreviaOcProps) {
  return (
    <section
      aria-labelledby="revision-previa-titulo"
      className="overflow-hidden rounded-lg border border-line-row bg-surface-card"
      data-print="hidden"
      data-component="revision-previa-oc"
    >
      <div className="border-b border-line-row px-4 py-3">
        <h2 id="revision-previa-titulo" className="text-sm font-semibold text-ink-strong">
          Revisión previa
        </h2>
        <p className="text-xs text-ink-muted">
          {revision.pendientes === 0
            ? 'Todo listo para transmitir a autorización.'
            : `Faltan ${revision.pendientes} de ${revision.total} requisitos para transmitir a autorización.`}
        </p>
      </div>
      <ul>
        {revision.requisitos.map((r) => (
          <li
            key={r.id}
            data-requisito={r.id}
            data-ok={r.ok}
            className="flex min-h-11 items-center gap-3 border-b border-line-row px-4 py-2 text-sm last:border-b-0"
          >
            {r.ok ? (
              <span className="flex size-5 shrink-0 items-center justify-center rounded-full bg-success-bg text-success-fg">
                <Check aria-hidden="true" className="size-3" />
                <span className="sr-only">Listo:</span>
              </span>
            ) : (
              <span className="flex size-5 shrink-0 items-center justify-center rounded-full bg-warning-bg text-warning-fg">
                <TriangleAlert aria-hidden="true" className="size-3" />
                <span className="sr-only">Pendiente:</span>
              </span>
            )}
            <span className="min-w-0 flex-1">
              <span className="text-ink-strong">{r.etiqueta}</span>
              {!r.ok && r.detalle && (
                <span className="block text-xs text-ink-muted">{r.detalle}</span>
              )}
            </span>
            {!r.ok && r.destino && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => onIr(r.destino!.tab)}
                aria-label={`${r.destino.etiqueta}: ${r.etiqueta}`}
              >
                {r.destino.etiqueta}
              </Button>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
