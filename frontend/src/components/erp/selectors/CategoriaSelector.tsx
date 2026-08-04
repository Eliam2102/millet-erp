import { useMemo } from 'react';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useCategoriasArticuloList } from '@/modules/catalogos/api';
import { EstatusCatalogo } from '@/modules/catalogos/api/types';

/**
 * <c>&lt;CategoriaSelector/&gt;</c> — selector del catálogo de categorías de
 * artículo (<c>compartido.categorias_articulo</c>, ADR-0046 PR2). Molde
 * <c>MonedaSelector</c> (sobre <c>CatalogoEagerCombobox</c>), pero
 * <b>value/onChange = id (GUID) real</b>: aquí el FK es real (a diferencia de
 * Moneda, que guardaba el código), así que no hace falta el adapter id=código.
 *
 * <para><b>Cold value</b>: como el value es un GUID, sin ayuda el trigger
 * mostraría el GUID crudo mientras carga el catálogo. El read DTO del artículo
 * trae <c>categoria</c> (nombre) además de <c>categoriaId</c>; se pasa como
 * <c>initialLabel</c> para mostrar el nombre legible hasta que llegue la lista
 * (patrón <c>ProveedorSelector</c>).</para>
 */
export interface CategoriaSelectorProps {
  /** Id (GUID) de la categoría seleccionada, o null. */
  value: string | null | undefined;
  onChange: (categoriaId: string | null) => void;
  /** Nombre para el cold value (el <c>categoria</c> del read DTO). */
  initialLabel?: string | null;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function CategoriaSelector({
  value,
  onChange,
  initialLabel,
  placeholder = 'Selecciona categoría',
  disabled,
  className,
}: CategoriaSelectorProps) {
  const query = useCategoriasArticuloList();
  const items = useMemo(
    () => (query.data ?? []).filter((c) => c.estatus === EstatusCatalogo.Activo),
    [query.data],
  );

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      initialLabel={initialLabel}
      itemToLabel={(c) => c.nombre}
      renderTrigger={(c) => c.nombre}
      renderItem={(c) => (
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm">{c.nombre}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar categoría…"
      emptyListText="No hay categorías en el catálogo."
      ariaLabel="Seleccionar categoría"
      disabled={disabled}
      className={className}
    />
  );
}
