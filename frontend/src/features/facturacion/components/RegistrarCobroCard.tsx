import { useState } from 'react';
import { HandCoins, Plus, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useRegistrarCobro, useSesionActual } from '@/features/facturacion/api/useCajaSesiones';
import type { CobroFormaPagoInput } from '@/features/facturacion/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';

const FORMAS: ReadonlyArray<{ clave: string; nombre: string }> = [
  { clave: '01', nombre: 'Efectivo' },
  { clave: '02', nombre: 'Cheque' },
  { clave: '03', nombre: 'Transferencia' },
  { clave: '04', nombre: 'Tarjeta de crédito' },
  { clave: '28', nombre: 'Tarjeta de débito' },
];

/** Origen del cobro (espejo de OrigenCobroMostrador backend). */
const ORIGEN_MOSTRADOR = 1;
const ORIGEN_LIQUIDACION_RUTA = 2;

/**
 * Forma de pago en el form: el importe se mantiene como STRING mientras se
 * teclea (si fuera number, `Number("0.")→0` borraría el punto en cada
 * pulsación y no se podrían capturar centavos). Se convierte a número al
 * enviar.
 */
type FormaLocal = Omit<CobroFormaPagoInput, 'importe'> & { importe: string };

/**
 * <c>&lt;RegistrarCobroCard/&gt;</c> — flujo emitir→cobrar (`[Decisión 12-E]`,
 * CAJAS-PR6): registra el cobro de un comprobante Timbrado a la sesión
 * abierta del cajero, con una o más formas de pago (suma = total) y origen
 * Mostrador o Liquidación de ruta (`[12-7]`, comando unitario en v1). Solo
 * se renderiza con <c>caja.operar</c> y sesión abierta del día.
 */
export function RegistrarCobroCard(props: {
  comprobanteId: string;
  folio: string;
  total: number;
  moneda: string;
}) {
  const puedeOperar = useHasPermission(PermisosCanonicos.FacturacionCajaOperar);
  const sesion = useSesionActual();
  const registrar = useRegistrarCobro();
  const idempotencyKey = useFormIdempotencyKey();

  const [abierto, setAbierto] = useState(false);
  const [origen, setOrigen] = useState(String(ORIGEN_MOSTRADOR));
  const [formas, setFormas] = useState<FormaLocal[]>([
    { formaPago: '01', importe: props.total.toFixed(2) },
  ]);
  const [registrado, setRegistrado] = useState(false);

  if (!puedeOperar || registrado) return null;
  if (sesion.data?.sesion == null || sesion.data.diaAnteriorPendiente) return null;
  if (sesion.data.sesion.estado !== 'Abierta') return null;

  const suma = formas.reduce((acc, f) => {
    const n = Number(f.importe);
    return acc + (Number.isFinite(n) ? n : 0);
  }, 0);
  const cuadra = Math.abs(suma - props.total) < 0.005;

  function actualizarForma(i: number, parcial: Partial<FormaLocal>) {
    setFormas((prev) => prev.map((f, j) => (j === i ? { ...f, ...parcial } : f)));
  }

  function onCobrar() {
    if (!cuadra) {
      toast.error(`La suma de formas (${suma.toFixed(2)}) debe igualar el total (${props.total.toFixed(2)}).`);
      return;
    }
    registrar.mutate(
      {
        body: {
          comprobanteId: props.comprobanteId,
          formasPago: formas.map((f) => ({ ...f, importe: Number(f.importe) })),
          origen: Number(origen),
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          setRegistrado(true);
          toast.success(`Cobro de ${props.folio} registrado en tu sesión.`);
        },
        onError: (error) =>
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo registrar el cobro.',
            { description: esApiError(error) ? error.problem.detail : undefined },
          ),
      },
    );
  }

  if (!abierto) {
    return (
      <Button variant="outline" onClick={() => setAbierto(true)} data-print="hidden">
        <HandCoins className="mr-2 h-4 w-4" />
        Registrar cobro
      </Button>
    );
  }

  return (
    <div
      className="space-y-3 rounded-md border border-dashed border-primary/40 bg-primary/5 p-3"
      data-print="hidden"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium">
          Cobrar {props.folio} — total {props.total.toFixed(2)} {props.moneda}
        </h3>
        <div className="w-56">
          <Select value={origen} onValueChange={setOrigen}>
            <SelectTrigger aria-label="Origen del cobro">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={String(ORIGEN_MOSTRADOR)}>Mostrador</SelectItem>
              <SelectItem value={String(ORIGEN_LIQUIDACION_RUTA)}>
                Liquidación de ruta
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>

      {formas.map((f, i) => (
        <div key={i} className="flex flex-wrap items-end gap-2">
          <div className="w-48 space-y-1">
            <Label>Forma de pago</Label>
            <Select
              value={f.formaPago}
              onValueChange={(v) => actualizarForma(i, { formaPago: v })}
            >
              <SelectTrigger aria-label={`Forma de pago ${i + 1}`}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {FORMAS.map((forma) => (
                  <SelectItem key={forma.clave} value={forma.clave}>
                    {forma.nombre}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="w-36 space-y-1">
            <Label>Importe</Label>
            <Input
              inputMode="decimal"
              value={f.importe}
              onChange={(e) => actualizarForma(i, { importe: e.target.value })}
              aria-label={`Importe ${i + 1}`}
            />
          </div>
          <div className="w-44 space-y-1">
            <Label>Referencia</Label>
            <Input
              value={f.referencia ?? ''}
              onChange={(e) =>
                actualizarForma(i, {
                  referencia: e.target.value === '' ? null : e.target.value,
                })
              }
              placeholder="Opcional"
            />
          </div>
          {formas.length > 1 && (
            <Button
              size="icon"
              variant="ghost"
              aria-label="Quitar forma de pago"
              onClick={() => setFormas((prev) => prev.filter((_, j) => j !== i))}
            >
              <X className="h-4 w-4" />
            </Button>
          )}
        </div>
      ))}

      <div className="flex flex-wrap items-center justify-between gap-2">
        <Button
          size="sm"
          variant="ghost"
          onClick={() =>
            setFormas((prev) => [
              ...prev,
              { formaPago: '03', importe: Math.max(props.total - suma, 0).toFixed(2) },
            ])
          }
        >
          <Plus className="mr-1 h-3.5 w-3.5" />
          Otra forma de pago
        </Button>
        <div className="flex items-center gap-2">
          {!cuadra && (
            <span className="text-xs text-destructive">
              Suma {suma.toFixed(2)} ≠ total {props.total.toFixed(2)}
            </span>
          )}
          <Button size="sm" variant="ghost" onClick={() => setAbierto(false)}>
            Cancelar
          </Button>
          <Button size="sm" onClick={onCobrar} disabled={registrar.isPending || !cuadra}>
            Cobrar
          </Button>
        </div>
      </div>
    </div>
  );
}
