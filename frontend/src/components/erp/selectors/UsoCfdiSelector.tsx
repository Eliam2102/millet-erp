import { useMemo } from 'react';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useUsosCfdi } from '@/modules/catalogos/api';

/**
 * <c>&lt;UsoCfdiSelector/&gt;</c> — selector del catálogo SAT c_UsoCFDI
 * (seedeado local, read-only). Molde <c>MonedaSelector</c>: catálogo
 * corto eager sobre <c>CatalogoEagerCombobox</c> (FAC-DET-PR3, cierra el
 * diferido B16 — la emisión capturaba la clave como texto libre).
 *
 * <para><b>value/onChange operan sobre la CLAVE SAT</b> (<c>'G03'</c>,
 * <c>'S01'</c>, …), no sobre el id — la emisión guarda la clave string.
 * Cold value: el trigger muestra la clave cruda hasta que carga la
 * lista.</para>
 */
interface UsoCfdiOpcion {
  /** = clave SAT; el combobox keyea por este campo. */
  id: string;
  claveSat: string;
  descripcion: string;
}

export interface UsoCfdiSelectorProps {
  /** Clave SAT seleccionada (G01, G03, S01, …) o null. */
  value: string | null | undefined;
  onChange: (claveSat: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function UsoCfdiSelector({
  value,
  onChange,
  placeholder = 'Selecciona uso CFDI',
  disabled,
  className,
}: UsoCfdiSelectorProps) {
  const query = useUsosCfdi();
  const items = useMemo<UsoCfdiOpcion[]>(
    () =>
      (query.data ?? [])
        .filter((u) => u.activa)
        .map((u) => ({
          id: u.claveSat,
          claveSat: u.claveSat,
          descripcion: u.descripcion,
        })),
    [query.data],
  );

  return (
    <CatalogoEagerCombobox<UsoCfdiOpcion>
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(u) => `${u.claveSat} ${u.descripcion}`}
      renderTrigger={(u) => `${u.claveSat} · ${u.descripcion}`}
      renderItem={(u) => (
        <div className="min-w-0 flex-1">
          <span className="font-mono text-xs">{u.claveSat}</span>
          <p className="truncate text-sm">{u.descripcion}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar uso CFDI…"
      emptyListText="No hay usos CFDI en el catálogo."
      ariaLabel="Seleccionar uso CFDI"
      disabled={disabled}
      className={className}
    />
  );
}
