import type { ReactNode } from 'react';

/**
 * <c>&lt;Field/&gt;</c> — wrapper compartido por los sheets del
 * módulo Almacén: label + (asterisco si required) + children +
 * error inline. Mismo shape que el helper interno de cada sheet
 * de Compras (los sheets de Compras lo redefinen localmente).
 *
 * <para>Internamente compartido aquí para evitar copy-paste en
 * los múltiples sheets de devoluciones (8.A + MAT-REV + 8.B).</para>
 */
export interface FieldProps {
  label: string;
  required?: boolean;
  error?: string;
  children: ReactNode;
}

export function Field({ label, required, error, children }: FieldProps) {
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
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
