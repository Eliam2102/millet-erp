import { useState } from 'react';
import { BuscadorInteligente } from '@/components/erp/selectors/BuscadorInteligente';
import { useClientesLookupCxc } from '@/features/cxc/api/useLineasCredito';
import type { ClienteLookupCxcItem } from '@/features/cxc/api/types';

/** Tope de resultados por búsqueda; si se alcanza se pide afinar (86e3tfdx8). */
const LIMITE_CLIENTES = 50;

/**
 * <c>&lt;ClienteSelectorCxc/&gt;</c> — combobox lazy contra
 * <c>GET /api/v1/cuentas-por-cobrar/clientes-lookup?q=</c> (CXC-FE-PR2).
 * Mismo patrón que <c>ClienteSelector</c> de Facturación (86e3tfdx8): UNA
 * sola caja (<c>&lt;BuscadorInteligente/&gt;</c>) — todas las palabras, en
 * cualquier orden y sin acentos, en razón social, RFC o clave; hasta 50
 * resultados. Gate del endpoint: <c>lineas-credito.leer</c>.
 *
 * <para><c>onChange</c> entrega el ITEM COMPLETO (no solo el id) para
 * que el caller muestre razón social/RFC sin otra query.</para>
 */
export interface ClienteSelectorCxcProps {
  value: string | null | undefined;
  onChange: (item: ClienteLookupCxcItem | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /** Etiqueta para value "frío" (id sin item conocido). Evita mostrar el
   * GUID crudo en el trigger. */
  initialLabel?: string;
}

export function ClienteSelectorCxc({
  value,
  onChange,
  placeholder = 'Buscar cliente…',
  disabled,
  className,
  initialLabel,
}: ClienteSelectorCxcProps) {
  const [termino, setTermino] = useState<string | undefined>();
  const clientesQuery = useClientesLookupCxc({ q: termino, limit: LIMITE_CLIENTES });
  const items = clientesQuery.data ?? [];

  return (
    <BuscadorInteligente<ClienteLookupCxcItem>
      value={value}
      onChange={onChange}
      onTerminoChange={setTermino}
      items={items}
      cargando={clientesQuery.isLoading || clientesQuery.isFetching}
      error={clientesQuery.isError ? clientesQuery.error : undefined}
      itemToLabel={(c) => `${c.clave} · ${c.razonSocial}`}
      valueLabel={initialLabel ?? (value ? 'Cliente seleccionado' : undefined)}
      renderItem={(c) => (
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="truncate font-mono text-xs">{c.clave}</span>
            <span className="text-xs text-ink-muted">{c.rfc ?? 'Sin RFC'}</span>
          </div>
          <p className="truncate text-sm">{c.razonSocial}</p>
        </div>
      )}
      placeholder={placeholder}
      ariaLabel="Seleccionar cliente"
      searchAriaLabel="Buscar cliente"
      searchPlaceholder="Buscar por nombre, RFC o clave…"
      emptyText="Escribe parte del nombre, el RFC o la clave del cliente."
      noMatchText={(t) => `Ningún cliente activo coincide con "${t}".`}
      pie={items.length >= LIMITE_CLIENTES ? 'Afina la búsqueda para ver más.' : undefined}
      disabled={disabled}
      className={className}
      popoverWidthClassName="w-[min(40rem,90vw)]"
    />
  );
}
