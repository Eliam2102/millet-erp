import { useEffect, useRef } from 'react';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { AlmacenSelector } from '@/components/erp/selectors/AlmacenSelector';
import { NivelReorden } from '@/features/almacen/api/types';

/**
 * <c>&lt;EntidadReordenSelector/&gt;</c> — selector polimórfico de la entidad
 * de reabasto (código = reorden, ADR-0047 PR5.F). Renderiza
 * <see cref="SucursalSelector"/> cuando <c>nivel === Sucursal</c> (N1) o
 * <see cref="AlmacenSelector"/> cuando <c>nivel === Almacen</c> (N2).
 *
 * <para>Al CAMBIAR de nivel resetea la entidad a <c>null</c> (patrón §6.8,
 * mismo espíritu que <see cref="DepartamentoSelectorPorSucursal"/>): un id de
 * sucursal no aplica como almacén y viceversa. NO resetea en el montaje
 * inicial, para preservar el <c>value</c> al editar (donde el nivel es
 * inmutable). Ambos selectores hijos son eager (catálogo chico) y resuelven
 * su etiqueta desde la lista cargada, así que no requieren cold value.</para>
 */
export interface EntidadReordenSelectorProps {
  /** Nivel actual del form: define cuál selector se muestra. */
  nivel: NivelReorden;
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  disabled?: boolean;
  className?: string;
}

export function EntidadReordenSelector({
  nivel,
  value,
  onChange,
  disabled,
  className,
}: EntidadReordenSelectorProps) {
  // Guarda el nivel visto por última vez. El efecto se salta el primer render
  // (nivel inicial === actual) y solo resetea cuando el usuario lo cambia.
  const nivelPrevio = useRef(nivel);
  useEffect(() => {
    if (nivelPrevio.current === nivel) return;
    nivelPrevio.current = nivel;
    onChange(null);
    // onChange (field.onChange de RHF) es estable; excluirlo evita re-resetear.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nivel]);

  return nivel === NivelReorden.Sucursal ? (
    <SucursalSelector
      value={value}
      onChange={onChange}
      disabled={disabled}
      className={className}
    />
  ) : (
    <AlmacenSelector
      value={value}
      onChange={onChange}
      disabled={disabled}
      className={className}
    />
  );
}
