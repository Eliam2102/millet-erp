import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import type { ReglaFirmaCancelacion } from '@/features/facturacion/api/types';
import {
  useGuardarReglasFirmaCancelacion,
  useReglasFirmaCancelacion,
} from '@/features/facturacion/api/useFacturas';
import { mostrarError } from '@/features/facturacion/lib/mostrar-error-cancelacion';
import { TIPOS_OPERACION_CANCELACION } from '@/features/facturacion/lib/tipos-operacion-cancelacion';
import { formatMoney } from '@/lib/money';
import { useState } from 'react';
import { toast } from 'sonner';

interface Fila {
  tipo: string;
  umbral: string;
  firmas: string;
}

const aFila = (r: ReglaFirmaCancelacion): Fila => ({
  tipo: r.tipoComprobante ?? '',
  umbral: String(r.umbralMxn),
  firmas: String(r.firmasRequeridas),
});

/**
 * <c>&lt;ReglasFirmaCancelacionSheet/&gt;</c> — M-235: cuántas firmas pide la
 * cancelación según el tipo de operación y el importe en MXN. Sin reglas basta una
 * firma. Arriba del umbral se aplican las firmas de la regla; aplica a las
 * solicitudes nuevas. Editar exige el permiso de autorizar cancelaciones.
 */
export function ReglasFirmaCancelacionSheet({
  abierto,
  onOpenChange,
  puedeEditar,
}: {
  abierto: boolean;
  onOpenChange: (abierto: boolean) => void;
  puedeEditar: boolean;
}) {
  const query = useReglasFirmaCancelacion(abierto);
  const guardar = useGuardarReglasFirmaCancelacion();
  const [filas, setFilas] = useState<Fila[]>([]);
  // Las filas se copian de lo guardado al abrir (ajuste de estado en render, sin efecto).
  const [origen, setOrigen] = useState<ReglaFirmaCancelacion[] | undefined>();
  if (abierto && query.data && query.data !== origen) {
    setOrigen(query.data);
    setFilas(query.data.map(aFila));
  }

  function cambiarAbierto(valor: boolean) {
    if (!valor) setOrigen(undefined);
    onOpenChange(valor);
  }

  function actualizar(i: number, cambio: Partial<Fila>) {
    setFilas((prev) => prev.map((f, j) => (j === i ? { ...f, ...cambio } : f)));
  }

  function enviar() {
    const reglas = filas.map((f) => ({
      tipoComprobante: f.tipo || null,
      umbralMxn: Number(f.umbral),
      firmasRequeridas: Number(f.firmas),
    }));
    if (reglas.some((r) => !Number.isFinite(r.umbralMxn) || r.umbralMxn < 0)) {
      toast.error('Indica un umbral en MXN mayor o igual a cero.');
      return;
    }
    guardar.mutate(
      { reglas, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success('Firmas de cancelación guardadas. Aplican a las solicitudes nuevas.');
          cambiarAbierto(false);
        },
        onError: (error) => mostrarError(error, 'No se pudieron guardar las firmas.'),
      },
    );
  }

  return (
    <Sheet open={abierto} onOpenChange={cambiarAbierto}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>Firmas por operación y monto</SheetTitle>
          <SheetDescription>
            Sin reglas, una firma autoriza la cancelación. Arriba del umbral (total con impuestos
            en MXN) se piden las firmas de la regla; la segunda la da otra persona.
          </SheetDescription>
        </SheetHeader>
        <SheetBody>
          {query.isLoading ? (
            <p role="status">Cargando reglas…</p>
          ) : !filas.length ? (
            <p className="text-ink-muted">Sin reglas: toda cancelación pide una firma.</p>
          ) : (
            <ul className="flex flex-col gap-3">
              {filas.map((f, i) => (
                <li
                  key={i}
                  className="grid grid-cols-[1fr_9rem_7rem_auto] items-end gap-2"
                >
                  <label className="flex flex-col gap-1 text-xs text-ink-muted">
                    Operación
                    <select
                      className="block h-9 w-full rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink"
                      value={f.tipo}
                      disabled={!puedeEditar}
                      onChange={(e) => actualizar(i, { tipo: e.target.value })}
                    >
                      {TIPOS_OPERACION_CANCELACION.map((t) => (
                        <option key={t.value} value={t.value}>
                          {t.label}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label className="flex flex-col gap-1 text-xs text-ink-muted">
                    Arriba de (MXN)
                    <Input
                      type="number"
                      min={0}
                      step="0.01"
                      value={f.umbral}
                      disabled={!puedeEditar}
                      onChange={(e) => actualizar(i, { umbral: e.target.value })}
                    />
                  </label>
                  <label className="flex flex-col gap-1 text-xs text-ink-muted">
                    Firmas
                    <select
                      className="block h-9 w-full rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink"
                      value={f.firmas}
                      disabled={!puedeEditar}
                      onChange={(e) => actualizar(i, { firmas: e.target.value })}
                    >
                      <option value="1">Una</option>
                      <option value="2">Dos</option>
                    </select>
                  </label>
                  {puedeEditar ? (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setFilas((prev) => prev.filter((_, j) => j !== i))}
                    >
                      Quitar
                    </Button>
                  ) : (
                    <span className="text-xs text-ink-muted">
                      {formatMoney({ amount: Number(f.umbral), currency: 'MXN' })}
                    </span>
                  )}
                </li>
              ))}
            </ul>
          )}
          {puedeEditar && (
            <Button
              className="mt-3"
              variant="outline"
              size="sm"
              onClick={() => setFilas((prev) => [...prev, { tipo: '', umbral: '0', firmas: '2' }])}
            >
              Agregar regla
            </Button>
          )}
        </SheetBody>
        {puedeEditar && (
          <SheetFooter>
            <Button variant="ghost" onClick={() => cambiarAbierto(false)} disabled={guardar.isPending}>
              Cancelar
            </Button>
            <Button onClick={enviar} disabled={guardar.isPending}>
              {guardar.isPending ? 'Guardando…' : 'Guardar'}
            </Button>
          </SheetFooter>
        )}
      </SheetContent>
    </Sheet>
  );
}
