import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { useAplicarNotaCargo } from '@/features/cxp/api/useNotasYAnticipos';
import type { NotaCargoListItem } from '@/features/cxp/api/types';
import { esApiError } from '@/lib/api';

/**
 * «Aplicar» de la fila de notas de cargo con confirmación y resumen (CXP-14).
 * El estado pendiente es de la fila: aplicar una no deshabilita las demás.
 */
export function AplicarNotaCargoAccion({ n }: { n: NotaCargoListItem }) {
  const aplicar = useAplicarNotaCargo();
  const [abierto, setAbierto] = useState(false);

  function confirmar() {
    aplicar.mutate(
      { id: n.id, versionEsperada: n.version, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => toast.success(`Nota de cargo ${n.folio} aplicada`),
        onError: (error) =>
          toast.error(
            esApiError(error)
              ? (error.problem.detail ?? error.problem.title)
              : 'Error al aplicar.',
          ),
        onSettled: () => setAbierto(false),
      },
    );
  }

  return (
    <>
      <Button variant="outline" size="sm" onClick={() => setAbierto(true)} disabled={aplicar.isPending}>
        Aplicar
      </Button>
      <ConfirmarAccionDialog
        open={abierto}
        onOpenChange={setAbierto}
        titulo="Aplicar nota de cargo"
        resumen={
          <>
            Se aplicará la nota de cargo {n.folio}
            {n.proveedorNombre ? ` a ${n.proveedorNombre}` : ''} por{' '}
            <MoneyDisplay amount={n.monto} currency={n.moneda} strong />; no se puede deshacer.
          </>
        }
        verbo="Aplicar nota de cargo"
        tono="primario"
        pendiente={aplicar.isPending}
        onConfirmar={confirmar}
      />
    </>
  );
}
