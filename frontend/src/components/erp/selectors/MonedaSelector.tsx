import { useMemo } from 'react';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useMonedas } from '@/modules/catalogos/api';

/**
 * <c>&lt;MonedaSelector/&gt;</c> — selector del catálogo de monedas
 * (<c>compartido.monedas</c>, ADR-0014). Mismo molde que
 * <c>IncotermSelector</c> / <c>RegimenFiscalSelector</c>: catálogo corto,
 * sin paginación, sobre <c>CatalogoEagerCombobox</c>, render
 * <c>"codigo · nombre"</c>.
 *
 * <para><b>value/onChange operan sobre el CÓDIGO ISO</b> (<c>'MXN'</c>, …),
 * no sobre el id: los consumidores guardan el código string (p.ej.
 * <c>Articulo.PrecioReferenciaMoneda</c>, validado server-side contra el
 * catálogo). Para encajar con <c>CatalogoEagerCombobox</c> —que keyea por
 * <c>item.id</c>— se adapta cada moneda a <c>{ id: codigo, codigo, nombre }</c>,
 * de modo que el "id" del combobox <b>es</b> el código. El cold value lo
 * resuelve el propio combobox: si el catálogo aún no carga, el trigger cae a
 * <c>value ?? placeholder</c> (muestra el código crudo) y lo mejora a
 * <c>"MXN · Peso Mexicano"</c> al llegar la lista.</para>
 */
interface MonedaItem {
  /** = código ISO; el combobox keyea por este campo. */
  id: string;
  codigo: string;
  nombre: string;
}

export interface MonedaSelectorProps {
  /** Código ISO seleccionado (MXN, USD, …) o null. */
  value: string | null | undefined;
  onChange: (codigo: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function MonedaSelector({
  value,
  onChange,
  placeholder = 'Selecciona moneda',
  disabled,
  className,
}: MonedaSelectorProps) {
  const query = useMonedas();
  const items = useMemo<MonedaItem[]>(
    () =>
      (query.data ?? [])
        .filter((m) => m.activa)
        .map((m) => ({ id: m.codigo, codigo: m.codigo, nombre: m.nombre })),
    [query.data],
  );

  return (
    <CatalogoEagerCombobox<MonedaItem>
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(m) => `${m.codigo} ${m.nombre}`}
      renderTrigger={(m) => `${m.codigo} · ${m.nombre}`}
      renderItem={(m) => (
        <div className="min-w-0 flex-1">
          <span className="font-mono text-xs">{m.codigo}</span>
          <p className="truncate text-sm">{m.nombre}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar moneda…"
      emptyListText="No hay monedas en el catálogo."
      ariaLabel="Seleccionar moneda"
      disabled={disabled}
      className={className}
    />
  );
}
