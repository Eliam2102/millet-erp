import { useState } from 'react';
import { Pencil, Plus, Power, PowerOff, Truck, UserRound } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { ErrorState, TableSkeleton } from '@/components/erp';
import type {
  OperadorListItem,
  VehiculoListItem,
} from '@/features/facturacion/api/types';
import {
  useActualizarOperador,
  useActualizarVehiculo,
  useOperadoresAdmin,
  useVehiculosAdmin,
} from '@/features/facturacion/api/cartaPorteCatalogos';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { VehiculoInlineForm } from '@/features/facturacion/components/VehiculoInlineForm';
import { OperadorInlineForm } from '@/features/facturacion/components/OperadorInlineForm';

/**
 * <c>&lt;CartaPorteCatalogosPage/&gt;</c> — catálogos administrables de
 * Carta Porte (módulo Facturación) en <c>/admin/carta-porte-catalogos</c>:
 * Vehículos (autotransporte) y Operadores (choferes). Cierra
 * PLATFORM-TODO(&lt;VehiculoPicker&gt;/&lt;OperadorPicker&gt;) junto con los
 * pickers de emisión.
 *
 * <para>Mismo patrón que <c>CanalesVentaPage</c>: lista + alta y edición
 * con inline forms (NUNCA modal), desactivar con confirm (el renglón sale
 * de los pickers de emisión sin tocar las Cartas Porte históricas) y
 * reactivar directo. Mutaciones gated por
 * <c>facturacion.carta-porte.emitir</c> (mismo permiso que los POST del
 * backend); la lectura la garantiza el guard de la ruta
 * (<c>facturacion.carta-porte.leer</c>).</para>
 */
export function CartaPorteCatalogosPage() {
  return (
    <div className="mx-auto max-w-5xl space-y-8 p-4">
      <SeccionVehiculos />
      <SeccionOperadores />
    </div>
  );
}

function toastActualizarError(error: Error, fallback: string) {
  if (esApiError(error)) {
    toast.error(error.problem.title, {
      description: error.traceId ? `Código: ${error.traceId}` : undefined,
    });
  } else {
    toast.error(fallback);
  }
}

// ─── Vehículos ────────────────────────────────────────────────────────

function SeccionVehiculos() {
  const [agregando, setAgregando] = useState(false);
  const [editandoId, setEditandoId] = useState<string | null>(null);
  const [mostrarInactivos, setMostrarInactivos] = useState(false);
  const [confirmDesactivar, setConfirmDesactivar] =
    useState<VehiculoListItem | null>(null);

  const canGestionar = useHasPermission(
    PermisosCanonicos.FacturacionCartaPorteEmitir,
  );

  const query = useVehiculosAdmin(mostrarInactivos);
  const vehiculos = query.data ?? [];

  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarVehiculo();

  function cambiarActivo(vehiculo: VehiculoListItem, activo: boolean, mensajeOk: string) {
    actualizar.mutate(
      { id: vehiculo.id, payload: { activo }, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(mensajeOk);
          setConfirmDesactivar(null);
        },
        onError: (error) => {
          toastActualizarError(error, 'Error al actualizar el vehículo.');
          setConfirmDesactivar(null);
        },
      },
    );
  }

  return (
    <section className="space-y-4">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <Truck className="h-5 w-5" aria-hidden="true" />
            Vehículos
          </h2>
          <p className="text-sm text-muted-foreground">
            Autotransporte para Carta Porte 3.1: placa, configuración SAT,
            permiso SCT, seguro y peso bruto (obligatorio para timbrar).
            Total: {vehiculos.length}.
          </p>
        </div>
        <div className="flex items-center gap-3">
          <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <input
              type="checkbox"
              className="size-4 rounded border-input"
              checked={mostrarInactivos}
              onChange={(e) => setMostrarInactivos(e.target.checked)}
            />
            Mostrar inactivos
          </label>
          {canGestionar && !agregando && (
            <Button
              size="sm"
              variant="outline"
              onClick={() => {
                setAgregando(true);
                setEditandoId(null);
              }}
            >
              <Plus className="mr-1 h-4 w-4" />
              Agregar vehículo
            </Button>
          )}
        </div>
      </header>

      {agregando && canGestionar && (
        <VehiculoInlineForm
          onCancel={() => setAgregando(false)}
          onSaved={() => setAgregando(false)}
        />
      )}

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los vehículos"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton rows={4} />
      ) : vehiculos.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          No hay vehículos registrados.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {vehiculos.map((v) => {
            const editando = editandoId === v.id;
            return (
              <li key={v.id} className="px-3 py-2">
                {editando && canGestionar ? (
                  <VehiculoInlineForm
                    vehiculo={v}
                    onCancel={() => setEditandoId(null)}
                    onSaved={() => setEditandoId(null)}
                  />
                ) : (
                  <div className="flex flex-wrap items-center gap-3">
                    <span className="w-24 truncate font-mono text-sm font-medium">
                      {v.placa}
                    </span>
                    <span
                      className="w-16 font-mono text-xs"
                      title="Configuración vehicular (c_ConfigAutotransporte)"
                    >
                      {v.configVehicular}
                    </span>
                    <span className="w-12 text-xs tabular-nums text-muted-foreground">
                      {v.anioModelo}
                    </span>
                    {v.pesoBrutoVehicular != null ? (
                      <span
                        className="w-16 text-xs tabular-nums"
                        title="Peso bruto vehicular (toneladas)"
                      >
                        {v.pesoBrutoVehicular} t
                      </span>
                    ) : (
                      <span
                        className="w-16 text-xs text-amber-600"
                        title="Sin peso bruto vehicular — el SAT lo exige para timbrar Carta Porte 3.1."
                      >
                        Sin peso
                      </span>
                    )}
                    <span className="flex-1 truncate text-xs text-muted-foreground">
                      {[
                        v.tipoPermisoSct != null
                          ? `${v.tipoPermisoSct}${v.numPermisoSct != null ? ` ${v.numPermisoSct}` : ''}`
                          : null,
                        v.aseguradora,
                      ]
                        .filter(Boolean)
                        .join(' · ') || '—'}
                    </span>
                    {v.activo ? (
                      <Badge variant="secondary">Activo</Badge>
                    ) : (
                      <Badge variant="outline" className="text-muted-foreground">
                        Inactivo
                      </Badge>
                    )}
                    {canGestionar && (
                      <div className="flex items-center gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => {
                            setEditandoId(v.id);
                            setAgregando(false);
                          }}
                          aria-label={`Editar vehículo ${v.placa}`}
                        >
                          <Pencil className="h-3.5 w-3.5" />
                        </Button>
                        {v.activo ? (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setConfirmDesactivar(v)}
                            aria-label={`Desactivar vehículo ${v.placa}`}
                          >
                            <PowerOff className="h-3.5 w-3.5" />
                          </Button>
                        ) : (
                          <Button
                            variant="ghost"
                            size="sm"
                            disabled={actualizar.isPending}
                            onClick={() =>
                              cambiarActivo(v, true, `Vehículo ${v.placa} reactivado`)
                            }
                            aria-label={`Reactivar vehículo ${v.placa}`}
                          >
                            <Power className="h-3.5 w-3.5" />
                          </Button>
                        )}
                      </div>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      <AlertDialog
        open={confirmDesactivar != null}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar vehículo</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar el vehículo{' '}
              <span className="font-semibold">{confirmDesactivar?.placa}</span>?
              Saldrá del selector de emisión de Carta Porte, pero las Cartas
              Porte existentes conservan su histórico. Se puede reactivar en
              cualquier momento.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={actualizar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (confirmDesactivar == null) return;
                cambiarActivo(
                  confirmDesactivar,
                  false,
                  `Vehículo ${confirmDesactivar.placa} desactivado`,
                );
              }}
              disabled={actualizar.isPending}
            >
              {actualizar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}

// ─── Operadores ───────────────────────────────────────────────────────

function SeccionOperadores() {
  const [agregando, setAgregando] = useState(false);
  const [editandoId, setEditandoId] = useState<string | null>(null);
  const [mostrarInactivos, setMostrarInactivos] = useState(false);
  const [confirmDesactivar, setConfirmDesactivar] =
    useState<OperadorListItem | null>(null);

  const canGestionar = useHasPermission(
    PermisosCanonicos.FacturacionCartaPorteEmitir,
  );

  const query = useOperadoresAdmin(mostrarInactivos);
  const operadores = query.data ?? [];

  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarOperador();

  function cambiarActivo(operador: OperadorListItem, activo: boolean, mensajeOk: string) {
    actualizar.mutate(
      { id: operador.id, payload: { activo }, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(mensajeOk);
          setConfirmDesactivar(null);
        },
        onError: (error) => {
          toastActualizarError(error, 'Error al actualizar el operador.');
          setConfirmDesactivar(null);
        },
      },
    );
  }

  return (
    <section className="space-y-4">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <UserRound className="h-5 w-5" aria-hidden="true" />
            Operadores
          </h2>
          <p className="text-sm text-muted-foreground">
            Choferes para Carta Porte 3.1: RFC, nombre y número de licencia
            federal. Total: {operadores.length}.
          </p>
        </div>
        <div className="flex items-center gap-3">
          <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <input
              type="checkbox"
              className="size-4 rounded border-input"
              checked={mostrarInactivos}
              onChange={(e) => setMostrarInactivos(e.target.checked)}
            />
            Mostrar inactivos
          </label>
          {canGestionar && !agregando && (
            <Button
              size="sm"
              variant="outline"
              onClick={() => {
                setAgregando(true);
                setEditandoId(null);
              }}
            >
              <Plus className="mr-1 h-4 w-4" />
              Agregar operador
            </Button>
          )}
        </div>
      </header>

      {agregando && canGestionar && (
        <OperadorInlineForm
          onCancel={() => setAgregando(false)}
          onSaved={() => setAgregando(false)}
        />
      )}

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los operadores"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton rows={4} />
      ) : operadores.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          No hay operadores registrados.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {operadores.map((o) => {
            const editando = editandoId === o.id;
            return (
              <li key={o.id} className="px-3 py-2">
                {editando && canGestionar ? (
                  <OperadorInlineForm
                    operador={o}
                    onCancel={() => setEditandoId(null)}
                    onSaved={() => setEditandoId(null)}
                  />
                ) : (
                  <div className="flex flex-wrap items-center gap-3">
                    <span className="w-36 truncate font-mono text-xs">
                      {o.rfc}
                    </span>
                    <span className="flex-1 truncate text-sm">{o.nombre}</span>
                    <span
                      className="w-28 truncate font-mono text-xs text-muted-foreground"
                      title="Número de licencia federal"
                    >
                      {o.numLicencia}
                    </span>
                    {o.activo ? (
                      <Badge variant="secondary">Activo</Badge>
                    ) : (
                      <Badge variant="outline" className="text-muted-foreground">
                        Inactivo
                      </Badge>
                    )}
                    {canGestionar && (
                      <div className="flex items-center gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => {
                            setEditandoId(o.id);
                            setAgregando(false);
                          }}
                          aria-label={`Editar operador ${o.nombre}`}
                        >
                          <Pencil className="h-3.5 w-3.5" />
                        </Button>
                        {o.activo ? (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setConfirmDesactivar(o)}
                            aria-label={`Desactivar operador ${o.nombre}`}
                          >
                            <PowerOff className="h-3.5 w-3.5" />
                          </Button>
                        ) : (
                          <Button
                            variant="ghost"
                            size="sm"
                            disabled={actualizar.isPending}
                            onClick={() =>
                              cambiarActivo(o, true, `Operador ${o.nombre} reactivado`)
                            }
                            aria-label={`Reactivar operador ${o.nombre}`}
                          >
                            <Power className="h-3.5 w-3.5" />
                          </Button>
                        )}
                      </div>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      <AlertDialog
        open={confirmDesactivar != null}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar operador</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar al operador{' '}
              <span className="font-semibold">{confirmDesactivar?.nombre}</span>
              ? Saldrá del selector de emisión de Carta Porte, pero las
              Cartas Porte existentes conservan su histórico. Se puede
              reactivar en cualquier momento.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={actualizar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (confirmDesactivar == null) return;
                cambiarActivo(
                  confirmDesactivar,
                  false,
                  `Operador ${confirmDesactivar.nombre} desactivado`,
                );
              }}
              disabled={actualizar.isPending}
            >
              {actualizar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}
