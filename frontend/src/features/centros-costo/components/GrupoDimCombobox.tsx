import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { useListaCatalogo } from '@/features/centros-costo/api/useCatalogoCrud';
import {
  EstatusCatalogo,
  type GrupoDimDetalle,
} from '@/features/centros-costo/api/types';

/**
 * Selector de grupo de clasificación (CECO — fix de UX del modal). Los
 * grupos son catálogos BUSCABLES (grupos_dim3 son 44 → un Select desborda
 * la pantalla sin buscador; 05 §7.1: "qué contiene, no dónde vive"), así
 * que se usa el combobox ⇅ (`CatalogoEagerCombobox`, ≤200 items,
 * client-side). Molde de uso dentro de un Dialog: `DesignarAprobadorDialog`
 * — Popover anidado en Dialog probado en el repo (el focus-trap de Radix
 * no pelea). El MISMO control para dim2 (6) y dim3 (44): la diferencia de
 * conteo es implementación, no le incumbe al usuario.
 *
 * <para>Carga TODOS los grupos (no solo activos): así, en edición, un
 * nodo que referencia un grupo ya inactivo muestra su NOMBRE (no un GUID)
 * — el item inactivo se marca; asignar a uno inactivo lo rechaza el
 * backend (CECO_GRUPO_DIM*_INVALIDO).</para>
 */
interface GrupoDimComboboxProps {
  recurso: 'grupos-dim2' | 'grupos-dim3';
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  disabled?: boolean;
}

export function GrupoDimCombobox({
  recurso,
  value,
  onChange,
  disabled,
}: GrupoDimComboboxProps) {
  const query = useListaCatalogo<GrupoDimDetalle>(recurso, { limit: 200 });
  const items = query.data?.items ?? [];

  return (
    <CatalogoEagerCombobox
      items={items}
      loading={query.isLoading}
      // Muestra el error real de la query (p. ej. 403) en vez de
      // "catálogo vacío" — patrón del barrido #655.
      error={query.error}
      value={value}
      onChange={onChange}
      itemToLabel={(g) => g.nombre}
      renderTrigger={(g) =>
        g.estatus === EstatusCatalogo.Inactivo
          ? `${g.nombre} (inactivo)`
          : g.nombre
      }
      renderItem={(g) => (
        <div className="min-w-0 flex-1">
          <span className="truncate text-sm">{g.nombre}</span>
          {g.estatus === EstatusCatalogo.Inactivo && (
            <span className="ml-1 text-xs text-muted-foreground">(inactivo)</span>
          )}
        </div>
      )}
      placeholder="Selecciona un grupo"
      searchPlaceholder="Buscar grupo…"
      emptyListText="No hay grupos en el catálogo."
      ariaLabel="Seleccionar grupo"
      disabled={disabled}
    />
  );
}
