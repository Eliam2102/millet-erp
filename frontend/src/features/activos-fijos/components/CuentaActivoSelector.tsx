import { CuentaSelector } from '@/features/contabilidad/components/dimensiones/CuentaSelector';
import { useCuenta } from '@/features/contabilidad/api/hooks';
export function CuentaActivoSelector({
  value,
  onChange,
}: {
  value: string | null;
  onChange: (id: string | null) => void;
}) {
  const cuenta = useCuenta(value);
  const c = cuenta.data?.data;
  return (
    <CuentaSelector
      value={c ? { id: c.id, codigo: c.codigo, nombre: c.nombre } : null}
      onChange={(v) => onChange(v?.id ?? null)}
      soloAfectables
      vacio="Sin cuenta configurada"
    />
  );
}
