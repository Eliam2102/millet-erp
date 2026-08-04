import type { ReactNode } from 'react';
import {
  DimensionUnidad,
  DIMENSION_UNIDAD_LABEL,
} from '@/modules/catalogos/api/types';

/**
 * Helpers compartidos entre el Sheet "Nueva unidad" y el inline edit de la
 * página (ADR-0046). Sin componente Select de shadcn en el repo → usamos un
 * <c>&lt;select&gt;</c> nativo estilizado como los Inputs.
 */
export function FormRow({
  label,
  required,
  hint,
  error,
  children,
}: {
  label: string;
  required?: boolean;
  hint?: string;
  error?: string;
  children: ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <label className="flex items-center gap-1 text-sm font-medium">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {hint != null && error == null && (
        <p className="text-xs text-muted-foreground">{hint}</p>
      )}
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}

export function DimensionSelect({
  value,
  onChange,
  disabled,
}: {
  value: number;
  onChange: (v: number) => void;
  disabled?: boolean;
}) {
  return (
    <select
      aria-label="Dimensión"
      className="h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
      value={value}
      disabled={disabled}
      onChange={(e) => onChange(Number(e.target.value))}
    >
      {(Object.values(DimensionUnidad) as DimensionUnidad[]).map((d) => (
        <option key={d} value={d}>
          {DIMENSION_UNIDAD_LABEL[d]}
        </option>
      ))}
    </select>
  );
}
