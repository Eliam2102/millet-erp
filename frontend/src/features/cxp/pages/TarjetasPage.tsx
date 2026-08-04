import { hoyLocalISO } from '@/lib/datetime';
import { useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Ban, CheckCircle2, Lock, Plus, Unlock } from 'lucide-react';
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
  TableSkeleton,
  UsuarioSelector,
} from '@/components/erp';
import {
  useBloquearTarjeta,
  useCancelarTarjeta,
  useReactivarTarjeta,
  useTarjetas,
} from '@/features/cxp/api/useTarjetasCredito';
import {
  EstadoTarjeta,
  type Tarjeta,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoTarjetaChip } from '@/features/cxp/components/EstadoChips';
import { NuevaTarjetaSheet } from '@/features/cxp/components/NuevaTarjetaSheet';
import type { TarjetasSearch } from '@/features/cxp/lib/tc-search-schema';

const FROM = '/_app/cxp/tc/tarjetas' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P1 — Master de Tarjetas de Crédito</c> (doc 07 §FE-F6-PR1, Admin).
 * Bandeja con acciones inline: Bloquear / Reactivar / Cancelar.
 *
 * <para>PLATFORM-TODO(&lt;TarjetaDetallePageUsuariosAutorizados&gt;):
 * gestión de usuarios autorizados (CRUD) entra cuando se construya
 * <c>/cxp/tc/tarjetas/$id</c>.</para>
 */
export function TarjetasPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeAdmin = useHasPermission(
    PermisosCanonicos.CuentasPorPagarTcAdministrar,
  );
  const [nuevaAbierta, setNuevaAbierta] = useState(false);

  const query = useTarjetas({
    estado: search.estado,
    titularId: search.titularId,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<TarjetasSearch>) {
    navigate({ to: '/cxp/tc/tarjetas', search: { ...search, ...parcial } });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Tarjetas de crédito
          </h1>
          <p className="text-sm text-muted-foreground">
            Master de TC empresariales. Solo se almacenan los últimos 4
            dígitos. Cancelar es irreversible (no se puede reactivar).
          </p>
        </div>
        {puedeAdmin && (
          <Button onClick={() => setNuevaAbierta(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Nueva tarjeta
          </Button>
        )}
      </div>

      <Filtros search={search} onChange={actualizarSearch} />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las tarjetas"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={4}
          columns={[
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin tarjetas"
          description="Agrega la primera tarjeta de crédito empresarial."
        />
      ) : (
        <Tabla items={items} puedeAdmin={puedeAdmin} />
      )}

      <NuevaTarjetaSheet open={nuevaAbierta} onOpenChange={setNuevaAbierta} />
    </div>
  );
}

function Filtros({
  search,
  onChange,
}: {
  search: TarjetasSearch;
  onChange: (parcial: Partial<TarjetasSearch>) => void;
}) {
  const algun = search.estado != null || search.titularId;
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estado</label>
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              estado:
                v === SENTINEL_ALL ? undefined : (Number(v) as EstadoTarjeta),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por estado" className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(EstadoTarjeta.Activa)}>Activa</SelectItem>
            <SelectItem value={String(EstadoTarjeta.Bloqueada)}>
              Bloqueada
            </SelectItem>
            <SelectItem value={String(EstadoTarjeta.Cancelada)}>
              Cancelada
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Titular</label>
        <UsuarioSelector
          value={search.titularId ?? null}
          onChange={(id) => onChange({ titularId: id ?? undefined })}
          placeholder="Todos los titulares"
          className="w-56"
        />
      </div>
      {algun && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({ estado: undefined, titularId: undefined })
          }
        >
          Limpiar
        </Button>
      )}
    </div>
  );
}

function Tabla({
  items,
  puedeAdmin,
}: {
  items: readonly Tarjeta[];
  puedeAdmin: boolean;
}) {
  const bloquear = useBloquearTarjeta();
  const reactivar = useReactivarTarjeta();
  const cancelar = useCancelarTarjeta();

  function dispatch(label: string, p: Promise<unknown>) {
    p.then(
      () => toast.success(label),
      (error) => {
        if (esApiError(error)) {
          toast.error(error.problem.title);
          return;
        }
        toast.error('Error al ejecutar acción.');
      },
    );
  }

  function handleBloquear(t: Tarjeta) {
    const motivo = window.prompt('Motivo del bloqueo (5-200 chars):', '');
    if (!motivo || motivo.trim().length < 5) {
      toast.error('Motivo demasiado corto.');
      return;
    }
    const hoy = hoyLocalISO();
    dispatch(
      'Tarjeta bloqueada',
      bloquear.mutateAsync({
        id: t.id,
        versionEsperada: t.version,
        command: { motivo: motivo.trim(), fecha: hoy },
        idempotencyKey: crypto.randomUUID(),
      }),
    );
  }

  function handleReactivar(t: Tarjeta) {
    dispatch(
      'Tarjeta reactivada',
      reactivar.mutateAsync({
        id: t.id,
        versionEsperada: t.version,
        idempotencyKey: crypto.randomUUID(),
      }),
    );
  }

  function handleCancelar(t: Tarjeta) {
    if (!window.confirm(`¿Cancelar tarjeta ${t.numeroEnmascarado}? Es irreversible.`)) return;
    const hoy = hoyLocalISO();
    dispatch(
      'Tarjeta cancelada',
      cancelar.mutateAsync({
        id: t.id,
        versionEsperada: t.version,
        fecha: hoy,
        idempotencyKey: crypto.randomUUID(),
      }),
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">Alias</th>
            <th className="px-3 py-2 text-left">Número</th>
            <th className="px-3 py-2 text-left">Emisora</th>
            <th className="px-3 py-2 text-right">Límite</th>
            <th className="px-3 py-2 text-left">Día corte / pago</th>
            <th className="px-3 py-2 text-left">Estado</th>
            <th className="px-3 py-2 text-right" aria-label="Acciones" />
          </tr>
        </thead>
        <tbody>
          {items.map((t) => (
            <tr key={t.id} className="border-t hover:bg-muted/30">
              <td className="px-3 py-2 font-medium">{t.nombreAlias}</td>
              <td className="px-3 py-2 font-mono text-xs">
                {t.numeroEnmascarado}
              </td>
              <td className="px-3 py-2">{t.emisora}</td>
              <td className="px-3 py-2 text-right font-mono">
                {formatearMonto(t.limiteCreditoMxn, 'MXN')}
              </td>
              <td className="px-3 py-2 text-xs text-muted-foreground">
                {t.diaCorte} / {t.diaLimitePago}
              </td>
              <td className="px-3 py-2">
                <EstadoTarjetaChip estado={t.estado} />
              </td>
              <td className="px-3 py-2 text-right">
                {puedeAdmin && t.estado === EstadoTarjeta.Activa && (
                  <>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleBloquear(t)}
                      disabled={bloquear.isPending}
                    >
                      <Lock className="mr-1 h-3 w-3" />
                      Bloquear
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleCancelar(t)}
                      className="text-destructive"
                      disabled={cancelar.isPending}
                    >
                      <Ban className="mr-1 h-3 w-3" />
                      Cancelar
                    </Button>
                  </>
                )}
                {puedeAdmin && t.estado === EstadoTarjeta.Bloqueada && (
                  <>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleReactivar(t)}
                      disabled={reactivar.isPending}
                    >
                      <Unlock className="mr-1 h-3 w-3" />
                      Reactivar
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleCancelar(t)}
                      className="text-destructive"
                      disabled={cancelar.isPending}
                    >
                      <Ban className="mr-1 h-3 w-3" />
                      Cancelar
                    </Button>
                  </>
                )}
                {t.estado === EstadoTarjeta.Cancelada && (
                  <span className="text-xs italic text-muted-foreground">
                    <CheckCircle2 className="mr-1 inline h-3 w-3" />
                    Final
                  </span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
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
