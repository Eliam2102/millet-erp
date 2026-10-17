import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
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
  BarraFiltros,
  EmptyState,
  ErrorState,
  PaginacionTabla,
  ProveedorSelector,
  TableSkeleton,
  TabsEstado,
  useConteosPorEstado,
} from '@/components/erp';
import { BusquedaFolio } from '@/components/erp/data/BusquedaFolio';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/components/erp/data/paginacion';
import { ProveedorCelda } from '@/features/cxp/components/ProveedorCelda';
import { cxpKeys } from '@/features/cxp/api/keys';
import { useNotasCargo } from '@/features/cxp/api/useNotasYAnticipos';
import { AplicarNotaCargoAccion } from '@/features/cxp/components/AplicarNotaCargoAccion';
import {
  EstadoNotaCargo,
  type NotaCargoListItem,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoNotaCargoChip } from '@/features/cxp/components/EstadoChips';
import { NuevaNotaCargoSheet } from '@/features/cxp/components/NuevaNotaCargoSheet';
import type { NotasCargoSearch } from '@/features/cxp/lib/notas-y-anticipos-search-schema';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';

const FROM = '/_app/cxp/notas-cargo/' as const;
const TABS_ESTADO = [
  { valor: EstadoNotaCargo.Borrador, label: 'Borrador' },
  { valor: EstadoNotaCargo.Autorizada, label: 'Autorizada' },
  { valor: EstadoNotaCargo.Aplicada, label: 'Aplicada' },
  { valor: EstadoNotaCargo.Formalizada, label: 'Formalizada' },
  { valor: EstadoNotaCargo.Cancelada, label: 'Cancelada' },
] as const;

/**
 * <c>P1 — Bandeja de Notas de Cargo</c> (doc 07 §FE-F4-PR1). Tabla con
 * filtros + acción inline Aplicar (Autorizada → Aplicada) con confirmación.
 * Autorizar (Borrador → Autorizada) vive solo en el detalle: la regla de
 * segregación (el creador no autoriza) necesita <c>creadoPor</c>, que la lista
 * no expone (CXP-30). Requiere permisos específicos.
 *
 * <para>PLATFORM-TODO(&lt;NotaCargoEvidenciaAutorizacion&gt;): cuando
 * se requiera evidencia formal del autorizador (Dirección de Finanzas),
 * integrar <c>AutorizacionInformalForm</c> en un sheet previo a
 * <c>autorizar</c>. Hoy basta con el permiso canonical.</para>
 */
export function NotasCargoPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeCrear = useHasPermission(
    PermisosCanonicos.CuentasPorPagarNotasCargoCrear,
  );
  const puedeAplicar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarNotasCargoAplicar,
  );
  const [sheetAbierto, setSheetAbierto] = useState(false);

  const query = useNotasCargo({
    estado: search.estado,
    proveedorId: search.proveedorId,
    q: search.q,
    ...paginaAOffsetLimit(search),
  });

  const conteos = useConteosPorEstado(
    cxpKeys.notasCargo(),
    '/api/v1/cuentas-por-pagar/notas-cargo',
    { proveedorId: search.proveedorId, q: search.q },
  );

  function actualizarSearch(parcial: Partial<NotasCargoSearch>) {
    navigate({
      to: '/cxp/notas-cargo',
      // Cambiar un filtro regresa a la página 1; la paginación pasa su propia página.
      search: { ...search, page: undefined, ...parcial },
    });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Notas de cargo"
        descripcion="Cargos internos al proveedor (devoluciones, garantías, fletes). Ciclo: Borrador → Autorizada (Dirección) → Aplicada."
        acciones={
          <>
            {puedeCrear && (
              <Button onClick={() => setSheetAbierto(true)}>
                <Plus className="mr-2 h-4 w-4" />
                Crear nota de cargo
              </Button>
            )}
          </>
        }
      />

      <TabsEstado
        opciones={TABS_ESTADO}
        valor={search.estado}
        onChange={(estado) => actualizarSearch({ estado: estado as EstadoNotaCargo | undefined })}
        conteos={conteos.data}
      />

      <BarraFiltros
        aria-label="Filtros de notas de cargo"
        busqueda={
          <BusquedaFolio
            aria-label="Buscar por folio o concepto"
            valor={search.q}
            onCommit={(q) => actualizarSearch({ q })}
            placeholder="Folio o concepto"
          />
        }
        filtros={
          <ProveedorSelector
            value={search.proveedorId ?? null}
            onChange={(id) => actualizarSearch({ proveedorId: id ?? undefined })}
            placeholder="Todos los proveedores"
            className="h-ctl-md w-56 text-xs"
          />
        }
        onLimpiar={
          search.proveedorId || search.q
            ? () => actualizarSearch({ proveedorId: undefined, q: undefined })
            : undefined
        }
        total={
          query.data
            ? `${query.data.total} ${query.data.total === 1 ? 'nota de cargo' : 'notas de cargo'}`
            : undefined
        }
      />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las notas de cargo"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-32' },
            { width: 'w-48' },
            { width: 'w-32' },
            { width: 'w-28' },
            { width: 'w-32' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin notas de cargo"
          description={
            search.estado != null || search.proveedorId || search.q
              ? 'No hay notas que coincidan con los filtros.'
              : 'Aún no hay notas de cargo. Crea la primera desde el botón superior.'
          }
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <Tabla items={items} puedeAplicar={puedeAplicar} />
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? 0}
            limit={query.data?.limit ?? paginaAOffsetLimit(search).limit}
            onChange={(offset, limit) => actualizarSearch(offsetLimitAPagina(offset, limit))}
          />
        </div>
      )}

      <NuevaNotaCargoSheet open={sheetAbierto} onOpenChange={setSheetAbierto} />
    </div>
  );
}

interface TablaProps {
  items: readonly NotaCargoListItem[];
  puedeAplicar: boolean;
}

function Tabla({ items, puedeAplicar }: TablaProps) {
  const search = useSearch({ from: FROM });
  return (
    <TableContainer>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Folio</TableHead>
            <TableHead>Proveedor</TableHead>
            <TableHead>Concepto</TableHead>
            <TableHead className="text-right">Monto</TableHead>
            <TableHead>Estado</TableHead>
            <TableHead className="text-right">
              <span className="sr-only">Acciones</span>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {items.map((n) => (
            <TableRow key={n.id} className="hover:bg-surface-subtle">
              <TableCell className="font-mono text-xs">
                <Link
                  to="/cxp/notas-cargo/$id"
                  params={{ id: n.id }}
                  search={search}
                  className="text-brand hover:underline"
                  aria-label={`Abrir nota de cargo ${n.folio}`}
                >
                  {n.folio}
                </Link>
              </TableCell>
              <TableCell>
                <ProveedorCelda nombre={n.proveedorNombre} rfc={n.proveedorRfc} />
              </TableCell>
              <TableCell title={n.concepto}>{truncar(n.concepto, 80)}</TableCell>
              <TableCell className="text-right tabular-nums">
                <MoneyDisplay amount={n.monto} currency={n.moneda} strong />
              </TableCell>
              <TableCell>
                <EstadoNotaCargoChip estado={n.estado} />
              </TableCell>
              <TableCell className="text-right">
                {n.estado === EstadoNotaCargo.Autorizada && puedeAplicar && (
                  <AplicarNotaCargoAccion n={n} />
                )}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}

function truncar(s: string, max: number): string {
  return s.length > max ? `${s.slice(0, max)}…` : s;
}

