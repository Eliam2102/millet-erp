import { useMemo } from 'react';
import { CatalogoEagerCombobox } from '@/components/erp/selectors/CatalogoEagerCombobox';
import { UbicacionRutaItem } from '@/components/erp/selectors/UbicacionRutaItem';
import { rutaUbicacion } from '@/features/almacen/lib/ubicacion-label';
import { useAsignacionesList } from '@/features/almacen/api/useAsignaciones';
import { useUbicaciones } from '@/features/almacen/api/useUbicaciones';
import { useSaldosPorUbicacion } from '@/features/almacen/api/useSaldosCierreReportes';
import { EstatusCatalogo } from '@/features/almacen/api/types';

/**
 * <c>&lt;UbicacionBinSelector/&gt;</c> — selector inteligente de bin (rack N4)
 * por línea de movimiento (ADR-0047 C7.2b). UN componente, dos modos según la
 * fuente de datos:
 *
 * <list type="bullet">
 *   <item><c>asignacion</c> (ENTRADAS): las ubicaciones ASIGNADAS del artículo.
 *     El backend exige asignación activa y prohíbe la ÚNICA, así que la lista
 *     solo trae racks reales. Con <c>requiereSubAlmacen</c> (default) se acotan
 *     al sub-almacén; sin él, se listan TODAS las asignadas del artículo (PR
 *     recepción: el sub se deriva del bin, no se pide en cabecera).</item>
 *   <item><c>saldo</c> (SALIDAS): las ubicaciones con EXISTENCIA (&gt;0) del
 *     artículo, incluida la ÚNICA para agotar histórico. Con
 *     <c>requiereSubAlmacen</c> (default) se acotan al sub; sin él (salida-con-RQ)
 *     se listan de todos los subs y el sub se deriva del bin server-side.</item>
 * </list>
 *
 * <para>La etiqueta muestra la ruta de 3 niveles <c>"ALM › SUB · UBI"</c> en
 * ambos modos: en asignación los datos vienen de <c>UbicacionListItem</c>, en
 * saldo de <c>SaldoUbicacionItem</c> (enriquecido con las claves de los padres
 * desde C1). N1 (sucursal) diferido — si no llegan las claves, degrada a la clave.</para>
 */
export interface UbicacionBinSelectorProps {
  modo: 'asignacion' | 'saldo';
  articuloId: string | null | undefined;
  subAlmacenId: string | null | undefined;
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  disabled?: boolean;
  className?: string;
  /**
   * Si <c>true</c> (default), la lista se acota al <c>subAlmacenId</c> y el
   * selector va deshabilitado sin él (comportamiento histórico de todos los
   * call-sites). Si <c>false</c> (recepción y salida-con-RQ), se listan TODAS
   * las ubicaciones del artículo sin filtrar por sub — el sub se deriva del bin
   * server-side. Aplica a ambos modos (asignación y saldo).
   */
  requiereSubAlmacen?: boolean;
}

interface BinOption {
  id: string;
  clave: string;
  nombre: string;
  esDefault: boolean;
  /** Solo en modo saldo: existencia disponible en el bin. */
  cantidad?: number;
  /** Claves de los padres N2/N3 para la ruta. Presentes en ambos modos
   *  (asignación desde UbicacionListItem, saldo desde SaldoUbicacionItem tras
   *  C1); si faltan, la ruta degrada a la clave. */
  almacenClave?: string;
  subAlmacenClave?: string;
}

export function UbicacionBinSelector({
  modo,
  articuloId,
  subAlmacenId,
  value,
  onChange,
  disabled,
  className,
  requiereSubAlmacen = true,
}: UbicacionBinSelectorProps) {
  // El prop modula ambos modos: salida-con-RQ y recepción pasan false (sin sub
  // de cabecera, el bin deriva el sub); vale y devolución omiten el prop (true).
  const requiereSub = requiereSubAlmacen;
  const habilitado =
    !!articuloId && (!requiereSub || !!subAlmacenId) && !disabled;

  // ── Modo asignación (entradas) ──────────────────────────────────────────
  const asignacionesQuery = useAsignacionesList(
    modo === 'asignacion' && articuloId
      ? { articuloId, estatus: EstatusCatalogo.Activo, limit: 500 }
      : {},
  );
  const ubicacionesQuery = useUbicaciones(
    // requiereSub=true (salidas/dev-interna): idéntico al gate previo — solo
    // por subAlmacenId. requiereSub=false (recepción): lista TODAS las
    // ubicaciones, gateado por articuloId para no fetchear sin artículo.
    modo !== 'asignacion'
      ? {}
      : requiereSub
        ? subAlmacenId
          ? { subAlmacenId, limit: 500 }
          : {}
        : articuloId
          ? { limit: 500 }
          : {},
  );

  // ── Modo saldo (salidas) ────────────────────────────────────────────────
  const saldosQuery = useSaldosPorUbicacion(
    modo === 'saldo' ? articuloId : null,
    modo === 'saldo' ? subAlmacenId : null,
  );

  const items = useMemo<BinOption[]>(() => {
    if (!habilitado) return [];
    if (modo === 'saldo') {
      return (saldosQuery.data ?? []).map((s) => ({
        id: s.ubicacionId,
        clave: s.clave,
        nombre: s.nombre,
        esDefault: s.esDefault,
        cantidad: s.cantidad,
        // C1: el DTO de saldo ya trae la ruta de los padres → misma etiqueta
        // de 3 niveles que el modo asignación.
        almacenClave: s.almacenClave,
        subAlmacenClave: s.subAlmacenClave,
      }));
    }
    // Modo asignación: cruzar las asignaciones activas del artículo con las
    // ubicaciones cargadas (del sub, o todas si requiereSubAlmacen=false) para
    // la etiqueta + ruta.
    const ubicPorId = new Map(
      (ubicacionesQuery.data?.items ?? []).map((u) => [u.id, u]),
    );
    return (asignacionesQuery.data?.items ?? [])
      .map((a) => ubicPorId.get(a.ubicacionId))
      .filter((u): u is NonNullable<typeof u> => u != null)
      .map((u) => ({
        id: u.id,
        clave: u.clave,
        nombre: u.nombre,
        esDefault: u.esDefault,
        almacenClave: u.almacenClave,
        subAlmacenClave: u.subAlmacenClave,
      }));
  }, [
    habilitado,
    modo,
    saldosQuery.data,
    asignacionesQuery.data,
    ubicacionesQuery.data,
  ]);

  const loading =
    modo === 'saldo'
      ? saldosQuery.isLoading
      : asignacionesQuery.isLoading || ubicacionesQuery.isLoading;

  const error =
    modo === 'saldo'
      ? saldosQuery.error
      : asignacionesQuery.error ?? ubicacionesQuery.error;

  const placeholder = !habilitado
    ? requiereSub
      ? 'Elige artículo y sub-almacén primero'
      : 'Elige artículo primero'
    : 'Selecciona ubicación';

  const emptyListText =
    modo === 'saldo'
      ? 'Sin existencia disponible para este artículo.'
      : requiereSub
        ? 'El artículo no tiene ubicaciones asignadas en este sub-almacén.'
        : 'El artículo no tiene ubicaciones asignadas.';

  return (
    <CatalogoEagerCombobox<BinOption>
      items={items}
      loading={loading}
      error={error}
      value={value}
      onChange={onChange}
      itemToLabel={(u) =>
        `${rutaUbicacion({
          almacenClave: u.almacenClave,
          subAlmacenClave: u.subAlmacenClave,
          clave: u.clave,
        })} ${u.nombre}`
      }
      renderTrigger={(u) =>
        rutaUbicacion({
          almacenClave: u.almacenClave,
          subAlmacenClave: u.subAlmacenClave,
          clave: u.clave,
        })
      }
      renderItem={(u) => (
        <UbicacionRutaItem
          niveles={{
            almacenClave: u.almacenClave,
            subAlmacenClave: u.subAlmacenClave,
            clave: u.clave,
          }}
          nombre={u.nombre}
          adornos={
            <>
              {u.esDefault && (
                <span className="ml-2 rounded bg-sky-100 px-1 text-[10px] font-medium text-sky-800">
                  ÚNICA
                </span>
              )}
              {u.cantidad != null && (
                <span className="ml-2 text-muted-foreground">
                  · {u.cantidad} disp.
                </span>
              )}
            </>
          }
        />
      )}
      placeholder={placeholder}
      searchPlaceholder="Buscar ubicación…"
      emptyListText={emptyListText}
      ariaLabel="Seleccionar ubicación"
      disabled={!habilitado}
      className={className}
    />
  );
}
