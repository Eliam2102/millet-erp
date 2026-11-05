import { Field } from '@/components/erp/forms/Field';

/** Sección de campos del form de emisión (grid 2 columnas). */
export function Seccion({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <section className="space-y-2">
      <h3 className="text-sm font-medium">{titulo}</h3>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">{children}</div>
    </section>
  );
}

interface CampoProps {
  label: string;
  required?: boolean;
  error?: string;
  hint?: string;
  children: React.ReactNode;
}

export function Campo({ label, required, error, hint, children }: CampoProps) {
  return <Field label={label} required={required} error={error} hint={hint}>{children}</Field>;
}
