import { FormCampo } from '@/components/erp/forms/FormGrid';
import { Label } from '@/components/ui/label';
import { CONCEPTOS_RETENCION_CLIENTE } from '@/features/facturacion/api/types';
import { totalesCartaPorte } from '@/features/facturacion/lib/carta-porte-retencion';
import { formatMoney } from '@/lib/money';
import { useEffect } from 'react';
import { Controller, useWatch, type Control, type UseFormSetValue } from 'react-hook-form';

/** Campos que la retención lee del formulario de Carta Porte (nueva o siguiente tramo). */
type ConRetencion = {
  tipoCfdi: 'T' | 'I';
  montoServicio: number;
  tasaIvaServicio: number | null;
  conceptoRetencion?: string | null;
  moneda?: string;
};

/**
 * P51: retención de IVA que hace el cliente sobre el flete (solo tipo I), con el catálogo de la factura de servicio, y el resumen
 * IVA / retención / total que se timbrará. En tipo T el selector se desactiva y se limpia.
 */
export function RetencionServicioCampos<T extends ConRetencion>({
  control,
  setValue,
  span = 6,
}: {
  control: Control<T>;
  setValue: UseFormSetValue<T>;
  span?: 3 | 4 | 6 | 12;
}) {
  // react-hook-form no estrecha bien genéricos: se trabaja sobre la forma mínima.
  const c = control as unknown as Control<ConRetencion>;
  const set = setValue as unknown as UseFormSetValue<ConRetencion>;
  const [tipoCfdi, monto, tasaIva, concepto, moneda] = useWatch({
    control: c,
    name: ['tipoCfdi', 'montoServicio', 'tasaIvaServicio', 'conceptoRetencion', 'moneda'],
  });
  const esIngreso = tipoCfdi === 'I';

  useEffect(() => {
    if (!esIngreso && concepto) set('conceptoRetencion', null, { shouldDirty: true });
  }, [esIngreso, concepto, set]);

  const t = totalesCartaPorte(tipoCfdi, Number(monto) || 0, tasaIva, concepto);
  const currency = moneda && moneda.length === 3 && moneda !== 'XXX' ? moneda.toUpperCase() : 'MXN';
  const dinero = (amount: number) => formatMoney({ amount, currency });

  return (
    <>
      <FormCampo span={span} className="space-y-1">
        <Label className="text-xs" htmlFor="cp-retencion">
          Retención IVA del cliente
        </Label>
        <Controller
          name="conceptoRetencion"
          control={c}
          render={({ field }) => (
            <select
              id="cp-retencion"
              className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-sm disabled:opacity-50"
              disabled={!esIngreso}
              value={field.value ?? ''}
              onChange={(e) => field.onChange(e.target.value || null)}
            >
              <option value="">Sin retención</option>
              {CONCEPTOS_RETENCION_CLIENTE.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          )}
        />
        <p className="text-[11px] leading-tight text-ink-muted">
          {esIngreso
            ? 'Si el cliente retiene IVA del flete, se resta del total y viaja en el CFDI.'
            : 'Solo aplica a tipo I (flete facturado).'}
        </p>
      </FormCampo>
      {esIngreso && (
        <FormCampo span={span} className="space-y-1" data-testid="cp-resumen-servicio">
          <Label className="text-xs">Total del servicio</Label>
          <dl className="grid grid-cols-2 gap-x-3 text-xs tabular-nums">
            <dt className="text-ink-muted">IVA trasladado</dt>
            <dd className="text-right">{dinero(t.iva)}</dd>
            <dt className="text-ink-muted">Retención IVA</dt>
            <dd className="text-right">{t.retencion > 0 ? `− ${dinero(t.retencion)}` : dinero(0)}</dd>
            <dt className="font-medium">Total</dt>
            <dd className="text-right font-medium">{dinero(t.total)}</dd>
          </dl>
        </FormCampo>
      )}
    </>
  );
}
