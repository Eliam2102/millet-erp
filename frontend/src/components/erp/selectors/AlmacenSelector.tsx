import { useAlmacenes } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;AlmacenSelector/&gt;</c> — selector de almacén destino. Doc
 * 05 §11.3. Acepta filtro <c>sucursalId</c> opcional para limitar a
 * los almacenes de una sucursal (dropdown dependiente "primero
 * sucursal, después almacén").
 */
export interface AlmacenSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /**
   * Si se pasa, filtra la lista a almacenes de esta sucursal. Cuando
   * cambia, el caller debería resetear <c>value</c> a <c>null</c>
   * para no mantener un id de almacén que ya no aplica.
   */
  sucursalId?: string;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function AlmacenSelector({
  value,
  onChange,
  sucursalId,
  placeholder = 'Selecciona almacén',
  disabled,
  className,
}: AlmacenSelectorProps) {
  const query = useAlmacenes(sucursalId);
  const items = query.data?.items ?? [];

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(a) => `${a.clave} ${a.nombre}`}
      renderTrigger={(a) => `${a.clave} · ${a.nombre}`}
      renderItem={(a) => (
        <div className="min-w-0 flex-1">
          <span className="truncate font-mono text-xs">{a.clave}</span>
          <p className="truncate text-sm">{a.nombre}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar almacén…"
      emptyListText={
        sucursalId
          ? 'Esta sucursal no tiene almacenes registrados.'
          : 'No hay almacenes en el catálogo.'
      }
      ariaLabel="Seleccionar almacén"
      disabled={disabled}
      className={className}
    />
  );
}
