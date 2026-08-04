import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useTarjetas } from '@/features/cxp/api/useTarjetasCredito';
import { EstadoTarjeta } from '@/features/cxp/api/types';

/**
 * <c>&lt;TarjetaSelector/&gt;</c> — combobox eager sobre el catálogo de
 * tarjetas de crédito empresariales del módulo CxP
 * (<c>GET /cuentas-por-pagar/tarjetas</c>, ≤ decenas de items). Muestra
 * alias + número enmascarado + emisora; reemplaza los inputs de GUID
 * crudo en forms y filtros de TC.
 *
 * <para>Local a CxP (no vive en <c>components/erp/selectors</c>) porque
 * el catálogo es propio del módulo, igual que <c>useTarjetas</c>.</para>
 */
export interface TarjetaSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /**
   * En captura solo tiene sentido elegir tarjetas activas (default).
   * Los filtros de bandeja pasan <c>false</c> para poder consultar
   * movimientos históricos de tarjetas bloqueadas/canceladas.
   */
  soloActivas?: boolean;
}

export function TarjetaSelector({
  value,
  onChange,
  placeholder = 'Selecciona tarjeta',
  disabled,
  className,
  soloActivas = true,
}: TarjetaSelectorProps) {
  const query = useTarjetas();
  const items = (query.data?.items ?? []).filter(
    (t) => !soloActivas || t.estado === EstadoTarjeta.Activa,
  );

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(t) => `${t.nombreAlias} ${t.numeroEnmascarado} ${t.emisora}`}
      renderTrigger={(t) => `${t.nombreAlias} · ${t.numeroEnmascarado}`}
      renderItem={(t) => (
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium">{t.nombreAlias}</p>
          <p className="truncate text-xs text-muted-foreground">
            {t.numeroEnmascarado} · {t.emisora}
          </p>
        </div>
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar por alias, número o emisora…"
      emptyListText="No hay tarjetas en el catálogo."
      ariaLabel="Seleccionar tarjeta"
      disabled={disabled}
      className={className}
      popoverWidthClassName="w-[min(32rem,90vw)]"
    />
  );
}
