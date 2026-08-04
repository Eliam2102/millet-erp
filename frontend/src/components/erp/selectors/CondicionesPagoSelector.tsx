import { useCondicionesPago } from '@/features/catalogos/api';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';

/**
 * <c>&lt;CondicionesPagoSelector/&gt;</c> — selector de condiciones de
 * pago (F9-PR1: CONTADO, 15D, 30D, 45D, 60D, 90D, 120D). Cross-módulo:
 * OC (cabecera), CxP (programación), Cuentas por Cobrar (espejo).
 */
export interface CondicionesPagoSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function CondicionesPagoSelector({
  value,
  onChange,
  placeholder = 'Selecciona condiciones',
  disabled,
  className,
}: CondicionesPagoSelectorProps) {
  const query = useCondicionesPago();
  const items = query.data ?? [];

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(c) => `${c.clave} ${c.nombre}`}
      renderTrigger={(c) => `${c.clave} · ${c.nombre}`}
      renderItem={(c) => (
        <div className="min-w-0 flex-1">
          <span className="font-mono text-xs">{c.clave}</span>
          <p className="truncate text-sm">
            {c.nombre}
            {c.diasCredito > 0 && (
              <span className="ml-1 text-xs text-muted-foreground">
                ({c.diasCredito} días)
              </span>
            )}
          </p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar condiciones…"
      emptyListText="No hay condiciones de pago en el catálogo."
      ariaLabel="Seleccionar condiciones de pago"
      disabled={disabled}
      className={className}
    />
  );
}
