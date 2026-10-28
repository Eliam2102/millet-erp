import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useClasificacionesNoDeducibles } from '@/features/cxp/api/useClasificacionesNoDeducibles';

/** Select del catálogo de clasificaciones no deducibles (P8.6); `value` es la clave. */
export function ClasificacionNoDeducibleSelect({
  value,
  onChange,
  ariaLabel = 'Clasificación de gasto no deducible',
  ariaDescribedBy,
}: {
  value: string | null;
  onChange: (clave: string) => void;
  ariaLabel?: string;
  ariaDescribedBy?: string;
}) {
  const { data, isPending, isError } = useClasificacionesNoDeducibles();
  return (
    <Select value={value ?? ''} onValueChange={onChange} disabled={isPending || isError}>
      <SelectTrigger aria-label={ariaLabel} aria-describedby={ariaDescribedBy}>
        <SelectValue
          placeholder={isError ? 'No se pudo cargar el catálogo' : isPending ? 'Cargando…' : 'Selecciona…'}
        />
      </SelectTrigger>
      <SelectContent>
        {(data ?? []).map((c) => (
          <SelectItem key={c.clave} value={c.clave}>
            {c.descripcion}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
