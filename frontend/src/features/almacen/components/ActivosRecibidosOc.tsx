import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { TextAreaField } from '@/components/erp';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { Field } from '@/features/almacen/components/internal/Field';
import { esApiError, codigoSoporte } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useActivosRecibidos,
  useAnularRecepcionActivo,
  type ActivoRecibido,
} from '@/features/almacen/api/useActivosRecibidos';

const MENSAJES: Record<string, string> = {
  RECEPCION_ACTIVO_CAPITALIZADO: 'El activo ya fue capitalizado: dalo de baja en Activos Fijos.',
  RECEPCION_ACTIVO_FACTURADO: 'La recepción ya está facturada y no se puede anular.',
  RECEPCION_ACTIVO_ESTADO_NO_VERIFICABLE:
    'No se pudo verificar el estado del activo. Intenta de nuevo.',
  RECEPCION_ACTIVO_NO_ENCONTRADA: 'La recepción de activo no existe.',
};

export function AnularRecepcionActivoDialog({
  ordenCompraId,
  recepcionId,
  folio,
  open,
  onOpenChange,
}: {
  ordenCompraId: string;
  recepcionId: string;
  folio: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const anular = useAnularRecepcionActivo();
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string>();

  function confirmar() {
    const m = motivo.trim();
    if (!m) {
      setError('Motivo requerido');
      return;
    }
    anular.mutate(
      { ordenCompraId, recepcionId, motivo: m, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success('Recepción de activo anulada');
          setMotivo('');
          onOpenChange(false);
        },
        onError: (e) => {
          const codigo = esApiError(e) ? (e.problem.code as string | undefined) : undefined;
          if (codigo && MENSAJES[codigo]) toast.error(MENSAJES[codigo]);
          else if (esApiError(e)) toast.error(e.problem.title, { description: codigoSoporte(e) });
          else toast.error('Error inesperado.');
        },
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Anular recepción {folio ?? 'sin folio'}</DialogTitle>
          <DialogDescription>
            Captura el motivo. Las partidas de activo fijo de esta recepción quedan anuladas.
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <Field label="Motivo de la anulación" required error={error}>
            <TextAreaField
              value={motivo}
              onChange={(v) => {
                setMotivo(v ?? '');
                setError(undefined);
              }}
              maxLength={500}
              minRows={3}
            />
          </Field>
        </DialogBody>
        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={anular.isPending}
          >
            Cancelar
          </Button>
          <Button
            type="button"
            variant="destructive"
            onClick={confirmar}
            disabled={anular.isPending}
          >
            {anular.isPending ? 'Anulando…' : 'Anular recepción'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** Sección "Activos recibidos" de una OC; no renderiza nada si no hay filas. */
export function ActivosRecibidosOc({ ordenCompraId }: { ordenCompraId: string }) {
  const query = useActivosRecibidos(ordenCompraId);
  const puedeAnular = useHasPermission(PermisosCanonicos.AlmacenEntradasRegistrar);
  const [objetivo, setObjetivo] = useState<ActivoRecibido | null>(null);
  const filas = query.data ?? [];
  if (filas.length === 0) return null;

  // Una acción por recepción: la primera fila de cada recepcionId.
  const primeras = new Set<string>();
  const esPrimera = (f: ActivoRecibido) => {
    if (primeras.has(f.recepcionId)) return false;
    primeras.add(f.recepcionId);
    return true;
  };

  return (
    <section aria-labelledby="activos-recibidos-titulo" className="space-y-2">
      <h2 id="activos-recibidos-titulo" className="text-sm font-semibold text-ink">
        Activos recibidos
      </h2>
      <TableContainer>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Folio</TableHead>
              <TableHead className="text-right">Cantidad</TableHead>
              <TableHead>Fecha</TableHead>
              <TableHead>Estado</TableHead>
              {puedeAnular && <TableHead className="w-0" />}
            </TableRow>
          </TableHeader>
          <TableBody>
            {filas.map((f) => (
              <TableRow key={f.id}>
                <TableCell className="font-mono">{f.folio ?? 'Sin folio'}</TableCell>
                <TableCell className="text-right tabular-nums">{f.cantidad}</TableCell>
                <TableCell>
                  <DateTimeDisplay value={f.recibidaEn} variant="media" />
                </TableCell>
                <TableCell>
                  {f.anulada ? (
                    <span>
                      <span className="font-medium text-danger-fg">Anulada</span>
                      {f.motivoAnulacion && (
                        <span className="text-ink-muted"> · {f.motivoAnulacion}</span>
                      )}
                    </span>
                  ) : (
                    'Recibida'
                  )}
                </TableCell>
                {puedeAnular && (
                  <TableCell>
                    {esPrimera(f) && !f.anulada && (
                      <Button size="sm" variant="outline" onClick={() => setObjetivo(f)}>
                        Anular recepción
                      </Button>
                    )}
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      {objetivo && (
        <AnularRecepcionActivoDialog
          ordenCompraId={ordenCompraId}
          recepcionId={objetivo.recepcionId}
          folio={objetivo.folio}
          open
          onOpenChange={(o) => !o && setObjetivo(null)}
        />
      )}
    </section>
  );
}
