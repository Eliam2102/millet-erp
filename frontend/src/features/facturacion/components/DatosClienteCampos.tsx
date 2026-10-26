import { useId } from 'react';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { Label } from '@/components/ui/label';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';

import { camposDatosCliente, type DatosCliente } from '../lib/datos-cliente';

export function DatosClienteCampos({ datos, onChange, readOnly = false, errors, condicionesPagoPlaceholder }: {
  datos: DatosCliente;
  condicionesPagoPlaceholder?: string;
  onChange?: (campo: keyof DatosCliente, valor: string) => void;
  readOnly?: boolean;
  errors?: Partial<Record<keyof DatosCliente, string>>;
}) {
  const id = useId();
  return (
    <section className="space-y-3">
      <h2 className="text-md font-semibold">Datos para el cliente</h2>
      <p className="text-xs text-ink-muted">Se imprimen en la factura; no forman parte del CFDI</p>
      <FormGrid>
        {camposDatosCliente.map(([campo, label, maxLength]) => {
          const Control = campo === 'observaciones' ? Textarea : Input;
          return (
            <FormCampo key={campo} span={campo === 'observaciones' ? 12 : 6}>
              <Label htmlFor={`${id}-${campo}`}>{label}</Label>
              <Control id={`${id}-${campo}`} value={datos[campo] ?? ''} placeholder={campo === 'condicionesPago' ? condicionesPagoPlaceholder : undefined} maxLength={maxLength}
                readOnly={readOnly} aria-invalid={Boolean(errors?.[campo])} aria-describedby={errors?.[campo] ? `${id}-${campo}-error` : undefined} onChange={(e) => onChange?.(campo, e.target.value)} />
              {errors?.[campo] && <p id={`${id}-${campo}-error`} role="alert" className="text-xs text-danger-fg">{errors[campo]}</p>}
            </FormCampo>
          );
        })}
      </FormGrid>
    </section>
  );
}
