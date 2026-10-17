import { PageHeader } from '@/components/erp/PageHeader';
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
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { MotivoDialog } from '@/components/erp/feedback/MotivoDialog';

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
      <PageHeader
        titulo="Tarjetas de crédito"
        descripcion="Master de TC empresariales. Solo se almacenan los últimos 4 dígitos. Cancelar es irreversible (no se puede reactivar)."
        acciones={
          <>
            {puedeAdmin && (
              <Button onClick={() => setNuevaAbierta(true)}>
                <Plus className="mr-2 h-4 w-4" />
                Nueva tarjeta
              </Button>
            )}
          </>
        }
      />

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
  const [dialogo, setDialogo] = useState<{
    tipo: 'bloquear' | 'cancelar';
    t: Tarjeta;
  } | null>(null);
  const [errorMotivo, setErrorMotivo] = useState<string | null>(null);

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
    setErrorMotivo(null);
    setDialogo({ tipo: 'bloquear', t });
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
    setDialogo({ tipo: 'cancelar', t });
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
              <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                <MoneyDisplay amount={t.limiteCreditoMxn} currency={'MXN'} strong />
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
      {dialogo?.tipo === 'bloquear' && (
        <MotivoDialog
          titulo="Bloquear tarjeta"
          descripcion={<>Tarjeta {dialogo.t.nombreAlias} ({dialogo.t.numeroEnmascarado}). Podrás reactivarla después.</>}
          etiqueta="Motivo del bloqueo"
          min={5}
          max={200}
          verbo="Bloquear tarjeta"
          error={errorMotivo}
          onCerrar={() => setDialogo(null)}
          onConfirmar={async (motivo) => {
            const t = dialogo.t;
            try {
              await bloquear.mutateAsync({
                id: t.id,
                versionEsperada: t.version,
                command: { motivo, fecha: hoyLocalISO() },
                idempotencyKey: crypto.randomUUID(),
              });
              toast.success('Tarjeta bloqueada');
              setDialogo(null);
            } catch (e) {
              setErrorMotivo(
                esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo bloquear la tarjeta.',
              );
            }
          }}
        />
      )}
      <ConfirmarAccionDialog
        open={dialogo?.tipo === 'cancelar'}
        onOpenChange={(o) => !o && setDialogo(null)}
        titulo="Cancelar tarjeta"
        resumen={
          <>
            Se cancelará la tarjeta {dialogo?.t.nombreAlias} ({dialogo?.t.numeroEnmascarado}). Es
            irreversible.
          </>
        }
        verbo="Cancelar tarjeta"
        tono="peligro"
        pendiente={cancelar.isPending}
        onConfirmar={() => {
          const t = dialogo?.t;
          if (!t) return;
          dispatch(
            'Tarjeta cancelada',
            cancelar.mutateAsync({
              id: t.id,
              versionEsperada: t.version,
              fecha: hoyLocalISO(),
              idempotencyKey: crypto.randomUUID(),
            }),
          );
          setDialogo(null);
        }}
      />
    </div>
  );
}

