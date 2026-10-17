import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  BarraFiltros,
  TabsEstado,
  useConteosPorEstado,
  EmptyState,
  ErrorState,
  PaginacionTabla,
  SucursalSelector,
  TableSkeleton,
} from '@/components/erp';
import { BusquedaFolio } from '@/components/erp/data/BusquedaFolio';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/components/erp/data/paginacion';
import { cxpKeys } from '@/features/cxp/api/keys';
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
    q: search.q,
    ...paginaAOffsetLimit(search),
  });

  const conteos = useConteosPorEstado(
    cxpKeys.comprobaciones(),
    '/api/v1/cuentas-por-pagar/comprobaciones',
    { tipo: search.tipo, sucursalId: search.sucursalId, q: search.q },
  );

  function actualizarSearch(parcial: Partial<ComprobacionesSearch>) {
    navigate({
      to: '/cxp/comprobaciones',
      // Cambiar un filtro regresa a la página 1; la paginación pasa su propia página.
      search: { ...search, page: undefined, ...parcial },
    });
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
                    <Plus className="mr-2 size-3.5" />
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

      <TabsEstado
        opciones={TABS_ESTADO}
        valor={search.estado}
        onChange={(estado) => actualizarSearch({ estado: estado as EstadoComprobacionGastos | undefined })}
        conteos={conteos.data}
      />

      <Filtros search={search} total={query.data?.total} onChange={actualizarSearch} />

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
          description={
            search.tipo != null || search.estado != null || search.sucursalId || search.q
              ? 'No hay comprobaciones que coincidan con los filtros.'
              : 'Aún no hay comprobaciones. Crea la primera desde «Nueva comprobación».'
          }
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <Tabla items={items} />
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? 0}
            limit={query.data?.limit ?? paginaAOffsetLimit(search).limit}
            onChange={(offset, limit) => actualizarSearch(offsetLimitAPagina(offset, limit))}
          />
        </div>
      )}

      <NuevaComprobacionCajaChicaSheet open={sheetCajaChica} onOpenChange={setSheetCajaChica} />
      <NuevaComprobacionAduanalesSheet open={sheetAduanales} onOpenChange={setSheetAduanales} />
    </div>
  );
}

const TABS_ESTADO = [
  { valor: EstadoComprobacionGastos.Borrador, label: 'Borrador' },
  { valor: EstadoComprobacionGastos.PorRevisar, label: 'Por revisar' },
  { valor: EstadoComprobacionGastos.AutorizadaNivel1, label: 'Autorizada N1' },
  { valor: EstadoComprobacionGastos.Autorizada, label: 'Autorizada' },
  { valor: EstadoComprobacionGastos.Aplicada, label: 'Aplicada' },
  { valor: EstadoComprobacionGastos.Rechazada, label: 'Rechazada' },
] as const;

interface FiltrosProps {
  search: ComprobacionesSearch;
  total: number | undefined;
  onChange: (parcial: Partial<ComprobacionesSearch>) => void;
}

function Filtros({ search, total, onChange }: FiltrosProps) {
  const algunFiltro = search.tipo != null || search.sucursalId || search.q;
  return (
    <BarraFiltros
      aria-label="Filtros de comprobaciones"
      busqueda={
        <BusquedaFolio
          aria-label="Buscar por pedimento u observaciones"
          valor={search.q}
          onCommit={(q) => onChange({ q })}
          placeholder="Pedimento u observaciones"
        />
      }
      filtros={
        <Select
          value={search.tipo != null ? String(search.tipo) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              tipo: v === SENTINEL_ALL ? undefined : (Number(v) as TipoComprobacionGastos),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por tipo" className="h-ctl-md w-40 text-xs">
            <SelectValue placeholder="Todos los tipos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos los tipos</SelectItem>
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
      }
      masFiltros={
        <div className="space-y-1">
          <label className="text-xs text-ink-muted">Sucursal</label>
          <SucursalSelector
            value={search.sucursalId ?? null}
            onChange={(id) => onChange({ sucursalId: id ?? undefined })}
            placeholder="Todas las sucursales"
            className="w-full"
          />
        </div>
      }
      masFiltrosActivos={search.sucursalId ? 1 : 0}
      onLimpiar={
        algunFiltro
          ? () => onChange({ tipo: undefined, sucursalId: undefined, q: undefined })
          : undefined
      }
      total={total != null ? `${total} ${total === 1 ? 'comprobación' : 'comprobaciones'}` : undefined}
    />
  );
}

function Tabla({ items }: { items: readonly ComprobacionGastosListItem[] }) {
  const search = useSearch({ from: FROM });
  return (
    <TableContainer>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Comprobación</TableHead>
            <TableHead>Responsable</TableHead>
            <TableHead>Sucursal</TableHead>
            <TableHead>Periodo</TableHead>
            <TableHead className="text-right">Líneas</TableHead>
            <TableHead className="text-right">Monto</TableHead>
            <TableHead>Estado</TableHead>
            <TableHead className="text-right">
              <span className="sr-only">Acciones</span>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((c) => {
            const tipo = TipoComprobacionGastosLabels[c.tipo];
            return (
              <TableRow key={c.id} className="hover:bg-surface-subtle">
                <TableCell>
                  <Link
                    to="/cxp/comprobaciones/$id"
                    params={{ id: c.id }}
                    search={search}
                    className="font-medium text-brand hover:underline"
                    aria-label={`Abrir comprobación ${tipo} de ${c.responsableNombre ?? 'responsable por confirmar'}`}
                  >
                    {tipo}
                  </Link>
                </TableCell>
                <TableCell>
                  <div className="max-w-56 truncate" title={c.responsableNombre ?? undefined}>
                    {c.responsableNombre?.trim() || '[RESPONSABLE POR CONFIRMAR]'}
                  </div>
                </TableCell>
                <TableCell>{c.sucursalNombre?.trim() || '[SUCURSAL POR CONFIRMAR]'}</TableCell>
                <TableCell className="whitespace-nowrap text-xs">
                  {c.fechaInicio} → {c.fechaFin}
                </TableCell>
                <TableCell className="text-right font-mono">{c.numeroLineas}</TableCell>
                <TableCell className="text-right tabular-nums">
                  <MoneyDisplay amount={c.montoTotal} currency={c.moneda} strong />
                </TableCell>
                <TableCell>
                  <EstadoComprobacionChip estado={c.estado} />
                </TableCell>
                <TableCell>
                  <div className="flex flex-wrap items-center justify-end gap-1">
                    <AccionesComprobacion c={c} responsableNombre={c.responsableNombre} />
                  </div>
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
