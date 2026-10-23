import { BuscadorInteligente } from '@/components/erp/selectors/BuscadorInteligente';
import { Badge } from '@/components/ui/badge';
import type { ClienteLookupItem } from '@/features/facturacion/api/types';
import { useClientesLookup } from '@/features/facturacion/api/useCatalogosFacturacion';
import { useState } from 'react';

/** Tope de resultados por búsqueda; si se alcanza se pide afinar (86e3tfdx8). */
export const LIMITE_CLIENTES = 50;

/**
 * <c>&lt;ClienteSelector/&gt;</c> — combobox lazy contra
 * <c>GET /api/v1/facturacion/catalogos/clientes?q=</c> (FAC-UX-PR4).
 * 86e3tfdx8: UNA sola caja (<c>&lt;BuscadorInteligente/&gt;</c>) — todas
 * las palabras, en cualquier orden y sin acentos, en razón social, RFC o
 * clave («carlos perez» encuentra «PÉREZ GARCÍA CARLOS»); hasta 50
 * resultados, primero los que empiezan con la primera palabra.
 *
 * <para><c>onChange</c> entrega el ITEM COMPLETO (no solo el id) para que
 * el caller autollene los datos fiscales del receptor (RFC, régimen, CP,
 * uso CFDI/método/forma default). Los items sin datos fiscales completos
 * se marcan (gap G12).</para>
 */
export interface ClienteSelectorProps {
  value: string | null | undefined;
  onChange: (item: ClienteLookupItem | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /** Etiqueta para value "frío" (id sin item conocido), p.ej. razón social
   * del DTO. Evita mostrar el GUID crudo en el trigger. */
  initialLabel?: string;
}

export function ClienteSelector({
  value,
  onChange,
  placeholder = 'Buscar cliente…',
  disabled,
  className,
  initialLabel,
}: ClienteSelectorProps) {
  const [termino, setTermino] = useState<string | undefined>();
  const clientesQuery = useClientesLookup({ q: termino, limit: LIMITE_CLIENTES });
  const items = clientesQuery.data ?? [];

  return (
    <BuscadorInteligente<ClienteLookupItem>
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
            {!c.datosFiscalesCompletos && <Badge variant="warning">Fiscales incompletos</Badge>}
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
