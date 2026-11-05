import type { ComponentProps } from 'react';
import { useUsuarios } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;UsuarioSelector/&gt;</c> — selector de usuario para
 * "requisitante delegado" (P4) y "designar aprobador" (P9). Doc 05
 * §11.3.
 *
 * <para>Permiso requerido: <c>identidad.usuarios.leer</c>. Si el
 * usuario actual no lo tiene, el hook no consulta el endpoint.
 * El caller debe esconder o deshabilitar el selector vía
 * <c>useHasPermission</c> para evitar ofrecer una lista vacía.</para>
 */
export interface UsuarioSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /** Props extra del trigger (id y aria-* que entrega `Field`). */
  triggerProps?: ComponentProps<'button'>;
  /** Nombre del usuario precargado mientras no está en la lista (sin GUID crudo). */
  initialLabel?: string;
}

export function UsuarioSelector({
  value,
  onChange,
  placeholder = 'Selecciona usuario',
  disabled,
  className,
  triggerProps,
  initialLabel,
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
          <p className="truncate text-xs text-ink-muted">{u.email}</p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar por nombre o email…"
      emptyListText="No hay usuarios activos en el catálogo."
      ariaLabel="Seleccionar usuario"
      disabled={disabled}
      className={className}
      triggerProps={triggerProps}
      initialLabel={initialLabel}
      popoverWidthClassName="w-[min(32rem,90vw)]"
    />
  );
}
