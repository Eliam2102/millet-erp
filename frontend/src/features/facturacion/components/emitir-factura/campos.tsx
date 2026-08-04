import { Label } from '@/components/ui/label';

/** Sección de campos del form de emisión (grid 2 columnas). */
export function Seccion({
  titulo,
  children,
}: {
  titulo: string;
  children: React.ReactNode;
}) {
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
  return (
    <div className="space-y-1">
      <Label className="text-xs">
        {label}
        {required && <span className="ml-1 text-destructive">*</span>}
      </Label>
      {children}
      {hint != null && (
        <p className="text-[11px] leading-tight text-muted-foreground">{hint}</p>
      )}
      {error != null && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
