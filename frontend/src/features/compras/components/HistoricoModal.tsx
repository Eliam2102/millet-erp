import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { TimelineRequisicion } from '@/features/compras/components/TimelineRequisicion';
import type { HistoricoEntryResponse } from '@/features/compras/api/types';

/**
 * <c>&lt;HistoricoModal/&gt;</c> — Dialog que envuelve el
 * <c>&lt;TimelineRequisicion/&gt;</c>. UF7-PR4.
 *
 * <para>El histórico de auditoría suele tener decenas de eventos
 * (cada <c>LineaAgregada/Actualizada</c> cuenta), por lo que vive en
 * un modal en lugar de inline en P3 — la vista de detalle queda
 * limpia para operación, y el historial está a 1 click cuando hay
 * duda (debugging, soporte, validación de matriz).</para>
 */
export interface HistoricoModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Folio de la RQ — solo para mostrar en el título del modal. */
  folio: string;
  entradas: readonly HistoricoEntryResponse[];
  isLoading?: boolean;
}

export function HistoricoModal({
  open,
  onOpenChange,
  folio,
  entradas,
  isLoading,
}: HistoricoModalProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[80vh] max-w-2xl flex-col gap-4">
        <DialogHeader>
          <DialogTitle>
            Histórico — <span className="font-mono">{folio}</span>
          </DialogTitle>
          <DialogDescription>
            Todas las transiciones del agregado: creación, edición de
            líneas, autorizaciones, cubrimiento, recepciones y cierre.
            Click en "Ver cambios" para inspeccionar el diff de cada
            evento.
          </DialogDescription>
        </DialogHeader>

        {/* Scroll local del timeline; el modal mantiene header + footer
            visibles. */}
        <div className="-mx-6 max-h-[60vh] overflow-y-auto px-6">
          <TimelineRequisicion
            entradas={entradas}
            isLoading={isLoading}
          />
        </div>
      </DialogContent>
    </Dialog>
  );
}
