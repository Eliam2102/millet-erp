import { Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { formatCantidad } from '@/lib/format-cantidad';
import type { VarianteVidrio } from '@/features/almacen/api/types';
import {
  etiquetaMedida,
  m2Renglon,
  totalesM2,
  type MedidaHoja,
} from '@/features/almacen/components/nueva-recepcion-helpers';

/**
 * Renglones «medida + hojas» de una partida de vidrio crudo (F1-MP-01). La OC va en m²; el almacenista captura hojas por
 * medida y cada renglón muestra sus m². Varias medidas pueden salir de la misma línea de OC.
 */
export function MedidasHojaFila({
  idBase,
  posicion,
  variantes,
  medidas,
  disabled,
  error,
  onChange,
}: {
  idBase: string;
  posicion: number;
  variantes: readonly VarianteVidrio[];
  medidas: readonly MedidaHoja[];
  disabled: boolean;
  error?: string;
  onChange: (medidas: MedidaHoja[]) => void;
}) {
  const actualizar = (i: number, cambio: Partial<MedidaHoja>) =>
    onChange(medidas.map((m, j) => (j === i ? { ...m, ...cambio } : m)));
  const entero = (v: string) => (v === '' ? 0 : Math.trunc(Number(v)));
  const total = totalesM2(medidas, variantes);
  const idError = `${idBase}-medidas-error`;

  return (
    <div className="space-y-2" role="group" aria-label={`Medidas de hoja, partida ${posicion}`}>
      {medidas.map((m, i) => {
        const variante = variantes.find((v) => v.varianteId === m.varianteId);
        const usadas = new Set(medidas.filter((_, j) => j !== i).map((x) => x.varianteId));
        return (
          <div key={i} className="flex flex-wrap items-end gap-2">
            <div className="flex w-full min-w-0 flex-col gap-0.5">
              <span className="text-2xs text-ink-muted">Medida</span>
              <Select
                value={m.varianteId ?? ''}
                onValueChange={(id) => actualizar(i, { varianteId: id })}
                disabled={disabled}
              >
                <SelectTrigger
                  className="w-full"
                  aria-label={`Medida de la hoja, renglón ${i + 1}, partida ${posicion}`}
                  aria-invalid={error ? true : undefined}
                  aria-describedby={error ? idError : undefined}
                >
                  <SelectValue placeholder="Elige la medida" />
                </SelectTrigger>
                <SelectContent>
                  {variantes.map((v) => (
                    <SelectItem
                      key={v.varianteId}
                      value={v.varianteId}
                      disabled={usadas.has(v.varianteId)}
                    >
                      {etiquetaMedida(v)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <label className="flex flex-col gap-0.5 text-2xs text-ink-muted">
              Hojas
              <Input
                type="number"
                inputMode="numeric"
                min="0"
                step="1"
                className="w-24 text-right tabular-nums"
                disabled={disabled}
                value={m.hojas || ''}
                onChange={(e) => actualizar(i, { hojas: entero(e.target.value) })}
                aria-label={`Hojas recibidas, renglón ${i + 1}, partida ${posicion}`}
                aria-invalid={error ? true : undefined}
                placeholder="0"
              />
            </label>
            <label className="flex flex-col gap-0.5 text-2xs text-ink-muted">
              Dañadas
              <Input
                type="number"
                inputMode="numeric"
                min="0"
                step="1"
                className="w-24 text-right tabular-nums"
                disabled={disabled}
                value={m.danadas || ''}
                onChange={(e) => actualizar(i, { danadas: entero(e.target.value) })}
                aria-label={`Hojas dañadas, renglón ${i + 1}, partida ${posicion}`}
                placeholder="0"
              />
            </label>
            <span
              className="self-center whitespace-nowrap text-xs tabular-nums text-ink-muted"
              aria-live="polite"
            >
              {variante?.areaHojaM2 != null
                ? `${formatCantidad(m2Renglon(m.hojas, variante.areaHojaM2), 4)} m²`
                : '— m²'}
            </span>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              disabled={disabled || medidas.length === 1}
              onClick={() => onChange(medidas.filter((_, j) => j !== i))}
              aria-label={`Quitar renglón ${i + 1}, partida ${posicion}`}
            >
              <Trash2 className="size-4" aria-hidden="true" />
            </Button>
          </div>
        );
      })}
      <div className="flex items-center justify-between gap-2">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={disabled || medidas.length >= variantes.length}
          onClick={() => onChange([...medidas, { varianteId: null, hojas: 0, danadas: 0 }])}
        >
          <Plus className="mr-1 size-4" aria-hidden="true" />
          Agregar medida
        </Button>
        <span className="text-xs tabular-nums text-ink-muted">
          Total {formatCantidad(total.cantidad, 4)} m²
        </span>
      </div>
      {error && (
        <p id={idError} role="alert" className="text-2xs text-danger-fg">
          {error}
        </p>
      )}
    </div>
  );
}
