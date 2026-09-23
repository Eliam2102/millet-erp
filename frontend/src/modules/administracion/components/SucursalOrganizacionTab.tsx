import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import {
  Briefcase,
  Building2,
  CheckCircle2,
  FolderTree,
  Plus,
  PowerOff,
  RotateCcw,
  Search,
} from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
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
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { useDepartamentos, usePuestos } from '@/features/catalogos/api';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import type {
  SucursalDepartamentoResponse,
  SucursalPuestoResponse,
} from '@/modules/administracion/api/types';
import {
  useAsignarDepartamentoASucursal,
  useAsignarPuestoASucursal,
  useDepartamentosDeSucursal,
  useDesactivarAsignacionSucursalDepartamento,
  useDesactivarAsignacionSucursalPuesto,
  usePuestosDeSucursal,
  useReactivarAsignacionSucursalDepartamento,
  useReactivarAsignacionSucursalPuesto,
} from '@/modules/administracion/api';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { SucursalTabErrorState } from '@/modules/administracion/components/SucursalTabErrorState';
import { cn } from '@/lib/utils';

export interface SucursalOrganizacionTabProps {
  sucursalId: string;
  canGestionarDeptos: boolean;
  canGestionarPuestos: boolean;
}

export function SucursalOrganizacionTab({
  sucursalId,
  canGestionarDeptos,
  canGestionarPuestos,
}: SucursalOrganizacionTabProps) {
  const canGestionarMasterDeptos = useHasPermission(
    PermisosCanonicos.AdminDepartamentosGestionar,
  );
  const canGestionarMasterPuestos = useHasPermission(
    PermisosCanonicos.AdminPuestosGestionar,
  );

  const deptosQuery = useDepartamentosDeSucursal(sucursalId);
  const puestosQuery = usePuestosDeSucursal(sucursalId);
  const catalogoDeptosQuery = useDepartamentos();
  const catalogoPuestosQuery = usePuestos();

  const [deptoSeleccionadoId, setDeptoSeleccionadoId] = useState<string | null>(null);
  const [filtroDeptos, setFiltroDeptos] = useState('');
  const [filtroPuestos, setFiltroPuestos] = useState('');
  const [modalDeptoOpen, setModalDeptoOpen] = useState(false);
  const [modalPuestoOpen, setModalPuestoOpen] = useState(false);

  const deptosAsignados = deptosQuery.data?.items ?? [];
  const puestosAsignados = puestosQuery.data?.items ?? [];

  // Mapear cantidad de puestos por departamento
  const puestosCountPorDepto = useMemo(() => {
    const map = new Map<string, number>();
    for (const p of puestosAsignados) {
      if (p.departamentoId) {
        map.set(p.departamentoId, (map.get(p.departamentoId) ?? 0) + 1);
      }
    }
    return map;
  }, [puestosAsignados]);

  // Filtrar departamentos según término de búsqueda
  const deptosFiltrados = useMemo(() => {
    const q = filtroDeptos.trim().toLowerCase();
    if (!q) return deptosAsignados;
    return deptosAsignados.filter(
      (d) =>
        d.departamentoClave.toLowerCase().includes(q) ||
        d.departamentoNombre.toLowerCase().includes(q),
    );
  }, [deptosAsignados, filtroDeptos]);

  // Departamento actualmente activo/seleccionado (por default el primero si hay)
  const departamentoActivo = useMemo(() => {
    if (deptosAsignados.length === 0) return null;
    if (deptoSeleccionadoId) {
      const match = deptosAsignados.find((d) => d.departamentoId === deptoSeleccionadoId);
      if (match) return match;
    }
    return deptosAsignados[0];
  }, [deptosAsignados, deptoSeleccionadoId]);

  // Puestos que pertenecen exclusivamente al departamento activo
  const puestosDelDeptoActivo = useMemo(() => {
    if (!departamentoActivo) return [];
    return puestosAsignados.filter((p) => p.departamentoId === departamentoActivo.departamentoId);
  }, [puestosAsignados, departamentoActivo]);

  // Filtrar puestos del departamento activo
  const puestosFiltrados = useMemo(() => {
    const q = filtroPuestos.trim().toLowerCase();
    if (!q) return puestosDelDeptoActivo;
    return puestosDelDeptoActivo.filter(
      (p) =>
        p.puestoClave.toLowerCase().includes(q) ||
        p.puestoNombre.toLowerCase().includes(q),
    );
  }, [puestosDelDeptoActivo, filtroPuestos]);

  if (deptosQuery.isError) {
    return (
      <SucursalTabErrorState
        error={deptosQuery.error}
        onRetry={() => deptosQuery.refetch()}
      />
    );
  }
  if (puestosQuery.isError) {
    return (
      <SucursalTabErrorState
        error={puestosQuery.error}
        onRetry={() => puestosQuery.refetch()}
      />
    );
  }

  const isLoading = deptosQuery.isLoading || puestosQuery.isLoading;
  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-28 w-full" />
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-3.5 h-[calc(100vh-14.5rem)] min-h-[580px]">
      {/* ─── SECCIÓN SUPERIOR: DEPARTAMENTOS ASIGNADOS (MITAD SUPERIOR) ─── */}
      <section className="flex flex-col flex-1 min-h-0 rounded-lg border bg-card/60 p-3.5 shadow-xs">
        {/* Cabecera fija de la sección superior */}
        <div className="shrink-0 flex flex-col gap-2.5 sm:flex-row sm:items-center sm:justify-between border-b pb-2.5">
          <div className="flex items-center gap-2">
            <Building2 className="h-4.5 w-4.5 text-primary shrink-0" />
            <div>
              <div className="flex items-center gap-2">
                <h2 className="text-sm font-semibold leading-none text-foreground">
                  Departamentos de la sucursal
                </h2>
                {deptosAsignados.length > 0 && (
                  <Badge variant="secondary" className="text-xs font-mono">
                    {deptosAsignados.length}
                  </Badge>
                )}
              </div>
              <p className="mt-1 text-xs text-muted-foreground line-clamp-1">
                Selecciona un departamento para ver y gestionar sus puestos asociados.
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            <div className="relative w-full sm:w-56">
              <Search className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-muted-foreground" />
              <Input
                placeholder="Buscar departamentos..."
                value={filtroDeptos}
                onChange={(e) => setFiltroDeptos(e.target.value)}
                className="h-8 pl-8 text-xs"
                aria-label="Buscar departamentos asignados"
              />
            </div>

            {canGestionarMasterDeptos && (
              <Button size="sm" variant="outline" className="h-8 whitespace-nowrap text-xs" asChild>
                <Link to="/admin/departamentos">
                  <FolderTree className="mr-1.5 h-3.5 w-3.5 text-muted-foreground" /> Catálogo
                </Link>
              </Button>
            )}

            {canGestionarDeptos && (
              <Button
                onClick={() => setModalDeptoOpen(true)}
                size="sm"
                className="h-8 whitespace-nowrap text-xs"
              >
                <Plus className="mr-1 h-3.5 w-3.5" /> Asignar departamento
              </Button>
            )}
          </div>
        </div>

        {/* Contenedor scrolleable de departamentos */}
        <div className="flex-1 min-h-0 overflow-y-auto pt-2.5 pr-1">
          {deptosAsignados.length === 0 ? (
            <div className="flex h-full min-h-[120px] flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 px-4 py-6 text-center">
              <Building2 className="mb-2 h-7 w-7 text-muted-foreground/60" />
              <h3 className="text-sm font-semibold">No hay departamentos asignados</h3>
              <p className="mt-1 text-xs text-muted-foreground max-w-sm">
                Esta sucursal aún no tiene departamentos vinculados. Asigna departamentos
                del catálogo para habilitar la asignación de puestos.
              </p>
              {canGestionarDeptos && (
                <Button
                  variant="outline"
                  size="sm"
                  className="mt-3 text-xs"
                  onClick={() => setModalDeptoOpen(true)}
                >
                  <Plus className="mr-1.5 h-3.5 w-3.5" /> Asignar primer departamento
                </Button>
              )}
            </div>
          ) : deptosFiltrados.length === 0 ? (
            <div className="rounded-md border border-dashed bg-muted/10 px-4 py-6 text-center text-sm text-muted-foreground">
              No se encontraron departamentos con el término &ldquo;{filtroDeptos}&rdquo;.
            </div>
          ) : (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
              {deptosFiltrados.map((item) => {
                const estaSeleccionado =
                  departamentoActivo?.departamentoId === item.departamentoId;
                const countPuestos = puestosCountPorDepto.get(item.departamentoId) ?? 0;

                return (
                  <TarjetaDepartamento
                    key={item.departamentoId}
                    item={item}
                    sucursalId={sucursalId}
                    countPuestos={countPuestos}
                    seleccionado={estaSeleccionado}
                    canGestionar={canGestionarDeptos}
                    onSelect={() => setDeptoSeleccionadoId(item.departamentoId)}
                  />
                );
              })}
            </div>
          )}
        </div>
      </section>

      {/* ─── SECCIÓN INFERIOR: PUESTOS DEL DEPARTAMENTO SELECCIONADO (MITAD INFERIOR) ─── */}
      <section className="flex flex-col flex-1 min-h-0 rounded-lg border bg-card/60 p-3.5 shadow-xs">
        {departamentoActivo ? (
          <>
            {/* Cabecera fija de la sección de puestos */}
            <div className="shrink-0 flex flex-col gap-2.5 sm:flex-row sm:items-center sm:justify-between border-b pb-2.5">
              <div className="flex items-center gap-2">
                <Briefcase className="h-4.5 w-4.5 text-primary shrink-0" />
                <div>
                  <div className="flex items-center gap-2">
                    <h3 className="text-sm font-semibold leading-none text-foreground">
                      Puestos de {departamentoActivo.departamentoClave} — {departamentoActivo.departamentoNombre}
                    </h3>
                    <Badge variant="outline" className="text-xs font-mono">
                      {puestosDelDeptoActivo.length}{' '}
                      {puestosDelDeptoActivo.length === 1 ? 'puesto' : 'puestos'}
                    </Badge>
                  </div>
                  <p className="mt-1 text-xs text-muted-foreground line-clamp-1">
                    Puestos asignados exclusivamente a este departamento en la sucursal.
                  </p>
                </div>
              </div>

              <div className="flex items-center gap-2 shrink-0">
                <div className="relative w-full sm:w-56">
                  <Search className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-muted-foreground" />
                  <Input
                    placeholder="Buscar puesto..."
                    value={filtroPuestos}
                    onChange={(e) => setFiltroPuestos(e.target.value)}
                    className="h-8 pl-8 text-xs"
                    aria-label="Buscar puestos del departamento"
                  />
                </div>

                {canGestionarMasterPuestos && (
                  <Button size="sm" variant="outline" className="h-8 whitespace-nowrap text-xs" asChild>
                    <Link to="/admin/puestos">
                      <Briefcase className="mr-1.5 h-3.5 w-3.5 text-muted-foreground" /> Catálogo
                    </Link>
                  </Button>
                )}

                {canGestionarPuestos && (
                  <Button
                    onClick={() => setModalPuestoOpen(true)}
                    size="sm"
                    className="h-8 whitespace-nowrap text-xs"
                  >
                    <Plus className="mr-1 h-3.5 w-3.5" /> Asignar puesto
                  </Button>
                )}
              </div>
            </div>

            {/* Contenedor scrolleable de puestos */}
            <div className="flex-1 min-h-0 overflow-y-auto pt-2.5 pr-1">
              {puestosDelDeptoActivo.length === 0 ? (
                <div className="flex h-full min-h-[120px] flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 px-4 py-6 text-center">
                  <FolderTree className="mb-2 h-7 w-7 text-muted-foreground/60" />
                  <h4 className="text-sm font-semibold">
                    No hay puestos asignados a {departamentoActivo.departamentoClave}
                  </h4>
                  <p className="mt-1 text-xs text-muted-foreground max-w-sm">
                    Asigna los puestos que operan dentro de este departamento en la sucursal.
                  </p>
                  {canGestionarPuestos && (
                    <Button
                      variant="outline"
                      size="sm"
                      className="mt-3 text-xs"
                      onClick={() => setModalPuestoOpen(true)}
                    >
                      <Plus className="mr-1.5 h-3.5 w-3.5" /> Asignar puesto a este departamento
                    </Button>
                  )}
                </div>
              ) : puestosFiltrados.length === 0 ? (
                <div className="rounded-md border border-dashed bg-muted/10 px-4 py-6 text-center text-sm text-muted-foreground">
                  No se encontraron puestos que coincidan con &ldquo;{filtroPuestos}&rdquo;.
                </div>
              ) : (
                <ul className="divide-y rounded-md border bg-card">
                  {puestosFiltrados.map((item) => (
                    <FilaPuestoDepartamento
                      key={item.puestoId}
                      sucursalId={sucursalId}
                      item={item}
                      canGestionar={canGestionarPuestos}
                    />
                  ))}
                </ul>
              )}
            </div>
          </>
        ) : (
          <div className="flex h-full min-h-[140px] flex-col items-center justify-center text-center text-sm text-muted-foreground">
            <Building2 className="mb-2 h-7 w-7 text-muted-foreground/40" />
            Primero asigna o selecciona un departamento arriba para ver sus puestos asociados.
          </div>
        )}
      </section>

      {/* Modal para asignar nuevos departamentos a la sucursal */}
      <AsignarDepartamentoModal
        open={modalDeptoOpen}
        onOpenChange={setModalDeptoOpen}
        sucursalId={sucursalId}
        asignados={deptosAsignados}
        catalogoQuery={catalogoDeptosQuery}
      />

      {/* Modal para asignar puesto al departamento activo */}
      {departamentoActivo && (
        <AsignarPuestoADepartamentoModal
          open={modalPuestoOpen}
          onOpenChange={setModalPuestoOpen}
          sucursalId={sucursalId}
          departamento={departamentoActivo}
          puestosAsignados={puestosAsignados}
          catalogoQuery={catalogoPuestosQuery}
        />
      )}
    </div>
  );
}

// ─── Subcomponente: Tarjeta de Departamento (Lista Superior) ───────

interface TarjetaDepartamentoProps {
  item: SucursalDepartamentoResponse;
  sucursalId: string;
  countPuestos: number;
  seleccionado: boolean;
  canGestionar: boolean;
  onSelect: () => void;
}

function TarjetaDepartamento({
  item,
  sucursalId,
  countPuestos,
  seleccionado,
  canGestionar,
  onSelect,
}: TarjetaDepartamentoProps) {
  const desactivar = useDesactivarAsignacionSucursalDepartamento();
  const reactivar = useReactivarAsignacionSucursalDepartamento();
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const activo = item.estatus === EstatusCatalogo.Activo;
  const inactivo = item.estatus === EstatusCatalogo.Inactivo;

  function handleConfirmarDesactivar(e: React.MouseEvent) {
    e.stopPropagation();
    desactivar.mutate(
      {
        sucursalId,
        departamentoId: item.departamentoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(`${item.departamentoClave} desactivado`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          handleError(`desactivar ${item.departamentoClave}`)(error);
          setConfirmDesactivar(false);
        },
      },
    );
  }

  function handleReactivar(e: React.MouseEvent) {
    e.stopPropagation();
    reactivar.mutate(
      {
        sucursalId,
        departamentoId: item.departamentoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => toast.success(`${item.departamentoClave} reactivado`),
        onError: handleError(`reactivar ${item.departamentoClave}`),
      },
    );
  }

  return (
    <div
      onClick={onSelect}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') onSelect();
      }}
      className={cn(
        'group relative flex flex-col justify-between rounded-lg border p-3 text-left transition-all cursor-pointer',
        seleccionado
          ? 'border-primary bg-primary/5 ring-1 ring-primary shadow-xs'
          : 'border-border/80 bg-card hover:border-primary/50 hover:bg-muted/30',
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="font-mono text-sm font-bold text-primary">
              {item.departamentoClave}
            </span>
            {seleccionado && (
              <Badge variant="default" className="text-[10px] px-1.5 py-0 h-4">
                Activo
              </Badge>
            )}
          </div>
          <p className="mt-1 truncate text-sm font-medium text-foreground">
            {item.departamentoNombre}
          </p>
        </div>

        <div className="flex items-center gap-1.5" onClick={(e) => e.stopPropagation()}>
          {canGestionar && activo && (
            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7 text-muted-foreground hover:text-destructive"
              onClick={() => setConfirmDesactivar(true)}
              aria-label={`Desactivar departamento ${item.departamentoClave}`}
            >
              <PowerOff className="h-3.5 w-3.5" />
            </Button>
          )}
          {canGestionar && inactivo && (
            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7 text-muted-foreground hover:text-primary"
              onClick={handleReactivar}
              aria-label={`Reactivar departamento ${item.departamentoClave}`}
            >
              <RotateCcw className="h-3.5 w-3.5" />
            </Button>
          )}
        </div>
      </div>

      <div className="mt-3 flex items-center justify-between border-t pt-2 text-xs">
        <span className="flex items-center text-muted-foreground">
          <Briefcase className="mr-1 h-3 w-3" />
          {countPuestos} {countPuestos === 1 ? 'puesto' : 'puestos'}
        </span>
        {activo ? (
          <Badge variant="secondary" className="text-[10px] bg-emerald-500/10 text-emerald-700 dark:text-emerald-400">
            Habilitado
          </Badge>
        ) : (
          <Badge variant="outline" className="text-[10px] text-muted-foreground">
            Inactivo
          </Badge>
        )}
      </div>

      <AlertDialog
        open={confirmDesactivar}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(false);
        }}
      >
        <AlertDialogContent onClick={(e) => e.stopPropagation()}>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar departamento</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar{' '}
              <span className="font-mono font-semibold">{item.departamentoClave}</span> en
              esta sucursal?
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desactivar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={handleConfirmarDesactivar}
              disabled={desactivar.isPending}
            >
              {desactivar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

// ─── Subcomponente: Fila de Puesto (Lista Inferior) ───────────────

interface FilaPuestoDepartamentoProps {
  sucursalId: string;
  item: SucursalPuestoResponse;
  canGestionar: boolean;
}

function FilaPuestoDepartamento({
  sucursalId,
  item,
  canGestionar,
}: FilaPuestoDepartamentoProps) {
  const desactivar = useDesactivarAsignacionSucursalPuesto();
  const reactivar = useReactivarAsignacionSucursalPuesto();
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const activo = item.estatus === EstatusCatalogo.Activo;
  const inactivo = item.estatus === EstatusCatalogo.Inactivo;

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      {
        sucursalId,
        puestoId: item.puestoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(`${item.puestoClave} desactivado`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          handleError(`desactivar ${item.puestoClave}`)(error);
          setConfirmDesactivar(false);
        },
      },
    );
  }

  function handleReactivar() {
    reactivar.mutate(
      {
        sucursalId,
        puestoId: item.puestoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => toast.success(`${item.puestoClave} reactivado`),
        onError: handleError(`reactivar ${item.puestoClave}`),
      },
    );
  }

  const pending = desactivar.isPending || reactivar.isPending;

  return (
    <li className="flex items-center justify-between gap-3 px-4 py-3">
      <div className="flex flex-wrap items-center gap-3">
        <span className="font-mono text-sm font-semibold text-primary">
          {item.puestoClave}
        </span>
        <span className="text-sm font-medium text-foreground">
          {item.puestoNombre}
        </span>
        {activo && (
          <Badge variant="secondary" className="bg-emerald-500/10 text-emerald-700 dark:text-emerald-400">
            Activo
          </Badge>
        )}
        {inactivo && (
          <Badge variant="outline" className="text-muted-foreground">
            Inactivo
          </Badge>
        )}
      </div>

      <div className="flex items-center gap-2">
        {canGestionar && activo && (
          <Button
            variant="ghost"
            size="sm"
            disabled={pending}
            onClick={() => setConfirmDesactivar(true)}
            aria-label={`Desactivar puesto ${item.puestoClave}`}
            className="text-muted-foreground hover:text-destructive"
          >
            <PowerOff className="h-4 w-4 mr-1" /> Desactivar
          </Button>
        )}
        {canGestionar && inactivo && (
          <Button
            variant="ghost"
            size="sm"
            disabled={pending}
            onClick={handleReactivar}
            aria-label={`Reactivar puesto ${item.puestoClave}`}
          >
            <RotateCcw className="mr-1 h-3.5 w-3.5" /> Reactivar
          </Button>
        )}
      </div>

      <AlertDialog
        open={confirmDesactivar}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(false);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar puesto</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar{' '}
              <span className="font-mono font-semibold">{item.puestoClave}</span> en
              esta sucursal? Bloquea nuevas asignaciones de empleados con esta combinación.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desactivar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={handleConfirmarDesactivar}
              disabled={desactivar.isPending}
            >
              {desactivar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </li>
  );
}

// ─── Subcomponente: Modal para Asignar Puesto al Departamento Activo ───

interface AsignarPuestoADepartamentoModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sucursalId: string;
  departamento: SucursalDepartamentoResponse;
  puestosAsignados: SucursalPuestoResponse[];
  catalogoQuery: ReturnType<typeof usePuestos>;
}

function AsignarPuestoADepartamentoModal({
  open,
  onOpenChange,
  sucursalId,
  departamento,
  puestosAsignados,
  catalogoQuery,
}: AsignarPuestoADepartamentoModalProps) {
  const asignar = useAsignarPuestoASucursal();
  const [puestoId, setPuestoId] = useState('');
  const [busquedaPuesto, setBusquedaPuesto] = useState('');

  // Filtrar puestos que ya están asignados a esta sucursal
  const puestosDisponibles = useMemo(() => {
    const catalogo = catalogoQuery.data?.items ?? [];
    const asignadosIds = new Set(puestosAsignados.map((a) => a.puestoId));
    return catalogo
      .filter((p) => !asignadosIds.has(p.id))
      .sort((a, b) => a.clave.localeCompare(b.clave));
  }, [catalogoQuery.data, puestosAsignados]);

  const puestosFiltrados = useMemo(() => {
    const q = busquedaPuesto.trim().toLowerCase();
    if (!q) return puestosDisponibles;
    return puestosDisponibles.filter(
      (p) =>
        p.clave.toLowerCase().includes(q) ||
        p.nombre.toLowerCase().includes(q),
    );
  }, [puestosDisponibles, busquedaPuesto]);

  function handleOpenChange(next: boolean) {
    if (!next) {
      setPuestoId('');
      setBusquedaPuesto('');
    }
    onOpenChange(next);
  }

  function handleConfirmar() {
    if (!puestoId) return;

    asignar.mutate(
      {
        sucursalId,
        puestoId,
        departamentoId: departamento.departamentoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(
            `Puesto asignado exitosamente al departamento ${departamento.departamentoClave}`,
          );
          handleOpenChange(false);
        },
        onError: handleError('asignar puesto'),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Asignar puesto al departamento</DialogTitle>
          <DialogDescription>
            El puesto pertenecerá al departamento{' '}
            <strong className="text-foreground">
              {departamento.departamentoClave} — {departamento.departamentoNombre}
            </strong>{' '}
            en esta sucursal.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-2">
          {catalogoQuery.isLoading ? (
            <div className="space-y-3">
              <Skeleton className="h-9 w-full" />
              <Skeleton className="h-9 w-full" />
            </div>
          ) : puestosDisponibles.length === 0 ? (
            <div className="rounded-md border border-dashed bg-muted/20 p-4 text-center text-sm text-muted-foreground space-y-2">
              <p>Todos los puestos del catálogo maestro ya están asignados a esta sucursal.</p>
              <Button size="sm" variant="outline" className="text-xs" asChild>
                <Link to="/admin/puestos">
                  <Briefcase className="mr-1.5 h-3.5 w-3.5" /> Ir al catálogo de Puestos
                </Link>
              </Button>
            </div>
          ) : (
            <>
              {puestosDisponibles.length > 8 && (
                <div className="relative">
                  <Search className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-muted-foreground" />
                  <Input
                    placeholder="Filtrar puestos disponibles..."
                    value={busquedaPuesto}
                    onChange={(e) => setBusquedaPuesto(e.target.value)}
                    className="h-8 pl-8 text-xs"
                  />
                </div>
              )}

              <div className="space-y-1.5">
                <label
                  htmlFor="puesto-select-depto"
                  className="text-xs font-semibold uppercase tracking-wider text-foreground"
                >
                  Puesto *
                </label>
                <select
                  id="puesto-select-depto"
                  aria-label="Puesto a asignar"
                  className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm shadow-xs focus:outline-hidden focus:ring-2 focus:ring-ring"
                  value={puestoId}
                  onChange={(e) => setPuestoId(e.target.value)}
                  disabled={asignar.isPending}
                >
                  <option value="">
                    -- Selecciona un puesto ({puestosFiltrados.length} disponibles) --
                  </option>
                  {puestosFiltrados.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.clave} — {p.nombre}
                    </option>
                  ))}
                </select>
              </div>

              <div className="rounded-md bg-muted/50 p-2.5 text-xs text-muted-foreground flex items-center gap-2">
                <CheckCircle2 className="h-4 w-4 text-primary shrink-0" />
                <span>
                  Departamento destino:{' '}
                  <strong className="text-foreground">
                    {departamento.departamentoClave} — {departamento.departamentoNombre}
                  </strong>
                </span>
              </div>
            </>
          )}
        </div>

        <DialogFooter className="gap-2 sm:gap-0">
          <Button
            variant="outline"
            onClick={() => handleOpenChange(false)}
            disabled={asignar.isPending}
          >
            Cancelar
          </Button>
          <Button
            onClick={handleConfirmar}
            disabled={asignar.isPending || !puestoId || puestosDisponibles.length === 0}
          >
            {asignar.isPending ? 'Asignando…' : 'Asignar puesto'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ─── Subcomponente: Modal para Asignar Departamento del Catálogo ───

interface AsignarDepartamentoModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sucursalId: string;
  asignados: SucursalDepartamentoResponse[];
  catalogoQuery: ReturnType<typeof useDepartamentos>;
}

function AsignarDepartamentoModal({
  open,
  onOpenChange,
  sucursalId,
  asignados,
  catalogoQuery,
}: AsignarDepartamentoModalProps) {
  const [departamentoSeleccionadoId, setDepartamentoSeleccionadoId] = useState('');
  const [busquedaCatalogo, setBusquedaCatalogo] = useState('');
  const asignar = useAsignarDepartamentoASucursal();

  const disponibles = useMemo(() => {
    const catalogo = catalogoQuery.data?.items ?? [];
    const asignadosIds = new Set(asignados.map((a) => a.departamentoId));
    return catalogo
      .filter((d) => !asignadosIds.has(d.id))
      .sort((a, b) => a.clave.localeCompare(b.clave));
  }, [catalogoQuery.data, asignados]);

  const disponiblesFiltrados = useMemo(() => {
    const q = busquedaCatalogo.trim().toLowerCase();
    if (!q) return disponibles;
    return disponibles.filter(
      (d) =>
        d.clave.toLowerCase().includes(q) ||
        d.nombre.toLowerCase().includes(q),
    );
  }, [disponibles, busquedaCatalogo]);

  function handleAsignar() {
    if (!departamentoSeleccionadoId) return;
    const depto = disponibles.find((d) => d.id === departamentoSeleccionadoId);

    asignar.mutate(
      {
        sucursalId,
        departamentoId: departamentoSeleccionadoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(
            depto ? `Departamento ${depto.clave} asignado` : 'Departamento asignado',
          );
          setDepartamentoSeleccionadoId('');
          setBusquedaCatalogo('');
          onOpenChange(false);
        },
        onError: handleError('asignar departamento'),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Asignar departamento a la sucursal</DialogTitle>
          <DialogDescription>
            Selecciona un departamento del catálogo maestro para habilitarlo en esta
            sucursal.
          </DialogDescription>
        </DialogHeader>

        {catalogoQuery.isLoading ? (
          <div className="space-y-3 py-2">
            <Skeleton className="h-9 w-full" />
            <Skeleton className="h-9 w-full" />
          </div>
        ) : disponibles.length === 0 ? (
          <div className="rounded-md border border-dashed bg-muted/20 p-4 text-center text-sm text-muted-foreground space-y-2">
            <p>Todos los departamentos del catálogo maestro ya están asignados a esta sucursal.</p>
            <Button size="sm" variant="outline" className="text-xs" asChild>
              <Link to="/admin/departamentos">
                <FolderTree className="mr-1.5 h-3.5 w-3.5" /> Ir al catálogo de Departamentos
              </Link>
            </Button>
          </div>
        ) : (
          <div className="space-y-3 py-2">
            {disponibles.length > 8 && (
              <div className="relative">
                <Search className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-muted-foreground" />
                <Input
                  placeholder="Filtrar departamentos disponibles..."
                  value={busquedaCatalogo}
                  onChange={(e) => setBusquedaCatalogo(e.target.value)}
                  className="h-8 pl-8 text-xs"
                />
              </div>
            )}

            <div className="space-y-1.5">
              <Label htmlFor="select-depto-modal" className="text-xs font-medium">
                Departamento *
              </Label>
              <select
                id="select-depto-modal"
                aria-label="Departamento a asignar"
                value={departamentoSeleccionadoId}
                onChange={(e) => setDepartamentoSeleccionadoId(e.target.value)}
                className="flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
              >
                <option value="">-- Selecciona un departamento ({disponiblesFiltrados.length} disponibles) --</option>
                {disponiblesFiltrados.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.clave} - {d.nombre}
                  </option>
                ))}
              </select>
            </div>
          </div>
        )}

        <DialogFooter className="gap-2 sm:gap-0">
          <Button
            type="button"
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={asignar.isPending}
          >
            Cancelar
          </Button>
          <Button
            type="button"
            onClick={handleAsignar}
            disabled={!departamentoSeleccionadoId || asignar.isPending || disponibles.length === 0}
          >
            {asignar.isPending ? 'Asignando…' : 'Asignar a sucursal'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function handleError(accion: string) {
  return (error: unknown) => {
    if (esApiError(error)) {
      toast.error(error.problem.title, {
        description: error.traceId ? `Código: ${error.traceId}` : undefined,
      });
    } else {
      toast.error(`Error al ${accion}.`);
    }
  };
}
