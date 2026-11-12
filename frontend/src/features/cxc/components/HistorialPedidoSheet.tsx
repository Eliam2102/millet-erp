import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { DateTimeDisplay } from '@/components/erp';
import { useHistorialLiberacionPedido } from '@/features/cxc/api/useLiberaciones';
import { ChipResultadoLiberacion } from '@/features/cxc/components/ChipsLiberacion';
import { ETIQUETA_TIPO_CORRECCION, formatoCobertura, formatoMonto } from '@/features/cxc/lib/glosario';

/**
 * <c>&lt;HistorialPedidoSheet/&gt;</c> — M-238: todas las evaluaciones de liberación de un pedido, de la primera a la
 * última. Cuando una salió de «corregir y reevaluar» muestra qué se corrigió, para ver el antes y el después.
 */
export function HistorialPedidoSheet({
  pedidoRef,
  nombreUsuario,
  onOpenChange,
}: {
  pedidoRef: string | null;
  nombreUsuario: (id: string) => string;
  onOpenChange: (open: boolean) => void;
}) {
  const query = useHistorialLiberacionPedido(pedidoRef);
  return (
    <Sheet open={pedidoRef != null} onOpenChange={onOpenChange}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>Historial del pedido {pedidoRef}</SheetTitle>
          <SheetDescription>
            Cada evaluación de liberación del pedido, con las correcciones que llevaron a la siguiente.
          </SheetDescription>
        </SheetHeader>
        <SheetBody>
          {query.isLoading ? (
            <p role="status">Cargando historial…</p>
          ) : query.isError ? (
            <p role="alert" className="text-danger-fg">
              No se pudo cargar el historial.
            </p>
          ) : (
            <ol className="flex flex-col gap-3">
              {(query.data ?? []).map((e, i) => (
                <li key={e.decision.id} className="rounded-md border border-line-control p-3 text-sm">
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs font-medium text-ink-muted">Evaluación {i + 1}</span>
                    <ChipResultadoLiberacion resultado={e.decision.resultado} />
                  </div>
                  {e.correccion && (
                    <p className="mt-2 rounded bg-surface-muted p-2 text-xs">
                      <span className="font-medium">Corrección: {ETIQUETA_TIPO_CORRECCION[e.correccion.tipo]}.</span>{' '}
                      {e.correccion.detalle}
                    </p>
                  )}
                  <dl className="mt-2 grid grid-cols-2 gap-x-4 gap-y-1 text-xs">
                    <dt className="text-ink-muted">Monto del pedido</dt>
                    <dd className="font-mono tabular-nums">{formatoMonto(e.decision.montoPedido, e.decision.moneda)}</dd>
                    <dt className="text-ink-muted">Crédito disponible</dt>
                    <dd className="font-mono tabular-nums">
                      {formatoMonto(e.decision.creditoDisponibleSnapshot, e.decision.moneda)}
                    </dd>
                    {formatoCobertura(e.decision.porcentajeCobertura) && (
                      <>
                        <dt className="text-ink-muted">Cobertura</dt>
                        <dd>{formatoCobertura(e.decision.porcentajeCobertura)}</dd>
                      </>
                    )}
                    <dt className="text-ink-muted">Evaluó</dt>
                    <dd>
                      {nombreUsuario(e.decision.decididoPor)} · <DateTimeDisplay value={e.decision.decididoEn} />
                    </dd>
                  </dl>
                </li>
              ))}
            </ol>
          )}
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}
