import { useMemo } from 'react';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useFormasPago } from '@/modules/catalogos/api';

/**
 * <c>&lt;FormaPagoSelector/&gt;</c> — selector del catálogo SAT
 * c_FormaPago (seedeado local, read-only). Molde <c>MonedaSelector</c>
 * (FAC-DET-PR3, cierra el diferido B16 — la emisión capturaba la clave
 * como texto libre).
 *
 * <para><b>value/onChange operan sobre la CLAVE SAT</b> (<c>'01'</c>
 * efectivo, <c>'03'</c> transferencia, …), no sobre el id.</para>
 */
interface FormaPagoOpcion {
  /** = clave SAT; el combobox keyea por este campo. */
  id: string;
  claveSat: string;
  descripcion: string;
}

export interface FormaPagoSelectorProps {
  /** Clave SAT seleccionada (01, 03, 04, 99, …) o null. */
  value: string | null | undefined;
  onChange: (claveSat: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function FormaPagoSelector({
  value,
  onChange,
  placeholder = 'Selecciona forma de pago',
  disabled,
  className,
}: FormaPagoSelectorProps) {
  const query = useFormasPago();
  const items = useMemo<FormaPagoOpcion[]>(
    () =>
      (query.data ?? [])
        .filter((f) => f.activa)
        .map((f) => ({
          id: f.claveSat,
          claveSat: f.claveSat,
          descripcion: f.descripcion,
        })),
    [query.data],
  );

  return (
    <CatalogoEagerCombobox<FormaPagoOpcion>
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(f) => `${f.claveSat} ${f.descripcion}`}
      renderTrigger={(f) => `${f.claveSat} · ${f.descripcion}`}
      renderItem={(f) => (
        <div className="min-w-0 flex-1">
          <span className="font-mono text-xs">{f.claveSat}</span>
          <p className="truncate text-sm">{f.descripcion}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar forma de pago…"
      emptyListText="No hay formas de pago en el catálogo."
      ariaLabel="Seleccionar forma de pago"
      disabled={disabled}
      className={className}
    />
  );
}
