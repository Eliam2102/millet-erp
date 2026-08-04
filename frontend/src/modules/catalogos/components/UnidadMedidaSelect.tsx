import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useUnidadesMedidaList } from '@/modules/catalogos/api';
import { EstatusCatalogo } from '@/modules/catalogos/api/types';

const SIN_ASIGNAR = '__sin_asignar__';

export interface UnidadMedidaSelectProps {
  value: string | null;
  onChange: (id: string | null) => void;
  disabled?: boolean;
  /**
   * Muestra la opción "— sin asignar —" (edición de artículos legacy con
   * FK NULL). En alta NO se pasa: la unidad es obligatoria.
   */
  permitirVacio?: boolean;
}

/**
 * Selector de unidad de medida del catálogo (ADR-0046 Etapa 1b). shadcn
 * Select sobre las unidades ACTIVAS. Controlado (value/onChange) para
 * usarse con <c>&lt;Controller/&gt;</c> de react-hook-form.
 */
export function UnidadMedidaSelect({
  value,
  onChange,
  disabled,
  permitirVacio,
}: UnidadMedidaSelectProps) {
  const query = useUnidadesMedidaList();
  const activas = (query.data ?? []).filter(
    (u) => u.estatus === EstatusCatalogo.Activo,
  );

  return (
    <Select
      value={value && value.length > 0 ? value : SIN_ASIGNAR}
      onValueChange={(v) => onChange(v === SIN_ASIGNAR ? null : v)}
      disabled={disabled || query.isLoading}
    >
      <SelectTrigger>
        <SelectValue placeholder="Selecciona una unidad…" />
      </SelectTrigger>
      <SelectContent>
        {permitirVacio && (
          <SelectItem value={SIN_ASIGNAR}>— sin asignar —</SelectItem>
        )}
        {activas.map((u) => (
          <SelectItem key={u.id} value={u.id}>
            {u.codigo} · {u.nombre}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
