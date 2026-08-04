import { useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { CheckCircle2, Eye, Plus } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  EmptyState,
  ErrorState,
  ProveedorSelector,
  TableSkeleton,
} from '@/components/erp';
import {
  useAplicarNotaCargo,
  useAutorizarNotaCargo,
  useNotasCargo,
} from '@/features/cxp/api/useNotasYAnticipos';
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

const FROM = '/_app/cxp/notas-cargo/' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P1 — Bandeja de Notas de Cargo</c> (doc 07 §FE-F4-PR1). Tabla con
 * filtros + acciones inline contextuales: Autorizar (Borrador →
 * Autorizada) y Aplicar (Autorizada → Aplicada). Las acciones
 * requieren permisos específicos.
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
  const puedeAutorizar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarNotasCargoAutorizar,
  );
  const puedeAplicar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarNotasCargoAplicar,
  );
  const [sheetAbierto, setSheetAbierto] = useState(false);

  const query = useNotasCargo({
    estado: search.estado,
    proveedorId: search.proveedorId,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<NotasCargoSearch>) {
    navigate({ to: '/cxp/notas-cargo', search: { ...search, ...parcial } });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Notas de cargo
          </h1>
          <p className="text-sm text-muted-foreground">
            Cargos internos al proveedor (devoluciones, garantías, fletes).
            Ciclo: Borrador → Autorizada (Dirección) → Aplicada.
          </p>
        </div>
        {puedeCrear && (
          <Button onClick={() => setSheetAbierto(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Crear nota de cargo
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Estado</label>
          <Select
            value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
            onValueChange={(v) =>
              actualizarSearch({
                estado:
                  v === SENTINEL_ALL ? undefined : (Number(v) as EstadoNotaCargo),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por estado" className="w-48">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              <SelectItem value={String(EstadoNotaCargo.Borrador)}>
                Borrador
              </SelectItem>
              <SelectItem value={String(EstadoNotaCargo.Autorizada)}>
                Autorizada
              </SelectItem>
              <SelectItem value={String(EstadoNotaCargo.Aplicada)}>
                Aplicada
              </SelectItem>
              <SelectItem value={String(EstadoNotaCargo.Formalizada)}>
                Formalizada
              </SelectItem>
              <SelectItem value={String(EstadoNotaCargo.Cancelada)}>
                Cancelada
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">
            Proveedor
          </label>
          <ProveedorSelector
            value={search.proveedorId ?? null}
            onChange={(id) =>
              actualizarSearch({ proveedorId: id ?? undefined })
            }
            placeholder="Todos los proveedores"
            className="w-56"
          />
        </div>
      </div>

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
            search.estado != null || search.proveedorId
              ? 'No hay notas que coincidan con los filtros.'
              : 'Aún no hay notas de cargo. Crea la primera desde el botón superior.'
          }
        />
      ) : (
        <Tabla
          items={items}
          puedeAutorizar={puedeAutorizar}
          puedeAplicar={puedeAplicar}
        />
      )}

      <NuevaNotaCargoSheet open={sheetAbierto} onOpenChange={setSheetAbierto} />
    </div>
  );
}

interface TablaProps {
  items: readonly NotaCargoListItem[];
  puedeAutorizar: boolean;
  puedeAplicar: boolean;
}

function Tabla({ items, puedeAutorizar, puedeAplicar }: TablaProps) {
  const autorizar = useAutorizarNotaCargo();
  const aplicar = useAplicarNotaCargo();

  function handleAutorizar(n: NotaCargoListItem) {
    autorizar.mutate(
      {
        id: n.id,
        versionEsperada: n.version,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => toast.success(`NC ${n.folio} autorizada`),
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title);
            return;
          }
          toast.error('Error al autorizar.');
        },
      },
    );
  }

  function handleAplicar(n: NotaCargoListItem) {
    aplicar.mutate(
      {
        id: n.id,
        versionEsperada: n.version,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => toast.success(`NC ${n.folio} aplicada`),
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title);
            return;
          }
          toast.error('Error al aplicar.');
        },
      },
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">Folio</th>
            <th className="px-3 py-2 text-left">Proveedor</th>
            <th className="px-3 py-2 text-left">Concepto</th>
            <th className="px-3 py-2 text-right">Monto</th>
            <th className="px-3 py-2 text-left">Estado</th>
            <th className="px-3 py-2 text-right" aria-label="Acciones" />
          </tr>
        </thead>
        <tbody>
          {items.map((n) => (
            <tr key={n.id} className="border-t hover:bg-muted/30">
              <td className="px-3 py-2 font-mono text-xs">{n.folio}</td>
              <td className="max-w-56 truncate px-3 py-2">
                {n.proveedorNombre ?? (
                  <span className="font-mono text-xs text-muted-foreground">
                    {`${n.proveedorId.slice(0, 8)}…`}
                  </span>
                )}
              </td>
              <td className="px-3 py-2">{truncar(n.concepto, 80)}</td>
              <td className="px-3 py-2 text-right font-mono">
                {formatearMonto(n.monto, n.moneda)}
              </td>
              <td className="px-3 py-2">
                <EstadoNotaCargoChip estado={n.estado} />
              </td>
              <td className="px-3 py-2 text-right">
                <Button asChild variant="ghost" size="sm" className="mr-1">
                  <Link
                    to="/cxp/notas-cargo/$id"
                    params={{ id: n.id }}
                    aria-label={`Ver detalle de nota de cargo ${n.folio}`}
                  >
                    <Eye className="mr-1 h-3 w-3" />
                    Ver detalle
                  </Link>
                </Button>
                {n.estado === EstadoNotaCargo.Borrador && puedeAutorizar && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => handleAutorizar(n)}
                    disabled={autorizar.isPending}
                  >
                    <CheckCircle2 className="mr-1 h-3 w-3" />
                    Autorizar
                  </Button>
                )}
                {n.estado === EstadoNotaCargo.Autorizada && puedeAplicar && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => handleAplicar(n)}
                    disabled={aplicar.isPending}
                  >
                    Aplicar
                  </Button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function truncar(s: string, max: number): string {
  return s.length > max ? `${s.slice(0, max)}…` : s;
}

function formatearMonto(v: number, moneda: string): string {
  try {
    return new Intl.NumberFormat('es-MX', {
      style: 'currency',
      currency: moneda,
      minimumFractionDigits: 2,
    }).format(v);
  } catch {
    return `${v.toFixed(2)} ${moneda}`;
  }
}
