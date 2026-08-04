import { useMemo } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { ArticuloSelector, EmptyState } from '@/components/erp';
import { ArbolSaldos } from '@/features/almacen/components/ArbolSaldos';
import { useAlmacenes, useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import type { NivelNodoJerarquia } from '@/features/almacen/api/types';
import type { SaldosJerarquiaSearch } from '@/features/almacen/lib/saldos-jerarquia-search-schema';

const FROM = '/_app/almacen/saldos-jerarquia' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>Consulta jerárquica</c> (PR6, ADR-0047) — la cara del área de almacén:
 * saldos desplegados Sucursal → Almacén → Sub-almacén → Rack con rollup por
 * nodo, en árbol lazy. Dos modos sobre la misma vista: por ARTÍCULO (su
 * distribución por niveles) y por UBICACIÓN (browse desde la raíz; el rack
 * expande a sus artículos). Toggle "incluir vacíos" muestra la fila-en-0
 * (racks asignados sin saldo). Gateada por <c>almacen.almacenes.leer</c>.
 */
export function SaldosJerarquiaPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const modo = search.modo ?? 'ubicacion';

  const almacenesQuery = useAlmacenes({ limit: 500 });
  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });
  const almacenes = almacenesQuery.data?.items ?? [];
  // Sub-almacenes del almacén elegido (client-side; catálogo chico).
  const subAlmacenes = useMemo(() => {
    const items = subAlmacenesQuery.data?.items ?? [];
    return search.almacenId
      ? items.filter((s) => s.almacenId === search.almacenId)
      : items;
  }, [subAlmacenesQuery.data, search.almacenId]);

  function actualizarSearch(parcial: Partial<SaldosJerarquiaSearch>) {
    navigate({
      to: '/almacen/saldos-jerarquia',
      search: { ...search, ...parcial },
    });
  }

  // Punto de entrada del árbol: modo artículo arranca en la raíz filtrada;
  // modo ubicación arranca donde apunte el atajo (o la raíz sin selección).
  const entrada: { nodoTipo: NivelNodoJerarquia; nodoId?: string } =
    modo === 'ubicacion' && search.subAlmacenId
      ? { nodoTipo: 'subAlmacen', nodoId: search.subAlmacenId }
      : modo === 'ubicacion' && search.almacenId
        ? { nodoTipo: 'almacen', nodoId: search.almacenId }
        : { nodoTipo: 'raiz' };

  const arbolListo = modo === 'ubicacion' || !!search.articuloId;

  return (
    <div className="space-y-4">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">
          Consulta jerárquica
        </h1>
        <p className="text-sm text-muted-foreground">
          Saldos por sucursal → almacén → sub-almacén → rack, con rollup por
          nivel. El árbol carga cada nivel al expandirlo.
        </p>
      </header>

      <div className="flex flex-wrap items-end gap-3">
        <Tabs
          value={modo}
          onValueChange={(v) =>
            actualizarSearch({
              modo: v as SaldosJerarquiaSearch['modo'],
              // Cambiar de modo limpia la entrada del otro modo.
              articuloId: undefined,
              almacenId: undefined,
              subAlmacenId: undefined,
            })
          }
        >
          <TabsList>
            <TabsTrigger value="ubicacion">Por ubicación</TabsTrigger>
            <TabsTrigger value="articulo">Por artículo</TabsTrigger>
          </TabsList>
        </Tabs>

        {modo === 'articulo' ? (
          <div className="space-y-1">
            <label className="text-xs text-muted-foreground">Artículo</label>
            <ArticuloSelector
              value={search.articuloId ?? null}
              onChange={(id) => actualizarSearch({ articuloId: id ?? undefined })}
              placeholder="Buscar artículo"
              className="w-72"
            />
          </div>
        ) : (
          <>
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground">Almacén</label>
              <Select
                value={search.almacenId ?? SENTINEL_ALL}
                onValueChange={(v) =>
                  actualizarSearch({
                    almacenId: v === SENTINEL_ALL ? undefined : v,
                    subAlmacenId: undefined,
                  })
                }
              >
                <SelectTrigger aria-label="Saltar a un almacén" className="w-56">
                  <SelectValue placeholder="Todos" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={SENTINEL_ALL}>
                    Todos (desde la raíz)
                  </SelectItem>
                  {almacenes.map((a) => (
                    <SelectItem key={a.id} value={a.id}>
                      {a.clave} · {a.nombre}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground">
                Sub-almacén
              </label>
              <Select
                value={search.subAlmacenId ?? SENTINEL_ALL}
                onValueChange={(v) =>
                  actualizarSearch({
                    subAlmacenId: v === SENTINEL_ALL ? undefined : v,
                  })
                }
              >
                <SelectTrigger
                  aria-label="Saltar a un sub-almacén"
                  className="w-56"
                >
                  <SelectValue placeholder="Todos" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
                  {subAlmacenes.map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.clave} · {s.nombre}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </>
        )}

        <div className="flex items-center gap-2 pb-1">
          <input
            id="incluir-vacios"
            type="checkbox"
            checked={search.incluirVacios ?? false}
            onChange={(e) =>
              actualizarSearch({ incluirVacios: e.target.checked || undefined })
            }
            className="h-4 w-4"
          />
          <label htmlFor="incluir-vacios" className="text-sm">
            Incluir vacíos
          </label>
        </div>

        {(search.articuloId || search.almacenId || search.subAlmacenId) && (
          <Button
            variant="ghost"
            onClick={() =>
              actualizarSearch({
                articuloId: undefined,
                almacenId: undefined,
                subAlmacenId: undefined,
              })
            }
          >
            Limpiar
          </Button>
        )}
      </div>

      {arbolListo ? (
        <div className="rounded-md border bg-card p-2">
          {/* Encabezado slim alineado con las columnas del árbol. */}
          <div className="flex items-center gap-2 border-b px-2 pb-1 text-xs text-muted-foreground">
            <span>Nodo</span>
            <span className="ml-auto">Cantidad</span>
            <span className="w-32 text-right">Valor</span>
          </div>
          <ArbolSaldos
            key={`${modo}-${entrada.nodoTipo}-${entrada.nodoId ?? 'raiz'}-${search.articuloId ?? ''}-${search.incluirVacios ?? false}`}
            nodoTipo={entrada.nodoTipo}
            nodoId={entrada.nodoId}
            articuloId={modo === 'articulo' ? search.articuloId : undefined}
            incluirVacios={search.incluirVacios}
          />
        </div>
      ) : (
        <EmptyState
          title="Elige un artículo"
          description="En modo artículo, el árbol muestra dónde vive el artículo elegido: su distribución por sucursal, almacén, sub-almacén y rack."
        />
      )}
    </div>
  );
}
