import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import { ClasificacionNoDeducibleSelect } from '@/features/cxp/components/ClasificacionNoDeducibleSelect';

/**
 * Campo de clasificación no deducible por línea (P8.6). `value`: null = deducible,
 * '' = marcado sin elegir, clave = no deducible. Con `obligatoria` (ticket sin CFDI) solo
 * muestra el select; si no, un checkbox «Gasto no deducible» que lo despliega.
 */
export function ClasificacionNoDeducibleCampo({
  id,
  value,
  onChange,
  obligatoria,
  error,
}: {
  id: string;
  value: string | null | undefined;
  onChange: (value: string | null) => void;
  obligatoria: boolean;
  error?: string;
}) {
  const marcado = obligatoria || (value ?? null) !== null;
  return (
    <div className="space-y-1">
      {obligatoria ? (
        <Label className="text-xs">
          Clasificación (ticket no deducible)
          <span className="ml-1 text-destructive">*</span>
        </Label>
      ) : (
        <div className="flex items-center gap-2">
          <Checkbox
            id={id}
            checked={marcado}
            onCheckedChange={(v) => onChange(v === true ? '' : null)}
          />
          <Label htmlFor={id} className="text-xs">
            Gasto no deducible (su IVA va a «IVA no acreditable»)
          </Label>
        </div>
      )}
      {marcado && (
        <ClasificacionNoDeducibleSelect value={value || null} onChange={onChange} />
      )}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
