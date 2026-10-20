import { useWatch, type UseFormReturn } from 'react-hook-form';
import { Info } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { AvisoEnLinea } from '@/components/erp';
import {
  ClasePedidoManual,
  ComportamientoFiscal,
  SubtipoVentaDirecta,
} from '@/features/facturacion/api/types';
import { OPCIONES_SUBTIPO_VENTA_DIRECTA } from '@/features/facturacion/lib/glosario';
import type { PedidoManualValues } from '@/features/facturacion/schemas/pedido-manual';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ClasePedidoFieldset/&gt;</c> — bloque «Clase de pedido» del form
 * de pedido manual (F6.1). Todo pedido manual se clasifica:
 *
 * <list type="bullet">
 *   <item><b>Venta directa</b> con subtipo Activo / Almacenable / Servicio.
 *   Activo fija el comportamiento fiscal en «Venta de activo fijo».</item>
 *   <item><b>Emergencia</b> (A+W fuera de servicio): solo con el permiso
 *   <c>facturacion.pedidos.capturar-emergencia</c>; motivo obligatorio y
 *   referencia opcional al pedido A+W para conciliar después.</item>
 * </list>
 *
 * Ambas clases se facturan con la serie de Venta directa de la sucursal y
 * no se informan a A+W.
 */
export interface ClasePedidoFieldsetProps {
  form: UseFormReturn<PedidoManualValues>;
  /** Permiso `facturacion.pedidos.capturar-emergencia`. */
  puedeEmergencia: boolean;
  /**
   * Muestra la opción Emergencia aunque no haya permiso (deshabilitada):
   * al editar un pedido que ya es de emergencia.
   */
  mostrarEmergenciaSinPermiso?: boolean;
}

const OPCIONES_CLASE = [
  {
    value: ClasePedidoManual.VentaDirecta,
    label: 'Venta directa',
    descripcion:
      'Los pedidos de producción se capturan en A+W; aquí solo ventas que no pasan por producción.',
  },
  {
    value: ClasePedidoManual.Emergencia,
    label: 'Emergencia',
    descripcion: 'Captura temporal mientras A+W no está disponible.',
  },
] as const;

export function ClasePedidoFieldset({
  form,
  puedeEmergencia,
  mostrarEmergenciaSinPermiso = false,
}: ClasePedidoFieldsetProps) {
  const clase = useWatch({ control: form.control, name: 'claseManual' });
  const subtipo = useWatch({ control: form.control, name: 'subtipoVentaDirecta' });
  const errores = form.formState.errors;
  const opciones = OPCIONES_CLASE.filter(
    (o) =>
      o.value !== ClasePedidoManual.Emergencia ||
      puedeEmergencia ||
      mostrarEmergenciaSinPermiso,
  );

  const opts = { shouldDirty: true, shouldValidate: form.formState.isSubmitted } as const;

  function elegirClase(valor: ClasePedidoManual) {
    form.setValue('claseManual', valor, opts);
    if (valor === ClasePedidoManual.Emergencia) {
      // Una emergencia no lleva subtipo ni puede ser venta de activo fijo.
      form.setValue('subtipoVentaDirecta', null, opts);
      if (form.getValues('comportamientoFiscal') === ComportamientoFiscal.VentaActivoFijo) {
        form.setValue('comportamientoFiscal', ComportamientoFiscal.MostradorInmediato, opts);
      }
    }
  }

  function elegirSubtipo(valor: SubtipoVentaDirecta) {
    form.setValue('subtipoVentaDirecta', valor, opts);
    const comportamiento = form.getValues('comportamientoFiscal');
    if (valor === SubtipoVentaDirecta.Activo) {
      form.setValue('comportamientoFiscal', ComportamientoFiscal.VentaActivoFijo, opts);
    } else if (comportamiento === ComportamientoFiscal.VentaActivoFijo) {
      form.setValue('comportamientoFiscal', ComportamientoFiscal.MostradorInmediato, opts);
    }
  }

  return (
    <fieldset className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <legend className="float-left w-full text-sm font-semibold text-ink">
        Clase de pedido <span className="text-danger-fg">*</span>
      </legend>
      <div className="clear-left pt-0.5">
        <p className="flex items-start gap-1.5 text-xs text-ink-muted">
          <Info aria-hidden="true" className="mt-0.5 size-3.5 shrink-0" strokeWidth={1.8} />
          Se facturará con la serie de Venta directa de la sucursal y no se
          informa a A+W.
        </p>
      </div>

      <div
        role="radiogroup"
        aria-label="Clase de pedido"
        aria-invalid={errores.claseManual != null || undefined}
        className="grid grid-cols-1 gap-2 sm:grid-cols-2"
      >
        {opciones.map((o) => {
          const checked = clase === o.value;
          const deshabilitada =
            o.value === ClasePedidoManual.Emergencia && !puedeEmergencia;
          return (
            <label
              key={o.value}
              title={
                deshabilitada
                  ? 'Requiere el permiso para capturar pedidos de emergencia.'
                  : undefined
              }
              className={cn(
                'flex items-start gap-2.5 rounded-md border px-3 py-2.5 text-sm',
                checked
                  ? 'border-brand-soft-border bg-surface-selected'
                  : 'border-line-control bg-surface-card',
                deshabilitada ? 'cursor-not-allowed opacity-60' : 'cursor-pointer',
              )}
            >
              <input
                type="radio"
                name="claseManual"
                className="mt-0.5 accent-brand"
                checked={checked}
                disabled={deshabilitada}
                onChange={() => elegirClase(o.value)}
              />
              <span className="space-y-0.5">
                <span className="block font-medium text-ink">{o.label}</span>
                <span className="block text-xs text-ink-muted">{o.descripcion}</span>
              </span>
            </label>
          );
        })}
      </div>
      {errores.claseManual?.message != null && (
        <p role="alert" className="text-xs text-danger-fg">
          {errores.claseManual.message}
        </p>
      )}

      {clase === ClasePedidoManual.VentaDirecta && (
        <div className="space-y-1.5">
          <p id="subtipo-vd-label" className="text-xs font-medium text-ink-strong">
            Subtipo de venta directa <span className="text-danger-fg">*</span>
          </p>
          <div
            role="radiogroup"
            aria-labelledby="subtipo-vd-label"
            className="grid grid-cols-1 gap-2 sm:grid-cols-3"
          >
            {OPCIONES_SUBTIPO_VENTA_DIRECTA.map((o) => {
              const checked = subtipo === o.value;
              return (
                <label
                  key={o.value}
                  className={cn(
                    'flex cursor-pointer items-start gap-2 rounded-md border px-3 py-2 text-sm',
                    checked
                      ? 'border-brand-soft-border bg-surface-selected'
                      : 'border-line-control bg-surface-card',
                  )}
                >
                  <input
                    type="radio"
                    name="subtipoVentaDirecta"
                    className="mt-0.5 accent-brand"
                    checked={checked}
                    onChange={() => elegirSubtipo(o.value)}
                  />
                  <span className="space-y-0.5">
                    <span className="block font-medium text-ink">{o.label}</span>
                    <span className="block text-xs text-ink-muted">{o.descripcion}</span>
                  </span>
                </label>
              );
            })}
          </div>
          {errores.subtipoVentaDirecta?.message != null && (
            <p role="alert" className="text-xs text-danger-fg">
              {errores.subtipoVentaDirecta.message}
            </p>
          )}
        </div>
      )}

      {clase === ClasePedidoManual.Emergencia && (
        <div className="space-y-3">
          <AvisoEnLinea tono="advertencia">
            Úsalo solo si A+W no está disponible. Se factura con la serie de
            venta directa y queda marcado para conciliación.
          </AvisoEnLinea>
          <div className="space-y-1.5">
            <label
              htmlFor="motivo-emergencia"
              className="text-xs font-medium text-ink-strong"
            >
              Motivo de la emergencia <span className="text-danger-fg">*</span>
            </label>
            <Textarea
              id="motivo-emergencia"
              maxLength={500}
              placeholder="Por qué no se pudo capturar en A+W"
              aria-invalid={errores.motivoEmergencia != null || undefined}
              {...form.register('motivoEmergencia')}
            />
            {errores.motivoEmergencia?.message != null && (
              <p role="alert" className="text-xs text-danger-fg">
                {errores.motivoEmergencia.message}
              </p>
            )}
          </div>
          <div className="space-y-1.5 sm:w-1/2">
            <label
              htmlFor="referencia-pedido-aw"
              className="text-xs font-medium text-ink-strong"
            >
              Pedido A+W{' '}
              <span className="font-normal text-ink-muted">(opcional)</span>
            </label>
            <Input
              id="referencia-pedido-aw"
              maxLength={50}
              className="font-mono"
              placeholder="Para conciliar después"
              aria-invalid={errores.referenciaPedidoAw != null || undefined}
              {...form.register('referenciaPedidoAw')}
            />
            {errores.referenciaPedidoAw?.message != null && (
              <p role="alert" className="text-xs text-danger-fg">
                {errores.referenciaPedidoAw.message}
              </p>
            )}
          </div>
        </div>
      )}
    </fieldset>
  );
}
