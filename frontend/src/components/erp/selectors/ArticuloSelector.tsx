import type { ComponentProps } from 'react';
import { useState } from 'react';
import { NaturalezaBadge } from '@/components/erp/display/NaturalezaBadge';
import { useArticulos } from '@/features/catalogos/api';
import type { ArticuloListItem } from '@/features/catalogos/api/types';
import { BuscadorInteligente } from './BuscadorInteligente';

/**
 * <c>&lt;ArticuloSelector/&gt;</c> — combobox lazy contra
 * <c>GET /api/v1/catalogos/articulos?q=...&estatus=Activo</c>.
 * Doc 05 §11.3.
 *
 * <para>UNA sola caja (<c>&lt;BuscadorInteligente/&gt;</c>, 86e3ufkgd):
 * coincidencia parcial en clave o nombre, insensible a acentos y
 * mayúsculas, con debounce 300 ms y param <c>q</c>. Sustituye las dos cajas
 * excluyentes de ADR-0045.</para>
 *
 * <para>Cada item muestra <c>clave</c> + <c>nombre</c> +
 * <c>&lt;NaturalezaBadge/&gt;</c> + UM default. Filtra
 * <c>estatus=Activo</c> automáticamente con <c>limit=50</c>.</para>
 *
 * <para>API agnóstica de <c>react-hook-form</c>: recibe <c>value</c> +
 * <c>onChange</c> tipo controlled. El <c>&lt;Controller/&gt;</c> de
 * RHF lo wirea en formularios.</para>
 */
export interface ArticuloSelectorProps {
  /** Id del artículo seleccionado, o <c>null</c>/<c>undefined</c>. */
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /**
   * Callback aditivo: se dispara con el artículo completo al
   * seleccionarlo (NO al deseleccionar). Permite al caller heredar
   * atributos del catálogo —ej. <c>unidadMedidaDefault</c>— sin re-fetch.
   * Opcional: los consumidores que solo necesitan el id (vía
   * <c>onChange</c>) no se ven afectados.
   */
  onSelect?: (articulo: ArticuloListItem) => void;
  /** Texto placeholder cuando no hay selección. */
  placeholder?: string;
  /** Deshabilita el trigger (read-only mode). */
  disabled?: boolean;
  /** Clase del trigger. */
  className?: string;
  /** Props extra del trigger (id y aria-* que entrega `Field`). */
  triggerProps?: ComponentProps<'button'>;
  /**
   * Etiqueta inicial ("clave · nombre") del artículo pre-seleccionado al
   * EDITAR, cuando <c>value</c> viene del DTO enriquecido. Permite mostrar
   * la etiqueta sin depender de que el item esté en la lista capada del
   * typeahead (ADR-0042 addendum). En alta no se pasa.
   */
  initialLabel?: string;
  /**
   * Clasificación de la requisición (0–3). Si viene, sólo se ofrecen los
   * artículos de ese tipo (86e3tfedd). Sin ella, el catálogo completo.
   */
  tipoRequisicion?: number;
}

export function ArticuloSelector({
  value,
  onChange,
  onSelect,
  placeholder = 'Buscar artículo…',
  disabled,
  className,
  triggerProps,
  initialLabel,
  tipoRequisicion,
}: ArticuloSelectorProps) {
  const [termino, setTermino] = useState<string | undefined>();
  const articulosQuery = useArticulos({ q: termino, tipoRequisicion, limit: 50 });

  return (
    <BuscadorInteligente<ArticuloListItem>
      value={value}
      onChange={(a) => {
        onChange(a?.id ?? null);
        // onSelect solo al seleccionar en firme (no al deseleccionar), para
        // que el caller herede atributos del artículo recién elegido.
        if (a) onSelect?.(a);
      }}
      onTerminoChange={setTermino}
      items={articulosQuery.data?.items ?? []}
      cargando={articulosQuery.isLoading || articulosQuery.isFetching}
      error={articulosQuery.isError ? articulosQuery.error : undefined}
      itemToLabel={(a) => `${a.clave} · ${a.nombre}`}
      valueLabel={initialLabel}
      renderItem={(a) => (
        <>
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <span className="truncate font-mono text-xs">{a.clave}</span>
              <span className="text-xs text-ink-muted">
                {a.unidadMedidaDefault}
              </span>
            </div>
            <p className="truncate text-sm">{a.nombre}</p>
          </div>
          <NaturalezaBadge naturaleza={a.naturaleza} />
        </>
      )}
      placeholder={placeholder}
      ariaLabel="Seleccionar artículo"
      searchAriaLabel="Buscar artículo"
      searchPlaceholder="Buscar por clave o nombre…"
      emptyText={
        tipoRequisicion != null
          ? 'No hay artículos dados de alta para este tipo de requisición.'
          : 'Empieza a escribir la clave o el nombre del artículo.'
      }
      noMatchText={(t) => `Ningún artículo activo coincide con "${t}".`}
      disabled={disabled}
      className={className}
      triggerProps={triggerProps}
    />
  );
}
