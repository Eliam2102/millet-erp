import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { ComparacionProformaResponse } from '@/features/facturacion/api/types';
import { formatDateTime } from '@/lib/datetime';

/** Qué se confirma antes de timbrar un pedido con proforma (U1.8a). */
export type ConfirmacionProforma =
  | { tipo: 'diferencias'; comparacion: ComparacionProformaResponse }
  | { tipo: 'error' };

/**
 * Aviso previo al timbrado: lo que se va a timbrar difiere de la última
 * proforma enviada al cliente (CA-c), o no se pudo comparar. «Revisar»
 * regresa al formulario; la acción primaria timbra de todos modos.
 */
export function DiferenciasProformaDialog({
  confirmacion,
  onRevisar,
  onTimbrar,
}: {
  confirmacion: ConfirmacionProforma | null;
  onRevisar: () => void;
  onTimbrar: () => void;
}) {
  const comparacion = confirmacion?.tipo === 'diferencias' ? confirmacion.comparacion : null;
  return (
    <AlertDialog
      open={confirmacion != null}
      onOpenChange={(abierto) => {
        if (!abierto) onRevisar();
      }}
    >
      <AlertDialogContent className="max-w-2xl rounded-2xl shadow-dialog">
        {comparacion != null ? (
          <>
            <AlertDialogHeader>
              <AlertDialogTitle className="text-xl">
                Lo que vas a timbrar difiere de la proforma{' '}
                <span className="font-mono">{comparacion.folio}</span>
              </AlertDialogTitle>
              <AlertDialogDescription className="text-sm text-ink-muted">
                {comparacion.generadaEn
                  ? `La proforma se generó el ${formatDateTime(comparacion.generadaEn)}. `
                  : ''}
                Revisa los cambios: si el cliente aprobó la proforma, corrige la factura o genera
                una proforma nueva antes de timbrar.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <div className="max-h-[50vh] overflow-y-auto rounded-lg shadow-card-flat">
              <Table className="w-full text-sm">
                <TableHeader className="bg-surface-subtle">
                  <TableRow>
                    <TableHead className="px-3 py-2 text-left">Campo</TableHead>
                    <TableHead className="px-3 py-2 text-left">Proforma</TableHead>
                    <TableHead className="px-3 py-2 text-left">Timbrado</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {comparacion.diferencias.map((d, i) => (
                    <TableRow key={`${d.campo}-${i}`} className="border-t border-line-row">
                      <TableCell className="px-3 py-2 text-ink-strong">{d.campo}</TableCell>
                      <TableCell className="px-3 py-2 tabular-nums text-ink-secondary">
                        {d.proforma ?? <span className="text-ink-muted">Sin dato</span>}
                      </TableCell>
                      <TableCell className="px-3 py-2 font-medium tabular-nums text-ink">
                        {d.timbrado ?? <span className="text-ink-muted">Sin dato</span>}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </>
        ) : (
          <AlertDialogHeader>
            <AlertDialogTitle className="text-xl">
              No se pudo comparar con la proforma
            </AlertDialogTitle>
            <AlertDialogDescription className="text-sm text-ink-muted">
              No fue posible revisar si la factura coincide con la última proforma del pedido.
              Revisa tu conexión y vuelve a intentarlo, o timbra sin comparar si ya verificaste
              los importes con el cliente.
            </AlertDialogDescription>
          </AlertDialogHeader>
        )}
        <AlertDialogFooter>
          <AlertDialogCancel>Revisar</AlertDialogCancel>
          <AlertDialogAction onClick={onTimbrar}>
            {comparacion != null ? 'Timbrar de todos modos' : 'Timbrar sin comparar'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
