import { useRef, useState } from 'react';
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
import { DecimalField } from '@/components/erp/forms/DecimalField';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';
import {
  DECIMALES_FALLBACK,
  cabeEnDecimales,
  stepParaDecimales,
} from '@/components/erp/forms/decimales-unidad';
import { useDecimalesUnidad } from '@/components/erp/forms/useDecimalesUnidad';
import { esApiError } from '@/lib/api/error';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  useApartadosRequisicion,
  useLiberarApartado,
  type ApartadoLineaResponse,
} from '@/features/compras/api/useApartados';
import type { LineaResponse } from '@/features/compras/api/types';

export interface ApartadosRequisicionProps {
  requisicionId: string;
  lineas: LineaResponse[];
  /** Solo se consulta en estados con reserva viva (Autorizada / EnSurtido). */
  enabled: boolean;
}

/**
 * Existencia apartada por línea y acción "Liberar apartado" (86e3tferc).
 * Panel aparte de la tabla de líneas para aplicar tanto con la lista
 * read-only como con el editor. Sin permiso no se renderiza la acción.
 */
export function ApartadosRequisicion({
  requisicionId,
  lineas,
  enabled,
}: ApartadosRequisicionProps) {
  const permisos = useAuthStore((s) => s.permisos);
  const puedeLiberar = permisos.includes(PermisosCanonicos.ComprasRequisicionesLiberarApartado);
  const { data, isError } = useApartadosRequisicion(requisicionId, enabled);
  const [liberando, setLiberando] = useState<ApartadoLineaResponse | null>(null);

  if (isError)
    return (
      <p role="alert" className="text-sm text-danger-fg">
        No se pudo cargar la existencia apartada.
      </p>
    );
  if (!data || data.length === 0) return null;
  const porId = new Map(lineas.map((l) => [l.id, l]));

  return (
    <section aria-label="Existencia apartada" className="rounded-md border bg-surface-card p-3">
      <h3 className="mb-2 text-sm font-medium">Existencia apartada</h3>
      <ul className="space-y-2 text-sm">
        {data.map((a) => {
          const l = porId.get(a.lineaId);
          const etiqueta = l
            ? [l.articuloClave, l.articuloNombre].filter(Boolean).join(' · ') ||
              `Partida ${l.posicion}`
            : a.lineaId;
          return (
            <li key={a.lineaId} className="flex flex-wrap items-center justify-between gap-2">
              <div>
                <div className="font-medium">{etiqueta}</div>
                <div className="text-xs text-ink-muted" title={a.ultimoMotivo ?? undefined}>
                  Apartado: {a.pendiente} {a.unidadMedida}
                  {a.liberada > 0 && <> · liberado {a.liberada}</>}
                  {a.liberada > 0 && a.ultimoMotivo && <> ({a.ultimoMotivo})</>}
                </div>
              </div>
              {puedeLiberar && a.pendiente > 0 && (
                <Button type="button" variant="secondary" size="sm" onClick={() => setLiberando(a)}>
                  Liberar apartado
                </Button>
              )}
            </li>
          );
        })}
      </ul>
      {liberando && (
        <LiberarApartadoDialog
          requisicionId={requisicionId}
          apartado={liberando}
          onClose={() => setLiberando(null)}
        />
      )}
    </section>
  );
}

function LiberarApartadoDialog({
  requisicionId,
  apartado,
  onClose,
}: {
  requisicionId: string;
  apartado: ApartadoLineaResponse;
  onClose: () => void;
}) {
  const decimales = useDecimalesUnidad().porCodigo(apartado.unidadMedida) ?? DECIMALES_FALLBACK;
  const mutation = useLiberarApartado();
  const [cantidad, setCantidad] = useState<number | null>(apartado.pendiente);
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);
  // Guard síncrono: isPending se actualiza tras un render y un doble clic rápido enviaba dos POST.
  const enVuelo = useRef(false);
  // Misma clave al reintentar los mismos valores; valores distintos => clave nueva.
  const [idempotencyKey, setIdempotencyKey] = useState(() => crypto.randomUUID());

  function validar(): string | null {
    if (cantidad == null || cantidad <= 0) return 'La cantidad debe ser mayor a 0.';
    if (cantidad > apartado.pendiente)
      return `La cantidad no puede exceder lo apartado (${apartado.pendiente}).`;
    if (!cabeEnDecimales(cantidad, decimales))
      return `La unidad admite máximo ${decimales} decimales.`;
    if (motivo.trim().length === 0) return 'El motivo es obligatorio.';
    if (motivo.trim().length > 500) return 'El motivo no puede exceder 500 caracteres.';
    return null;
  }

  function enviar() {
    if (enVuelo.current || mutation.isPending) return;
    const msg = validar();
    setError(msg);
    if (msg != null || cantidad == null) return;
    enVuelo.current = true;
    mutation.mutate(
      {
        requisicionId,
        lineaId: apartado.lineaId,
        values: { cantidad, motivo: motivo.trim() },
        idempotencyKey,
      },
      {
        onSuccess: onClose,
        onError: (e) =>
          setError(
            esApiError(e) ? (e.problem.detail ?? e.message) : 'No se pudo liberar el apartado.',
          ),
        onSettled: () => {
          enVuelo.current = false;
        },
      },
    );
  }

  return (
    <Dialog open onOpenChange={(o) => !o && !mutation.isPending && onClose()}>
      <DialogContent size="sm">
        <DialogHeader>
          <DialogTitle>Liberar apartado</DialogTitle>
          <DialogDescription>
            Apartado actual: {apartado.pendiente} {apartado.unidadMedida}. Lo liberado vuelve a
            existencia libre y no se compra.
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <div className="space-y-3">
            <div className="space-y-1.5">
              <label htmlFor="liberar-cantidad" className="text-sm font-medium">
                Cantidad a liberar <span className="text-danger-fg">*</span>
              </label>
              <DecimalField
                value={cantidad}
                onChange={(v) => {
                  setCantidad(v);
                  setIdempotencyKey(crypto.randomUUID());
                }}
                min={0}
                max={apartado.pendiente}
                step={stepParaDecimales(decimales)}
                inputProps={{ id: 'liberar-cantidad' }}
              />
            </div>
            <div className="space-y-1.5">
              <label htmlFor="liberar-motivo" className="text-sm font-medium">
                Motivo <span className="text-danger-fg">*</span>
              </label>
              <TextAreaField
                value={motivo.length > 0 ? motivo : null}
                onChange={(v) => {
                  setMotivo(v ?? '');
                  setIdempotencyKey(crypto.randomUUID());
                }}
                maxLength={500}
                minRows={2}
                textareaProps={{ id: 'liberar-motivo' }}
              />
            </div>
            {error && (
              <div role="alert" className="text-sm text-danger-fg">
                {error}
              </div>
            )}
          </div>
        </DialogBody>
        <DialogFooter className="gap-2">
          <Button type="button" variant="ghost" onClick={onClose} disabled={mutation.isPending}>
            Cancelar
          </Button>
          <Button type="button" onClick={enviar} disabled={mutation.isPending}>
            {mutation.isPending ? 'Procesando…' : 'Liberar apartado'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
