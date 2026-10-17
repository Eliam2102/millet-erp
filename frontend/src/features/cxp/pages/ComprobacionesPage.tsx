import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Eye, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, SucursalSelector, TableSkeleton } from '@/components/erp';
import { useComprobaciones } from '@/features/cxp/api/useComprobaciones';
import { AccionesComprobacion } from '@/features/cxp/components/AccionesComprobacion';
import {
  EstadoComprobacionGastos,
  TipoComprobacionGastos,
  TipoComprobacionGastosLabels,
  type ComprobacionGastosListItem,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoComprobacionChip } from '@/features/cxp/components/EstadoChips';
import { NuevaComprobacionCajaChicaSheet } from '@/features/cxp/components/NuevaComprobacionCajaChicaSheet';
import { NuevaComprobacionAduanalesSheet } from '@/features/cxp/components/NuevaComprobacionAduanalesSheet';
import type { ComprobacionesSearch } from '@/features/cxp/lib/comprobaciones-search-schema';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';

const FROM = '/_app/cxp/comprobaciones/' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P1 — Bandeja de Comprobaciones de Gastos</c> (doc 07 §FE-F4-PR2).
 * Tabla con filtros por tipo + estado + sucursal. Acciones inline
 * contextuales según estado + tipo:
 * <list>
 *   <item><b>Borrador → PorRevisar</b>: enviar a revisión.</item>
 *   <item><b>PorRevisar → Autorizada</b>: autorizar (nivel 1).</item>
 *   <item><b>Autorizada → Aplicada</b>: aplicar (lo hace CxP).</item>
 *   <item><b>Aduanales PorRevisar → AutorizadaN1</b>: autorizar-nivel1
 *         (Comercio Exterior).</item>
 *   <item><b>Aduanales AutorizadaN1 → Autorizada</b>: autorizar-nivel2
 *         (DF). Verifica segregación de funciones.</item>
 *   <item><b>Cualquier estado intermedio</b>: rechazar (con motivo).</item>
 * </list>
 */
export function ComprobacionesPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeCapturar = useHasPermission(PermisosCanonicos.CuentasPorPagarComprobacionesCapturar);
  const [sheetCajaChica, setSheetCajaChica] = useState(false);
  const [sheetAduanales, setSheetAduanales] = useState(false);

  const query = useComprobaciones({
    tipo: search.tipo,
    estado: search.estado,
    sucursalId: search.sucursalId,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<ComprobacionesSearch>) {
    navigate({ to: '/cxp/comprobaciones', search: { ...search, ...parcial } });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Comprobaciones de gastos"
        descripcion="Caja chica + gastos aduanales (con doble firma). Viáticos y Tarjeta de crédito tienen sus propias pantallas."
        acciones={
          <>
            {puedeCapturar && (
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button>
                    <Plus className="mr-2 h-4 w-4" />
                    Nueva comprobación
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem onSelect={() => setSheetCajaChica(true)}>
                    Caja chica (N CFDIs)
                  </DropdownMenuItem>
                  <DropdownMenuItem onSelect={() => setSheetAduanales(true)}>
                    Aduanales (con pedimento)
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            )}
          </>
        }
      />

      <Filtros search={search} onChange={actualizarSearch} />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las comprobaciones"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-28' },
            { width: 'w-32' },
            { width: 'w-40' },
            { width: 'w-24' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin comprobaciones"
          description="No hay comprobaciones que coincidan con los filtros."
        />
      ) : (
        <Tabla items={items} />
      )}

      <NuevaComprobacionCajaChicaSheet open={sheetCajaChica} onOpenChange={setSheetCajaChica} />
      <NuevaComprobacionAduanalesSheet open={sheetAduanales} onOpenChange={setSheetAduanales} />
    </div>
  );
}

interface FiltrosProps {
  search: ComprobacionesSearch;
  onChange: (parcial: Partial<ComprobacionesSearch>) => void;
}

function Filtros({ search, onChange }: FiltrosProps) {
  const algunFiltro = search.tipo != null || search.estado != null || search.sucursalId;
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Tipo</label>
        <Select
          value={search.tipo != null ? String(search.tipo) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              tipo: v === SENTINEL_ALL ? undefined : (Number(v) as TipoComprobacionGastos),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por tipo" className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(TipoComprobacionGastos.ReembolsoCajaChica)}>
              Caja chica
            </SelectItem>
            <SelectItem value={String(TipoComprobacionGastos.GastosAduanales)}>
              Aduanales
            </SelectItem>
            <SelectItem value={String(TipoComprobacionGastos.Viaticos)}>Viáticos</SelectItem>
            <SelectItem value={String(TipoComprobacionGastos.TarjetaCredito)}>
              Tarjeta crédito
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estado</label>
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              estado: v === SENTINEL_ALL ? undefined : (Number(v) as EstadoComprobacionGastos),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por estado" className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(EstadoComprobacionGastos.Borrador)}>Borrador</SelectItem>
            <SelectItem value={String(EstadoComprobacionGastos.PorRevisar)}>Por revisar</SelectItem>
            <SelectItem value={String(EstadoComprobacionGastos.AutorizadaNivel1)}>
              Autorizada N1
            </SelectItem>
            <SelectItem value={String(EstadoComprobacionGastos.Autorizada)}>Autorizada</SelectItem>
            <SelectItem value={String(EstadoComprobacionGastos.Aplicada)}>Aplicada</SelectItem>
            <SelectItem value={String(EstadoComprobacionGastos.Rechazada)}>Rechazada</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Sucursal</label>
        <SucursalSelector
          value={search.sucursalId ?? null}
          onChange={(id) => onChange({ sucursalId: id ?? undefined })}
          placeholder="Todas las sucursales"
          className="w-56"
        />
      </div>
      {algunFiltro && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              tipo: undefined,
              estado: undefined,
              sucursalId: undefined,
            })
          }
        >
          Limpiar
        </Button>
      )}
    </div>
  );
}

function Tabla({ items }: { items: readonly ComprobacionGastosListItem[] }) {
  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">Tipo</th>
            <th className="px-3 py-2 text-left">Periodo</th>
            <th className="px-3 py-2 text-right">Líneas</th>
            <th className="px-3 py-2 text-right">Monto</th>
            <th className="px-3 py-2 text-left">Estado</th>
            <th className="px-3 py-2 text-right" aria-label="Acciones" />
          </tr>
        </thead>
        <tbody>
          {items.map((c) => (
            <tr key={c.id} className="border-t hover:bg-muted/30">
              <td className="px-3 py-2">{TipoComprobacionGastosLabels[c.tipo]}</td>
              <td className="px-3 py-2 whitespace-nowrap text-xs">
                {c.fechaInicio} → {c.fechaFin}
              </td>
              <td className="px-3 py-2 text-right font-mono">{c.numeroLineas}</td>
              <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                <MoneyDisplay amount={c.montoTotal} currency={c.moneda} strong />
              </td>
              <td className="px-3 py-2">
                <EstadoComprobacionChip estado={c.estado} />
              </td>
              <td className="px-3 py-2">
                <div className="flex flex-wrap items-center justify-end gap-1">
                  <Button asChild variant="ghost" size="sm">
                    <Link
                      to="/cxp/comprobaciones/$id"
                      params={{ id: c.id }}
                      aria-label={`Ver detalle de comprobación ${TipoComprobacionGastosLabels[c.tipo]}`}
                    >
                      <Eye className="mr-1 h-3 w-3" aria-hidden="true" />
                      Ver detalle
                    </Link>
                  </Button>
                  <AccionesComprobacion c={c} />
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
