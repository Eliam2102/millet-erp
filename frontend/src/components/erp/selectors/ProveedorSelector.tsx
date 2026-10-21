import type { ComponentProps } from 'react';
import { useState } from 'react';
import { useProveedor, useProveedores } from '@/features/catalogos/api';
import type { ProveedorListItem } from '@/features/catalogos/api/types';
import { BuscadorInteligente } from './BuscadorInteligente';

/**
 * <c>&lt;ProveedorSelector/&gt;</c> — combobox lazy contra
 * <c>GET /api/v1/catalogos/proveedores?...&estatus=Activo</c>.
 * Doc 05 §11.3. Análogo a <c>&lt;ArticuloSelector/&gt;</c>: el item
 * muestra clave + razón social + nombre comercial (si difiere) + RFC.
 *
 * <para>UNA sola caja (<c>&lt;BuscadorInteligente/&gt;</c>, 86e3ufkgd):
 * coincidencia parcial en clave, RFC, razón social o nombre comercial
 * (insensible a acentos y mayúsculas), con debounce 300 ms y param
 * <c>q</c>.</para>
 *
 * <para>Filtra <c>estatus=Activo</c> automáticamente.</para>
 */
export interface ProveedorSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /** Props extra del trigger (id y aria-* que entrega `Field`). */
  triggerProps?: ComponentProps<'button'>;
  /**
   * Etiqueta inicial ("clave · razón social") del proveedor pre-seleccionado
   * al EDITAR, cuando <c>value</c> viene del DTO enriquecido. Permite mostrar
   * la etiqueta sin depender de la lista capada del typeahead (ADR-0042
   * addendum). En alta no se pasa.
   */
  initialLabel?: string;
}

export function ProveedorSelector({
  value,
  onChange,
  placeholder = 'Buscar proveedor…',
  disabled,
  className,
  triggerProps,
  initialLabel,
}: ProveedorSelectorProps) {
  const [termino, setTermino] = useState<string | undefined>();
  // Id elegido en esta sesión: su etiqueta ya la conserva el buscador.
  const [elegidoId, setElegidoId] = useState<string | null>(null);
  const proveedoresQuery = useProveedores({ q: termino, limit: 50 });
  const items = proveedoresQuery.data?.items ?? [];

  // Valor "frío": hay value que no se eligió aquí, no está en la página
  // actual y el caller no pasó initialLabel (típico: recargar con
  // ?proveedorId= en la URL). Se resuelve con un lookup puntual por id
  // (cacheado), solo cuando hace falta.
  const necesitaLookup =
    !!value &&
    value !== elegidoId &&
    !initialLabel &&
    !items.some((p) => p.id === value);
  const lookup = useProveedor(necesitaLookup ? value : null);

  return (
    <BuscadorInteligente<ProveedorListItem>
      value={value}
      onChange={(p) => {
        setElegidoId(p?.id ?? null);
        onChange(p?.id ?? null);
      }}
      onTerminoChange={setTermino}
      items={items}
      cargando={proveedoresQuery.isLoading || proveedoresQuery.isFetching}
      error={proveedoresQuery.isError ? proveedoresQuery.error : undefined}
      itemToLabel={(p) => `${p.clave} · ${p.razonSocial}`}
      valueLabel={
        lookup.data
          ? `${lookup.data.clave} · ${lookup.data.razonSocial}`
          : initialLabel
      }
      renderItem={(p) => (
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="truncate font-mono text-xs">{p.clave}</span>
            <span className="font-mono text-xs text-ink-muted">{p.rfc}</span>
          </div>
          <p className="truncate text-sm">{p.razonSocial}</p>
          {p.nombreComercial && p.nombreComercial !== p.razonSocial && (
            <p className="truncate text-xs text-ink-muted">
              {p.nombreComercial}
            </p>
          )}
        </div>
      )}
      placeholder={placeholder}
      ariaLabel="Seleccionar proveedor"
      searchAriaLabel="Buscar proveedor"
      searchPlaceholder="Buscar por clave, RFC o nombre…"
      emptyText="Empieza a escribir la clave, el RFC o el nombre del proveedor."
      noMatchText={(t) => `Ningún proveedor activo coincide con "${t}".`}
      disabled={disabled}
      className={className}
      triggerProps={triggerProps}
      popoverWidthClassName="w-[min(40rem,90vw)]"
    />
  );
}
