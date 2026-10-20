import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useIntentosTimbrado } from '@/features/facturacion/api/useFacturas';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;IntentosTimbradoPanel/&gt;</c> — historial de intentos de timbrado
 * de un comprobante ([Decisión 01-G] G4, F13-PR2): una fila por llamada al
 * PAC, la más reciente primero. No se renderiza si el comprobante no tiene
 * intentos registrados (emitidos antes de la bitácora o aún en Borrador).
 */
export function IntentosTimbradoPanel({ comprobanteId }: { comprobanteId: string }) {
  const intentos = useIntentosTimbrado(comprobanteId);

  if (intentos.isLoading || (intentos.data ?? []).length === 0) return null;

  return (
    <section className="space-y-2" data-print="hidden">
      <h2 className="text-sm font-medium">Historial de intentos de timbrado</h2>
      <div className="overflow-x-auto rounded-md border border-line">
        <Table className="w-full text-sm">
          <TableHeader className="bg-surface-muted/50">
            <TableRow>
              <TableHead className="px-3 py-2 text-right tabular-nums">#</TableHead>
              <TableHead className="px-3 py-2 text-left">Resultado</TableHead>
              <TableHead className="px-3 py-2 text-left">Código</TableHead>
              <TableHead className="px-3 py-2 text-left">Mensaje</TableHead>
              <TableHead className="px-3 py-2 text-left">Fecha</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {(intentos.data ?? []).map((i) => (
              <TableRow key={i.id} className="border-t border-line">
                <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                  {i.intentoNumero}
                </TableCell>
                <TableCell
                  className={cn(
                    'px-3 py-2',
                    i.resultado === 'Timbrado'
                      ? 'text-success-fg'
                      : i.resultado === 'Fallido'
                        ? 'text-danger-fg'
                        : 'text-warning-fg',
                  )}
                >
                  {i.resultado === 'EnProceso' ? 'En proceso' : i.resultado}
                </TableCell>
                <TableCell className="px-3 py-2 font-mono text-xs">
                  {i.errorCodigo ?? '—'}
                </TableCell>
                <TableCell className="px-3 py-2 text-xs text-ink-muted">
                  {i.errorMensaje ?? '—'}
                </TableCell>
                <TableCell className="px-3 py-2 text-xs text-ink-muted">
                  {new Date(i.registradoAt).toLocaleString('es-MX')}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </section>
  );
}
