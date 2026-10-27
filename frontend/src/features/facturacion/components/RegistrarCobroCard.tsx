import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
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
import type { CobroFormaPagoInput } from '@/features/facturacion/api/types';
import { useRegistrarCobro, useSesionActual } from '@/features/facturacion/api/useCajaSesiones';
import {
  autorizacionVoucherInvalida,
  esFormaTarjeta,
  MENSAJE_VOUCHER,
  voucherIncompleto,
  formasPagoCaja,
  formasPagoVigentes,
} from '@/features/facturacion/lib/formas-pago-caja';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useFormasPago } from '@/modules/catalogos/api/formas-pago';
import { HandCoins, Plus, X } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';

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
  /**
   * FIX-12 · bandeja «Por cobrar» de Mi caja: el formulario arranca abierto
   * (sin el botón «Registrar cobro») y avisa al cerrar o al cobrar para que
   * el panel lateral se cierre. Sin estas props se comporta como siempre.
   */
  abiertoInicial?: boolean;
  onCerrar?: () => void;
  onRegistrado?: () => void;
}) {
  const puedeOperar = useHasPermission(PermisosCanonicos.FacturacionCajaOperar);
  // Sin permiso de caja no se consulta la sesión (el endpoint respondería 403).
  const sesion = useSesionActual({ enabled: puedeOperar });
  const registrar = useRegistrarCobro();
  const catalogo = useFormasPago();
  const disponibles = formasPagoCaja(catalogo.data ?? []);
  const idempotencyKey = useFormIdempotencyKey();

  const [abierto, setAbierto] = useState(props.abiertoInicial ?? false);
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
  const vigentes = formasPagoVigentes(
    formas.map((f) => f.formaPago),
    disponibles,
  );
  const cuadra = Math.abs(suma - props.total) < 0.005;
  const sinVoucher = voucherIncompleto(formas);

  function actualizarForma(i: number, parcial: Partial<FormaLocal>) {
    setFormas((prev) => prev.map((f, j) => (j === i ? { ...f, ...parcial } : f)));
  }

  function onCobrar() {
    if (!vigentes) {
      toast.error('Selecciona una forma de pago SAT activa para cada importe.');
      return;
    }
    if (sinVoucher) {
      toast.error(MENSAJE_VOUCHER);
      return;
    }
    if (!cuadra) {
      toast.error(
        `La suma de formas (${suma.toFixed(2)}) debe igualar el total (${props.total.toFixed(2)}).`,
      );
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
          props.onRegistrado?.();
        },
        onError: (error) =>
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo registrar el cobro.', {
            description: esApiError(error) ? error.problem.detail : undefined,
          }),
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
      className="space-y-3 rounded-md border border-dashed border-brand/40 bg-brand/5 p-3"
      data-print="hidden"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-medium">
          Cobrar {props.folio} — total {props.total.toFixed(2)} {props.moneda}
        </h3>
      </div>

      {catalogo.isError && (
        <p role="alert" className="text-xs text-danger-fg">
          No se pudieron consultar las formas de pago. Vuelve a abrir el cobro.
        </p>
      )}
      {formas.map((f, i) => (
        <FormGrid key={i}>
          <FormCampo span={6} className="space-y-1">
            <Label>Forma de pago</Label>
            <Select value={f.formaPago} onValueChange={(v) => actualizarForma(i, { formaPago: v })}>
              <SelectTrigger aria-label={`Forma de pago ${i + 1}`}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {disponibles.map((forma) => (
                  <SelectItem key={forma.claveSat} value={forma.claveSat}>
                    {forma.descripcion}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </FormCampo>
          <FormCampo span={6} className="space-y-1">
            <Label>Importe</Label>
            <Input
              inputMode="decimal"
              value={f.importe}
              onChange={(e) => actualizarForma(i, { importe: e.target.value })}
              aria-label={`Importe ${i + 1}`}
            />
          </FormCampo>
          {i === 0 && (
            <FormCampo span={6} className="space-y-1">
              <Label htmlFor="cobro-origen">Canal</Label>
              <Select value={origen} onValueChange={setOrigen}>
                <SelectTrigger id="cobro-origen" aria-label="Origen del cobro">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={String(ORIGEN_MOSTRADOR)}>Mostrador</SelectItem>
                  <SelectItem value={String(ORIGEN_LIQUIDACION_RUTA)}>
                    Liquidación de ruta
                  </SelectItem>
                </SelectContent>
              </Select>
            </FormCampo>
          )}
          <FormCampo span={6} className="space-y-1">
            <Label htmlFor={`cobro-referencia-${i}`}>
              {esFormaTarjeta(f.formaPago) ? 'Número de autorización del voucher' : 'Referencia'}
            </Label>
            <Input
              id={`cobro-referencia-${i}`}
              value={f.referencia ?? ''}
              onChange={(e) =>
                actualizarForma(i, {
                  referencia: e.target.value === '' ? null : e.target.value,
                })
              }
              placeholder={esFormaTarjeta(f.formaPago) ? 'Obligatorio · ej. 123456' : 'Opcional'}
              maxLength={100}
              aria-invalid={
                esFormaTarjeta(f.formaPago) &&
                ((f.referencia ?? '').trim() === '' || autorizacionVoucherInvalida([f]))
              }
              aria-required={esFormaTarjeta(f.formaPago)}
            />
          </FormCampo>
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
        </FormGrid>
      ))}

      <ResumenImportes
        moneda={props.moneda}
        total={props.total}
        estadoPago={[
          { etiqueta: 'Por cobrar', monto: props.total },
          { etiqueta: 'Importe capturado', monto: suma },
        ]}
        acciones={
          <>
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
                <span className="text-xs text-danger-fg">
                  Suma {suma.toFixed(2)} ≠ total {props.total.toFixed(2)}
                </span>
              )}
              <Button
                size="sm"
                variant="ghost"
                onClick={() => {
                  setAbierto(false);
                  props.onCerrar?.();
                }}
              >
                Cancelar
              </Button>
              <Button
                size="sm"
                onClick={onCobrar}
                disabled={registrar.isPending || !cuadra || !vigentes || sinVoucher}
                title={
                  !vigentes
                    ? 'Selecciona formas de pago SAT activas.'
                    : sinVoucher
                      ? MENSAJE_VOUCHER
                      : undefined
                }
              >
                Cobrar
              </Button>
            </div>
          </>
        }
      />
    </div>
  );
}
