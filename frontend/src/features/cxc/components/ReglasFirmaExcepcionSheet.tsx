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
import {
  useConfiguracionExcepcionCredito,
  useGuardarReglasFirmaExcepcion,
} from '@/features/cxc/api/useLiberaciones';
import type { ConfiguracionExcepcionCredito } from '@/features/cxc/api/types';
import { esApiError } from '@/lib/api';
import { useState } from 'react';
import { toast } from 'sonner';

interface Fila {
  umbral: string;
  firmas: string;
}

const aFilas = (c: ConfiguracionExcepcionCredito): Fila[] =>
  c.reglas.map((r) => ({ umbral: String(r.umbralMxn), firmas: String(r.firmasRequeridas) }));

/**
 * <c>&lt;ReglasFirmaExcepcionSheet/&gt;</c> — M-239: firmas por monto de las excepciones de crédito.
 * Hasta el umbral basta Crédito y Cobranza; arriba se pide también la firma de Dirección (decisión M-085).
 * La vigencia sale de configuración y aquí solo se muestra. Editar exige el permiso de Dirección.
 */
export function ReglasFirmaExcepcionSheet({
  abierto,
  onOpenChange,
  puedeEditar,
}: {
  abierto: boolean;
  onOpenChange: (abierto: boolean) => void;
  puedeEditar: boolean;
}) {
  const query = useConfiguracionExcepcionCredito(abierto);
  const guardar = useGuardarReglasFirmaExcepcion();
  const [filas, setFilas] = useState<Fila[]>([]);
  // Las filas se copian de lo guardado al abrir (ajuste de estado en render, sin efecto).
  const [origen, setOrigen] = useState<ConfiguracionExcepcionCredito | undefined>();
  if (abierto && query.data && query.data !== origen) {
    setOrigen(query.data);
    setFilas(aFilas(query.data));
  }

  function cambiarAbierto(valor: boolean) {
    if (!valor) setOrigen(undefined);
    onOpenChange(valor);
  }

  function enviar() {
    const reglas = filas.map((f) => ({ umbralMxn: Number(f.umbral), firmasRequeridas: Number(f.firmas) }));
    if (reglas.some((r) => !Number.isFinite(r.umbralMxn) || r.umbralMxn < 0)) {
      toast.error('Indica un umbral en MXN mayor o igual a cero.');
      return;
    }
    guardar.mutate(
      { reglas, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success('Firmas por monto guardadas. Aplican a las solicitudes nuevas.');
          cambiarAbierto(false);
        },
        onError: (e) =>
          toast.error(esApiError(e) ? e.problem.title : 'No se pudieron guardar las firmas.'),
      },
    );
  }

  return (
    <Sheet open={abierto} onOpenChange={cambiarAbierto}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>Firmas por monto de la excepción de crédito</SheetTitle>
          <SheetDescription>
            Hasta el umbral basta la firma de Crédito y Cobranza; arriba se pide también la de
            Dirección (otra persona). Aplica al monto de la excepción en MXN.
          </SheetDescription>
        </SheetHeader>
        <SheetBody>
          {query.isLoading ? (
            <p role="status">Cargando…</p>
          ) : (
            <div className="flex flex-col gap-3">
              {query.data && (
                <p className="text-xs text-ink-muted">
                  Vigencia por omisión: {query.data.vigenciaPorOmisionHoras} h; máxima:{' '}
                  {query.data.vigenciaMaximaHoras} h (configuración del servidor).
                  {query.data.reglasPorOmision &&
                    ' No hay reglas guardadas: se aplica la de M-085 (arriba del tope, dos firmas).'}
                </p>
              )}
              <ul className="flex flex-col gap-3">
                {filas.map((f, i) => (
                  <li key={i} className="grid grid-cols-[1fr_8rem_auto] items-end gap-2">
                    <label className="flex flex-col gap-1 text-xs text-ink-muted">
                      Arriba de (MXN)
                      <Input
                        type="number"
                        min={0}
                        step="0.01"
                        value={f.umbral}
                        disabled={!puedeEditar}
                        onChange={(e) =>
                          setFilas((prev) => prev.map((x, j) => (j === i ? { ...x, umbral: e.target.value } : x)))
                        }
                      />
                    </label>
                    <label className="flex flex-col gap-1 text-xs text-ink-muted">
                      Firmas
                      <select
                        className="block h-9 w-full rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink"
                        value={f.firmas}
                        disabled={!puedeEditar}
                        onChange={(e) =>
                          setFilas((prev) => prev.map((x, j) => (j === i ? { ...x, firmas: e.target.value } : x)))
                        }
                      >
                        <option value="1">Una (Crédito)</option>
                        <option value="2">Dos (+ Dirección)</option>
                      </select>
                    </label>
                    {puedeEditar && (
                      <Button variant="ghost" size="sm" onClick={() => setFilas((prev) => prev.filter((_, j) => j !== i))}>
                        Quitar
                      </Button>
                    )}
                  </li>
                ))}
              </ul>
              {puedeEditar && (
                <Button
                  className="self-start"
                  variant="outline"
                  size="sm"
                  onClick={() => setFilas((prev) => [...prev, { umbral: '0', firmas: '2' }])}
                >
                  Agregar regla
                </Button>
              )}
            </div>
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
