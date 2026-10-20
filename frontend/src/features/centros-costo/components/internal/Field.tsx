import type { ReactNode } from 'react';
import { FormCampo } from '@/components/erp/forms/FormGrid';

/**
 * <c>&lt;Field/&gt;</c> — label + required + error inline, compartido por
 * los modales del catálogo (molde FormRow de NuevaRequisicion; convención
 * por-feature). <c>full</c> = ocupa las 2 columnas del grid del modal.
 */
export interface FieldProps {
  label: string;
  /** Id del control asociado (label htmlFor — a11y y getByLabelText). */
  htmlFor?: string;
  required?: boolean;
  error?: string;
  /** Ocupa el ancho completo del grid de 2 columnas (md:col-span-2). */
  full?: boolean;
  children: ReactNode;
}

export function Field({ label, htmlFor, required, error, full, children }: FieldProps) {
  return (
    <FormCampo span={full ? 12 : 6} className="space-y-1.5">
      <label
        htmlFor={htmlFor}
        className="flex items-center gap-1 text-xs font-medium text-ink-strong"
      >
        {label}
        {required && (
          <span aria-hidden="true" className="text-danger-fg">
            *
          </span>
        )}
      </label>
      {children}
      {error != null && (
        <p role="alert" className="text-xs text-danger-fg">
          {error}
        </p>
      )}
    </FormCampo>
  );
}
