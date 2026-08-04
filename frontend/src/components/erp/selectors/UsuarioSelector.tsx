import { useUsuarios } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;UsuarioSelector/&gt;</c> — selector de usuario para
 * "requisitante delegado" (P4) y "designar aprobador" (P9). Doc 05
 * §11.3.
 *
 * <para>Permiso requerido: <c>identidad.usuarios.leer</c>. Si el
 * usuario actual no lo tiene, el endpoint devuelve 403 y el selector
 * muestra empty list — el caller debería esconder el selector vía
 * <c>useHasPermission</c> antes de renderizarlo (no esperar a que
 * falle el endpoint).</para>
 */
export interface UsuarioSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function UsuarioSelector({
  value,
  onChange,
  placeholder = 'Selecciona usuario',
  disabled,
  className,
}: UsuarioSelectorProps) {
  const query = useUsuarios();
  // Filtramos client-side a activos: los selectores solo deben permitir
  // asignar a usuarios vigentes. Inactivos siguen visibles en
  // listados de auditoría/admin pero no acá.
  const items = (query.data?.items ?? []).filter((u) => u.activo);

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(u) => `${u.nombre} ${u.email}`}
      renderTrigger={(u) => u.nombre}
      renderItem={(u) => (
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium">{u.nombre}</p>
          <p className="truncate text-xs text-muted-foreground">{u.email}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar por nombre o email…"
      emptyListText="No hay usuarios activos en el catálogo."
      ariaLabel="Seleccionar usuario"
      disabled={disabled}
      className={className}
      popoverWidthClassName="w-[min(32rem,90vw)]"
    />
  );
}
