import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
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
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { AvancePartidaPedido } from '@/features/facturacion/api/types';
import { useRegistrarSalida } from '@/features/facturacion/api/usePedidos';
import { esApiError } from '@/lib/api';
import { formatCantidad } from '@/lib/format-cantidad';
import { Loader2 } from 'lucide-react';
import { useId, useMemo, useRef, useState } from 'react';
import { toast } from 'sonner';

/** Tope del backend (SalidaFacturable.ReferenciaMaxLength). */
export const REFERENCIA_SALIDA_MAX = 80;

interface RegistrarSalidaDialogProps {
  pedidoId: string;
  avance: AvancePartidaPedido[];
  onCerrar: () => void;
}

/**
 * FAC-08: registra a mano una salida parcial del pedido (folio de salida o
 * contenedor) para facturarla por separado. Cada partida acepta hasta lo
 * pendiente de salir; el pedido no se modifica. Reenviar la misma referencia
 * con las mismas cantidades no crea otra salida.
 */
export function RegistrarSalidaDialog({ pedidoId, avance, onCerrar }: RegistrarSalidaDialogProps) {
  const id = useId();
  const registrar = useRegistrarSalida();
  // Una sola llave por apertura del diálogo: reintentos del mismo envío no duplican.
  const idempotencyKey = useRef(crypto.randomUUID());
  const [referencia, setReferencia] = useState('');
  const [cantidades, setCantidades] = useState<Record<number, string>>({});
  const [error, setError] = useState<string | null>(null);

  const filas = useMemo(
    () =>
      avance.map((a) => {
        const pendiente = Math.max(0, a.pedida - a.enSalidas);
        const texto = cantidades[a.posicion] ?? '';
        const cantidad = texto.trim() === '' ? 0 : Number(texto);
        const invalida = !Number.isFinite(cantidad) || cantidad < 0 || cantidad > pendiente;
        return { ...a, pendiente, texto, cantidad, invalida };
      }),
    [avance, cantidades],
  );
  const lineas = filas
    .filter((f) => !f.invalida && f.cantidad > 0)
    .map((f) => ({ posicion: f.posicion, cantidad: f.cantidad }));
  const hayInvalidas = filas.some((f) => f.invalida);
  const referenciaValida = referencia.trim().length > 0;
  const motivoBloqueo = !referenciaValida
    ? 'Escribe el folio de salida o del contenedor.'
    : hayInvalidas
      ? 'Corrige las cantidades que rebasan lo pendiente de salir.'
      : lineas.length === 0
        ? 'Indica la cantidad que sale de al menos una partida.'
        : undefined;

  function registrarSalida() {
    if (motivoBloqueo || registrar.isPending) return;
    setError(null);
    registrar.mutate(
      {
        pedidoId,
        command: { referencia: referencia.trim(), lineas },
        idempotencyKey: idempotencyKey.current,
      },
      {
        onSuccess: (salida) => {
          toast.success(
            salida.yaRegistrada
              ? `La salida ya estaba registrada como parcialidad ${salida.numeroParcialidad}`
              : `Parcialidad ${salida.numeroParcialidad} registrada`,
            { description: 'Ya puedes facturarla desde la sección de salidas.' },
          );
          onCerrar();
        },
        onError: (e) => {
          // Una nueva llave para que el siguiente intento con otros datos no choque.
          idempotencyKey.current = crypto.randomUUID();
          setError(
            esApiError(e)
              ? (e.problem.detail ?? e.problem.title ?? 'No se pudo registrar la salida.')
              : 'No se pudo registrar la salida. Revisa tu conexión y vuelve a intentarlo.',
          );
        },
      },
    );
  }

  return (
    <Dialog open onOpenChange={(abierto) => !abierto && !registrar.isPending && onCerrar()}>
      <DialogContent closeLabel="Cerrar" data-component="registrar-salida-dialog" size="lg">
        <DialogHeader>
          <DialogTitle>Registrar salida</DialogTitle>
          <DialogDescription>
            Captura lo que sale en esta entrega. Cada salida se factura por separado y el pedido no
            cambia.
          </DialogDescription>
        </DialogHeader>
        <DialogBody>
          <div className="space-y-5">
            <FormSeccion titulo="1. Salida">
              <FormCampo span={6} className="space-y-1.5">
                <Label htmlFor={`${id}-referencia`} className="text-xs font-medium text-ink-strong">
                  Folio de salida o contenedor *
                </Label>
                <Input
                  id={`${id}-referencia`}
                  value={referencia}
                  maxLength={REFERENCIA_SALIDA_MAX}
                  onChange={(e) => setReferencia(e.target.value)}
                  aria-describedby={`${id}-referencia-ayuda`}
                  disabled={registrar.isPending}
                  className="font-mono"
                />
                <p id={`${id}-referencia-ayuda`} className="text-xs text-ink-muted">
                  Identifica la salida; si la registras dos veces con las mismas cantidades no se
                  duplica.
                </p>
              </FormCampo>
            </FormSeccion>

            <FormSeccion titulo="2. Partidas que salen">
              <FormCampo span={12}>
                <div className="overflow-x-auto rounded-md border border-line">
                  <Table className="w-full text-sm">
                    <TableHeader className="bg-surface-subtle">
                      <TableRow>
                        <TableHead className="px-3 py-2 text-left">Partida</TableHead>
                        <TableHead className="px-3 py-2 text-left">Producto / servicio</TableHead>
                        <TableHead className="px-3 py-2 text-right">Pedida</TableHead>
                        <TableHead className="px-3 py-2 text-right">Pendiente de salir</TableHead>
                        <TableHead className="w-40 px-3 py-2 text-right">En esta salida</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {filas.map((f) => (
                        <TableRow key={f.posicion} className="border-t border-line-row">
                          <TableCell className="px-3 py-2 text-ink-muted tabular-nums">
                            {f.posicion}
                          </TableCell>
                          <TableCell className="px-3 py-2">{f.descripcion}</TableCell>
                          <TableCell className="px-3 py-2 text-right tabular-nums">
                            {formatCantidad(f.pedida)}
                          </TableCell>
                          <TableCell className="px-3 py-2 text-right tabular-nums">
                            {formatCantidad(f.pendiente)}
                          </TableCell>
                          <TableCell className="px-3 py-2 text-right">
                            <Input
                              type="number"
                              inputMode="decimal"
                              min={0}
                              max={f.pendiente}
                              step="any"
                              value={f.texto}
                              placeholder="0"
                              aria-label={`Cantidad de la partida ${f.posicion} en esta salida`}
                              aria-invalid={f.invalida}
                              disabled={registrar.isPending || f.pendiente === 0}
                              onChange={(e) =>
                                setCantidades((prev) => ({ ...prev, [f.posicion]: e.target.value }))
                              }
                              className="ml-auto w-32 text-right tabular-nums"
                            />
                            {f.invalida && (
                              <p className="mt-1 text-xs text-danger-fg">
                                Máximo {formatCantidad(f.pendiente)}.
                              </p>
                            )}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              </FormCampo>
            </FormSeccion>

            {error && (
              <p role="alert" className="text-sm text-danger-fg">
                {error}
              </p>
            )}
          </div>
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={onCerrar} disabled={registrar.isPending}>
            Cancelar
          </Button>
          <Button
            onClick={registrarSalida}
            disabled={motivoBloqueo != null || registrar.isPending}
            title={motivoBloqueo}
            data-action="registrar-salida"
          >
            {registrar.isPending && <Loader2 className="animate-spin" />}
            Registrar salida
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
