import { useTransportistas } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;TransportistaSelector/&gt;</c> — selector de transportistas
 * (F9-PR1). Cross-módulo: OC (informacion-logistica), CxP (recibos
 * de flete), Recepción (asociar guía).
 *
 * <para>Si el transportista no está en el catálogo, el caller debe
 * ofrecer un campo de texto libre <c>transportistaTexto</c> como
 * fallback (patrón doc 05 §11.3).</para>
 */
export interface TransportistaSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function TransportistaSelector({
  value,
  onChange,
  placeholder = 'Selecciona transportista',
  disabled,
  className,
}: TransportistaSelectorProps) {
  const query = useTransportistas();
  const items = query.data ?? [];

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(t) => `${t.clave} ${t.nombre}`}
      renderTrigger={(t) => `${t.clave} · ${t.nombre}`}
      renderItem={(t) => (
        <div className="min-w-0 flex-1">
          <span className="font-mono text-xs">{t.clave}</span>
          <p className="truncate text-sm">{t.nombre}</p>
          {t.email && (
            <p className="truncate text-xs text-muted-foreground">{t.email}</p>
          )}
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar transportista…"
      emptyListText="No hay transportistas en el catálogo."
      ariaLabel="Seleccionar transportista"
      disabled={disabled}
      className={className}
    />
  );
}
