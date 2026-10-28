import { Badge } from '@/components/ui/badge';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { formatDateTime } from '@/lib/datetime';
import type { RechazoContable } from '../../api/motor-types';
import { ESTADO_RECHAZO_UI, VARIANTE_CAUSA, payloadLegible, textoCausa } from '../../lib/motor';

interface Props {
  rechazo: RechazoContable | null;
  onClose: () => void;
}

/** Detalle de un rechazo con el evento original (payload) tal como llegó al motor. */
export function PayloadRechazoSheet({ rechazo: r, onClose }: Props) {
  return (
    <Sheet open={!!r} onOpenChange={(o) => !o && onClose()}>
      <SheetContent size="md" side="right" data-print="hidden">
        <SheetHeader>
          <SheetTitle>Evento rechazado</SheetTitle>
          <SheetDescription className="text-xs text-ink-muted">
            <span className="font-mono">{r?.tipoEvento}</span> · evento <span className="font-mono">{r?.eventoId}</span>
          </SheetDescription>
        </SheetHeader>
        {r && (
          <SheetBody className="flex flex-col gap-4">
            <dl className="grid grid-cols-2 gap-3 text-sm">
              <div>
                <dt className="text-xs text-ink-muted">Causa</dt>
                <dd>
                  <Badge variant={VARIANTE_CAUSA[r.causa] ?? 'neutral'}>{textoCausa(r.causa)}</Badge>
                </dd>
              </div>
              <div>
                <dt className="text-xs text-ink-muted">Estado</dt>
                <dd>
                  <Badge variant={ESTADO_RECHAZO_UI[r.estado]?.variante ?? 'neutral'}>
                    {ESTADO_RECHAZO_UI[r.estado]?.texto ?? r.estado}
                  </Badge>
                </dd>
              </div>
              <div className="col-span-2">
                <dt className="text-xs text-ink-muted">Mensaje</dt>
                <dd>{r.mensaje}</dd>
              </div>
              {r.detalle && (
                <div className="col-span-2">
                  <dt className="text-xs text-ink-muted">Detalle</dt>
                  <dd className="font-mono text-xs">{r.detalle}</dd>
                </div>
              )}
              <div>
                <dt className="text-xs text-ink-muted">Primer intento</dt>
                <dd>{formatDateTime(r.creadoEn)}</dd>
              </div>
              <div>
                <dt className="text-xs text-ink-muted">Último intento</dt>
                <dd>
                  {formatDateTime(r.ultimoIntentoEn)} · <span className="tabular-nums">{r.intentos}</span>{' '}
                  {r.intentos === 1 ? 'intento' : 'intentos'}
                </dd>
              </div>
              {r.resueltoPor && r.resueltoEn && (
                <div className="col-span-2">
                  <dt className="text-xs text-ink-muted">{r.estado === 'Descartado' ? 'Descartó' : 'Resolvió'}</dt>
                  <dd>
                    {r.resueltoPor} · {formatDateTime(r.resueltoEn)}
                  </dd>
                </div>
              )}
              {r.motivoDescarte && (
                <div className="col-span-2">
                  <dt className="text-xs text-ink-muted">Motivo del descarte</dt>
                  <dd className="whitespace-pre-line">{r.motivoDescarte}</dd>
                </div>
              )}
            </dl>
            <section aria-label="Evento original" className="flex flex-col gap-1.5">
              <h3 className="text-xs text-ink-muted">Evento original</h3>
              <pre className="overflow-x-auto rounded-md bg-surface-subtle p-3 font-mono text-xs text-ink">
                {payloadLegible(r.payload)}
              </pre>
            </section>
          </SheetBody>
        )}
      </SheetContent>
    </Sheet>
  );
}
