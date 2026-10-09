import { Check, X, Copy, ArrowRight } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ConfirmDuplicarDialog/&gt;</c> — modal de confirmación para
 * duplicar una OC (UF5-PR2, FOC7).
 *
 * <para><b>UX crítica</b>: el flujo C4 (cancelar + recrear) es la
 * mitigación principal post-autorización. El comprador debe entender
 * <i>exactamente</i> qué se copia y qué NO antes de confirmar — sino
 * espera que adjuntos/autorizaciones también vengan y se sorprende.</para>
 *
 * <para>Layout:</para>
 * <list>
 *   <item>Banner del origen (folio + estado).</item>
 *   <item>Lista verde "✓ Se copia": cabecera, líneas (como manuales),
 *   logística, importación, totales financieros.</item>
 *   <item>Lista roja "✕ NO se copia": adjuntos, autorizaciones,
 *   sub-estados, motivo de rechazo/cancelación, vínculo a RQs.</item>
 *   <item>CTA primaria "Duplicar y abrir nueva".</item>
 * </list>
 */
export interface ConfirmDuplicarDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  ocFolio: string;
  ocEstadoLabel: string;
  isPending?: boolean;
  /** Llamado al confirmar duplicación. El caller dispara el mutation. */
  onConfirm: () => Promise<void>;
}

export function ConfirmDuplicarDialog({
  open,
  onOpenChange,
  ocFolio,
  ocEstadoLabel,
  isPending,
  onConfirm,
}: ConfirmDuplicarDialogProps) {
  return (
    <Dialog
      open={open}
      onOpenChange={(o) => {
        if (!isPending) onOpenChange(o);
      }}
    >
      <DialogContent
        className="max-w-lg"
        data-component="confirm-duplicar-dialog"
      >
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Copy className="h-5 w-5" />
            Duplicar OC {ocFolio}
          </DialogTitle>
          <DialogDescription>
            Crea una nueva OC en <strong>Borrador</strong> heredando datos
            de esta OC <strong>{ocEstadoLabel}</strong>. Trazabilidad: la
            nueva OC conservará la referencia a esta orden.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3 text-sm">
          {/* Lista de qué se copia */}
          <div className="rounded-md border border-line bg-success-bg p-3">
            <h3 className="mb-2 flex items-center gap-1.5 font-medium text-success-fg">
              <Check className="h-4 w-4" />
              Se copia a la nueva OC
            </h3>
            <ul className="space-y-1 text-success-fg">
              <ItemList ok>
                Cabecera (proveedor, sucursal, almacén, condiciones,
                moneda, banderas, observaciones)
              </ItemList>
              <ItemList ok>
                Solo cantidades no recibidas, conservando su requisición de origen cuando corresponda
              </ItemList>
              <ItemList ok>
                Información de logística (dirección, transportista, guía)
              </ItemList>
              <ItemList ok>
                Información de importación (incoterm, contenedor, ruta)
              </ItemList>
              <ItemList ok>
                Totales financieros (descuento global, gastos, redondeo)
              </ItemList>
            </ul>
          </div>

          {/* Lista de qué NO se copia */}
          <div className="rounded-md border border-line bg-surface-muted p-3">
            <h3 className="mb-2 flex items-center gap-1.5 font-medium text-ink-secondary">
              <X className="h-4 w-4" />
              NO se copia
            </h3>
            <ul className="space-y-1 text-ink-secondary">
              <ItemList>Adjuntos (cotización, ficha técnica, etc.)</ItemList>
              <ItemList>Autorizaciones (firmas N1/N2)</ItemList>
              <ItemList>Sub-estados (recepción, facturación, pago)</ItemList>
              <ItemList>
                Motivo de rechazo/cancelación de la OC origen
              </ItemList>
              <ItemList>Cantidades ya recibidas</ItemList>
            </ul>
          </div>
        </div>

        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={isPending}
          >
            Cancelar
          </Button>
          <Button
            type="button"
            onClick={async () => {
              try {
                await onConfirm();
              } catch {
                // El caller maneja el error (toast / conflict dialog).
              }
            }}
            disabled={isPending}
            data-action="confirmar-duplicar"
          >
            {isPending ? (
              'Duplicando…'
            ) : (
              <>
                Duplicar y abrir nueva
                <ArrowRight className="ml-1 h-4 w-4" />
              </>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function ItemList({
  ok = false,
  children,
}: {
  ok?: boolean;
  children: React.ReactNode;
}) {
  return (
    <li className={cn('flex items-start gap-1.5 text-xs leading-snug')}>
      <span aria-hidden="true" className="mt-0.5 shrink-0">
        {ok ? '✓' : '✕'}
      </span>
      <span>{children}</span>
    </li>
  );
}
