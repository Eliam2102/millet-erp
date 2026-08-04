import { useEffect } from 'react';
import { useCanalesVenta } from '@/features/facturacion/api/useCatalogosFacturacion';

/**
 * <c>&lt;CanalVentaSelector/&gt;</c> — select nativo alimentado por el
 * catálogo administrable <c>compartido.canales_venta</c> vía
 * <c>GET /facturacion/catalogos/canales-venta</c> (FAC-ING-PR3; reemplaza
 * las opciones hardcodeadas del enum retirado en FAC-ING-PR2).
 *
 * <para>Normalización del default: si el valor actual no existe en el
 * catálogo (p. ej. el default histórico id 1 fue desactivado) y NO hay
 * <c>etiquetaValorActual</c>, al cargar el lookup se corrige al primer
 * canal activo. Con <c>etiquetaValorActual</c> (edición/prefill de un
 * documento cuyo canal ya no está activo) el valor se CONSERVA y se
 * muestra como opción extra — el backend acepta ids inactivos solo en
 * lectura; al re-enviar validará y devolverá 422 si aplica.</para>
 */
export interface CanalVentaSelectorProps {
  value: number | null;
  onChange: (id: number) => void;
  /** Nombre a mostrar cuando `value` no está en el lookup (canal
   * desactivado de un documento histórico). Sin él, un valor fuera de
   * catálogo se normaliza al primer canal activo. */
  etiquetaValorActual?: string | null;
  disabled?: boolean;
}

export function CanalVentaSelector({
  value,
  onChange,
  etiquetaValorActual,
  disabled,
}: CanalVentaSelectorProps) {
  const query = useCanalesVenta();
  const canales = query.data;

  const valorEnCatalogo =
    value != null && (canales?.some((c) => c.id === value) ?? false);

  useEffect(() => {
    if (canales == null || canales.length === 0) return;
    const debeNormalizar =
      value == null ||
      (!canales.some((c) => c.id === value) && etiquetaValorActual == null);
    if (debeNormalizar) onChange(canales[0].id);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canales]);

  if (query.isError) {
    return (
      <div className="flex items-center gap-2">
        <select
          disabled
          aria-label="Canal de venta"
          className="h-9 w-full rounded-md border border-destructive/50 bg-transparent px-3 text-sm shadow-sm"
        >
          <option>Error al cargar canales</option>
        </select>
        <button
          type="button"
          onClick={() => query.refetch()}
          className="text-xs font-medium text-primary hover:underline"
        >
          Reintentar
        </button>
      </div>
    );
  }

  return (
    <select
      aria-label="Canal de venta"
      className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
      disabled={disabled || query.isLoading}
      value={value ?? ''}
      onChange={(e) => onChange(Number(e.target.value))}
    >
      {query.isLoading && <option value="">Cargando canales…</option>}
      {!query.isLoading && value != null && !valorEnCatalogo && (
        <option value={value}>
          {etiquetaValorActual ?? `Canal ${value}`} (inactivo)
        </option>
      )}
      {(canales ?? []).map((c) => (
        <option key={c.id} value={c.id}>
          {c.nombre}
        </option>
      ))}
    </select>
  );
}
