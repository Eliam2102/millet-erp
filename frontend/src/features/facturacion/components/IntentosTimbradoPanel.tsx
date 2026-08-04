import { cn } from '@/lib/utils';
import { useIntentosTimbrado } from '@/features/facturacion/api/useFacturas';

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
      <div className="overflow-x-auto rounded-md border">
        <table className="w-full text-sm">
          <thead className="bg-muted/50">
            <tr>
              <th className="px-3 py-2 text-right">#</th>
              <th className="px-3 py-2 text-left">Resultado</th>
              <th className="px-3 py-2 text-left">Código</th>
              <th className="px-3 py-2 text-left">Mensaje</th>
              <th className="px-3 py-2 text-left">Fecha</th>
            </tr>
          </thead>
          <tbody>
            {(intentos.data ?? []).map((i) => (
              <tr key={i.id} className="border-t">
                <td className="px-3 py-2 text-right font-mono">{i.intentoNumero}</td>
                <td
                  className={cn(
                    'px-3 py-2',
                    i.resultado === 'Timbrado'
                      ? 'text-emerald-700'
                      : i.resultado === 'Fallido'
                        ? 'text-destructive'
                        : 'text-amber-700',
                  )}
                >
                  {i.resultado === 'EnProceso' ? 'En proceso' : i.resultado}
                </td>
                <td className="px-3 py-2 font-mono text-xs">{i.errorCodigo ?? '—'}</td>
                <td className="px-3 py-2 text-xs text-muted-foreground">
                  {i.errorMensaje ?? '—'}
                </td>
                <td className="px-3 py-2 text-xs text-muted-foreground">
                  {new Date(i.registradoAt).toLocaleString('es-MX')}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}
