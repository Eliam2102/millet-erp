import { useMemo, useState } from 'react';
import { Briefcase, Check, Pencil, Plus, Search, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { usePuestos } from '@/features/catalogos/api';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import type { SucursalPuestoResponse } from '@/modules/administracion/api/types';
import {
  useActualizarRolSugeridoAsignacion,
  useAsignarPuestoASucursal,
  usePuestosDeSucursal,
  useDepartamentosDeSucursal,
  useDesactivarAsignacionSucursalPuesto,
  useReactivarAsignacionSucursalPuesto,
} from '@/modules/administracion/api';
import { useRoles } from '@/modules/identidad/api/roles';
import type { RolResponse } from '@/modules/identidad/api/types';
import { esApiError } from '@/lib/api';
import { SucursalTabErrorState } from '@/modules/administracion/components/SucursalTabErrorState';
import { FilaAsignacionSucursal } from '@/modules/administracion/components/FilaAsignacionSucursal';

export interface SucursalPuestosTabProps {
  sucursalId: string;
  canGestionar: boolean;
}

/**
 * Un puesto agrupado con todas sus asignaciones (departamentos) dentro
 * de la sucursal. El mismo puesto genérico (p.ej. "Gerente") puede
 * tener varias filas — una por departamento en el que participa
 * (Parte E "un puesto en varios departamentos de la sucursal",
 * 2026-09-24).
 */
interface PuestoAgrupado {
  puestoId: string;
  puestoClave: string;
  puestoNombre: string;
  asignaciones: SucursalPuestoResponse[];
}

export function SucursalPuestosTab({
  sucursalId,
  canGestionar,
}: SucursalPuestosTabProps) {
  const catalogoQuery = usePuestos();
  const asignadosQuery = usePuestosDeSucursal(sucursalId);
  const deptosSucursalQuery = useDepartamentosDeSucursal(sucursalId);
  const rolesQuery = useRoles({ soloActivos: true, limit: 200 });

  const [filtro, setFiltro] = useState('');
  const [modalAsignarOpen, setModalAsignarOpen] = useState(false);

  const asignados = asignadosQuery.data?.items ?? [];
  const roles = rolesQuery.data?.items ?? [];

  const deptosActivos = useMemo(() => {
    return (deptosSucursalQuery.data?.items ?? [])
      .filter((d) => d.estatus === EstatusCatalogo.Activo)
      .map((d) => ({
        id: d.departamentoId,
        clave: d.departamentoClave,
        nombre: d.departamentoNombre,
      }));
  }, [deptosSucursalQuery.data]);

  // Agrupar las asignaciones (una fila por puesto+departamento) por
  // puesto para que la lista muestre "un puesto, varios departamentos"
  // en vez de repetir el puesto una vez por cada fila.
  const grupos = useMemo<PuestoAgrupado[]>(() => {
    const map = new Map<string, PuestoAgrupado>();
    for (const item of asignados) {
      let grupo = map.get(item.puestoId);
      if (!grupo) {
        grupo = {
          puestoId: item.puestoId,
          puestoClave: item.puestoClave,
          puestoNombre: item.puestoNombre,
          asignaciones: [],
        };
        map.set(item.puestoId, grupo);
      }
      grupo.asignaciones.push(item);
    }
    return Array.from(map.values()).sort((a, b) =>
      a.puestoClave.localeCompare(b.puestoClave),
    );
  }, [asignados]);

  const gruposFiltrados = useMemo(() => {
    const q = filtro.trim().toLowerCase();
    if (!q) return grupos;
    return grupos.filter(
      (g) =>
        g.puestoClave.toLowerCase().includes(q) ||
        g.puestoNombre.toLowerCase().includes(q) ||
        g.asignaciones.some((a) =>
          a.departamentoNombre?.toLowerCase().includes(q),
        ),
    );
  }, [grupos, filtro]);

  if (asignadosQuery.isError) {
    return (
      <SucursalTabErrorState
        error={asignadosQuery.error}
        onRetry={() => asignadosQuery.refetch()}
      />
    );
  }
  if (deptosSucursalQuery.isError) {
    return (
      <SucursalTabErrorState
        error={deptosSucursalQuery.error}
        onRetry={() => deptosSucursalQuery.refetch()}
      />
    );
  }

  const isLoading = asignadosQuery.isLoading || deptosSucursalQuery.isLoading;

  if (isLoading) {
    return (
      <div className="space-y-2">
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-14 w-full" />
        <Skeleton className="h-14 w-full" />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Barra superior de herramientas */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-2">
          <div className="relative w-full sm:w-72">
            <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
            <Input
              placeholder="Buscar puesto o departamento..."
              value={filtro}
              onChange={(e) => setFiltro(e.target.value)}
              className="pl-8"
              aria-label="Buscar puestos asignados"
            />
          </div>
          <span className="text-xs text-muted-foreground whitespace-nowrap">
            {grupos.length} {grupos.length === 1 ? 'puesto' : 'puestos'} ·{' '}
            {asignados.length} {asignados.length === 1 ? 'asignación' : 'asignaciones'}
          </span>
        </div>

        {canGestionar && (
          <Button
            onClick={() => setModalAsignarOpen(true)}
            size="sm"
            className="self-start sm:self-auto"
          >
            <Plus className="mr-1.5 h-4 w-4" /> Asignar puesto
          </Button>
        )}
      </div>

      {/* Lista de puestos agrupados o empty state */}
      {grupos.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 px-6 py-10 text-center">
          <Briefcase className="mb-3 h-10 w-10 text-muted-foreground/60" />
          <h3 className="text-sm font-semibold">No hay puestos asignados</h3>
          <p className="mt-1 text-xs text-muted-foreground max-w-sm">
            Esta sucursal aún no tiene puestos vinculados. Asocia puestos del catálogo
            a uno o varios departamentos activos de esta sucursal.
          </p>
          {canGestionar && (
            <Button
              variant="outline"
              size="sm"
              className="mt-4"
              onClick={() => setModalAsignarOpen(true)}
            >
              <Plus className="mr-1.5 h-4 w-4" /> Asignar primer puesto
            </Button>
          )}
        </div>
      ) : gruposFiltrados.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/10 px-4 py-8 text-center text-sm text-muted-foreground">
          No se encontraron puestos asignados que coincidan con &ldquo;{filtro}&rdquo;.
        </div>
      ) : (
        <div className="space-y-3">
          {gruposFiltrados.map((grupo) => (
            <GrupoPuestoCard
              key={grupo.puestoId}
              sucursalId={sucursalId}
              grupo={grupo}
              canGestionar={canGestionar}
              roles={roles}
            />
          ))}
        </div>
      )}

      {/* Modal para asignar puestos */}
      <AsignarPuestoModal
        open={modalAsignarOpen}
        onOpenChange={setModalAsignarOpen}
        sucursalId={sucursalId}
        asignados={asignados}
        deptosActivos={deptosActivos}
        catalogoQuery={catalogoQuery}
      />
    </div>
  );
}

// ─── Tarjeta de puesto agrupado (sus departamentos como filas) ────

interface GrupoPuestoCardProps {
  sucursalId: string;
  grupo: PuestoAgrupado;
  canGestionar: boolean;
  roles: RolResponse[];
}

function GrupoPuestoCard({
  sucursalId,
  grupo,
  canGestionar,
  roles,
}: GrupoPuestoCardProps) {
  return (
    <div className="rounded-md border bg-card">
      <div className="flex flex-wrap items-center gap-2 border-b bg-muted/30 px-4 py-2.5">
        <span className="font-mono text-sm font-semibold text-primary">
          {grupo.puestoClave}
        </span>
        <span className="text-sm font-medium text-foreground">
          {grupo.puestoNombre}
        </span>
        <Badge variant="outline" className="text-xs font-mono">
          {grupo.asignaciones.length}{' '}
          {grupo.asignaciones.length === 1 ? 'departamento' : 'departamentos'}
        </Badge>
      </div>
      <ul className="divide-y">
        {grupo.asignaciones
          .slice()
          .sort((a, b) =>
            (a.departamentoNombre ?? a.departamentoId).localeCompare(
              b.departamentoNombre ?? b.departamentoId,
            ),
          )
          .map((item) => (
            <AsignacionDepartamentoRow
              key={`${item.puestoId}-${item.departamentoId}`}
              sucursalId={sucursalId}
              item={item}
              canGestionar={canGestionar}
              roles={roles}
            />
          ))}
      </ul>
    </div>
  );
}

// ─── Fila de una asignación puntual (puesto+departamento) ─────────

interface AsignacionDepartamentoRowProps {
  sucursalId: string;
  item: SucursalPuestoResponse;
  canGestionar: boolean;
  roles: RolResponse[];
}

function AsignacionDepartamentoRow({
  sucursalId,
  item,
  canGestionar,
  roles,
}: AsignacionDepartamentoRowProps) {
  const desactivar = useDesactivarAsignacionSucursalPuesto();
  const reactivar = useReactivarAsignacionSucursalPuesto();
  const [editandoRol, setEditandoRol] = useState(false);

  const etiquetaAsignacion = `${item.puestoClave} en ${item.departamentoNombre ?? item.departamentoId}`;

  function handleConfirmarDesactivar(cerrar: () => void) {
    desactivar.mutate(
      {
        sucursalId,
        puestoId: item.puestoId,
        departamentoId: item.departamentoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => toast.success(`${etiquetaAsignacion} desactivado`),
        onError: handleError(`desactivar ${etiquetaAsignacion}`),
        onSettled: cerrar,
      },
    );
  }

  function handleReactivar() {
    reactivar.mutate(
      {
        sucursalId,
        puestoId: item.puestoId,
        departamentoId: item.departamentoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => toast.success(`${etiquetaAsignacion} reactivado`),
        onError: handleError(`reactivar ${etiquetaAsignacion}`),
      },
    );
  }

  if (editandoRol) {
    return (
      <RolSugeridoInlineForm
        sucursalId={sucursalId}
        item={item}
        roles={roles}
        onCancel={() => setEditandoRol(false)}
        onSaved={() => setEditandoRol(false)}
      />
    );
  }

  const rolNombre = (id: string | null) =>
    roles.find((r) => r.id === id)?.nombre ?? '—';
  const tieneExcepcion = item.rolSugeridoId != null;
  const heredaDelPuesto = !tieneExcepcion && item.rolSugeridoEfectivoId != null;

  return (
    <FilaAsignacionSucursal
      estatus={item.estatus}
      canGestionar={canGestionar}
      etiquetaAccesible={`asignación ${etiquetaAsignacion}`}
      tituloConfirmacion="Desactivar asignación"
      descripcionConfirmacion={
        <>
          ¿Confirmas desactivar{' '}
          <span className="font-mono font-semibold">{item.puestoClave}</span> en{' '}
          <span className="font-semibold">
            {item.departamentoNombre ?? item.departamentoId}
          </span>
          ? Bloquea nuevas asignaciones de empleados con esta combinación pero
          NO afecta las existentes.
        </>
      }
      desactivando={desactivar.isPending}
      reactivando={reactivar.isPending}
      onConfirmarDesactivar={handleConfirmarDesactivar}
      onReactivar={handleReactivar}
    >
      <span className="text-sm font-medium text-foreground">
        {item.departamentoNombre ?? item.departamentoId}
      </span>
      <span className="text-xs text-muted-foreground">
        {tieneExcepcion ? (
          <>
            Rol sugerido:{' '}
            <span className="font-medium text-foreground">
              {rolNombre(item.rolSugeridoId)}
            </span>
          </>
        ) : heredaDelPuesto ? (
          <>
            Hereda del puesto:{' '}
            <span className="font-medium text-foreground">
              {rolNombre(item.rolSugeridoEfectivoId)}
            </span>
          </>
        ) : (
          'Sin rol sugerido'
        )}
      </span>
      {canGestionar && (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="h-6 px-2 text-xs text-muted-foreground"
          onClick={() => setEditandoRol(true)}
          aria-label={`Editar rol sugerido de ${etiquetaAsignacion}`}
        >
          <Pencil className="mr-1 h-3 w-3" /> Rol sugerido
        </Button>
      )}
    </FilaAsignacionSucursal>
  );
}

// ─── Inline form (sin modal) para el rol sugerido de la asignación ─

interface RolSugeridoInlineFormProps {
  sucursalId: string;
  item: SucursalPuestoResponse;
  roles: RolResponse[];
  onCancel: () => void;
  onSaved: () => void;
}

function RolSugeridoInlineForm({
  sucursalId,
  item,
  roles,
  onCancel,
  onSaved,
}: RolSugeridoInlineFormProps) {
  const actualizar = useActualizarRolSugeridoAsignacion();
  const [rolId, setRolId] = useState(item.rolSugeridoId ?? '');

  const etiquetaAsignacion = `${item.puestoClave} en ${item.departamentoNombre ?? item.departamentoId}`;

  function handleGuardar() {
    actualizar.mutate(
      {
        sucursalId,
        puestoId: item.puestoId,
        departamentoId: item.departamentoId,
        rolSugeridoId: rolId || null,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(`Rol sugerido de ${etiquetaAsignacion} actualizado`);
          onSaved();
        },
        onError: handleError(`actualizar el rol sugerido de ${etiquetaAsignacion}`),
      },
    );
  }

  return (
    <li
      className="flex flex-col gap-2 border-l-4 border-amber-400 bg-amber-50/40 px-4 py-3 dark:bg-amber-950/10 sm:flex-row sm:items-center sm:justify-between"
      aria-label={`Editar rol sugerido de ${etiquetaAsignacion}`}
    >
      <div className="flex flex-1 flex-wrap items-center gap-2">
        <span className="text-sm font-medium text-foreground">
          {item.departamentoNombre ?? item.departamentoId}
        </span>
        <select
          aria-label={`Rol sugerido para ${etiquetaAsignacion}`}
          className="h-8 min-w-48 rounded-md border border-input bg-background px-2 text-sm shadow-xs focus:outline-hidden focus:ring-2 focus:ring-ring"
          value={rolId}
          onChange={(e) => setRolId(e.target.value)}
          disabled={actualizar.isPending}
        >
          <option value="">Hereda del puesto</option>
          {roles.map((r) => (
            <option key={r.id} value={r.id}>
              {r.nombre}
            </option>
          ))}
        </select>
      </div>
      <div className="flex items-center gap-2">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onCancel}
          disabled={actualizar.isPending}
        >
          <X className="mr-1 h-3.5 w-3.5" /> Cancelar
        </Button>
        <Button
          type="button"
          size="sm"
          onClick={handleGuardar}
          disabled={actualizar.isPending}
        >
          <Check className="mr-1 h-3.5 w-3.5" />
          {actualizar.isPending ? 'Guardando…' : 'Guardar'}
        </Button>
      </div>
    </li>
  );
}

// ─── Modal para asignar un puesto a uno o varios departamentos ────

interface AsignarPuestoModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sucursalId: string;
  asignados: SucursalPuestoResponse[];
  deptosActivos: Array<{ id: string; clave: string; nombre: string }>;
  catalogoQuery: ReturnType<typeof usePuestos>;
}

function AsignarPuestoModal({
  open,
  onOpenChange,
  sucursalId,
  asignados,
  deptosActivos,
  catalogoQuery,
}: AsignarPuestoModalProps) {
  const asignar = useAsignarPuestoASucursal();
  const [puestoId, setPuestoId] = useState('');
  const [departamentosSeleccionados, setDepartamentosSeleccionados] = useState<
    Set<string>
  >(new Set());
  const [busquedaPuesto, setBusquedaPuesto] = useState('');
  const [enviando, setEnviando] = useState(false);

  // Catálogo completo: un mismo puesto puede tener más asignaciones en
  // OTROS departamentos de la sucursal, así que ya NO se excluye por
  // estar asignado — se excluye por combinación (puesto, departamento).
  const puestosCatalogo = useMemo(() => {
    const catalogo = catalogoQuery.data?.items ?? [];
    return [...catalogo].sort((a, b) => a.clave.localeCompare(b.clave));
  }, [catalogoQuery.data]);

  const puestosFiltrados = useMemo(() => {
    const q = busquedaPuesto.trim().toLowerCase();
    if (!q) return puestosCatalogo;
    return puestosCatalogo.filter(
      (p) =>
        p.clave.toLowerCase().includes(q) ||
        p.nombre.toLowerCase().includes(q),
    );
  }, [puestosCatalogo, busquedaPuesto]);

  const puestoSeleccionado = useMemo(
    () => puestosCatalogo.find((p) => p.id === puestoId) ?? null,
    [puestosCatalogo, puestoId],
  );

  // Departamentos activos de la sucursal donde este puesto AÚN no
  // tiene fila (activa o inactiva — reactivar usa el botón dedicado,
  // no un nuevo POST).
  const departamentosDisponibles = useMemo(() => {
    if (!puestoId) return deptosActivos;
    const yaTieneFila = new Set(
      asignados.filter((a) => a.puestoId === puestoId).map((a) => a.departamentoId),
    );
    return deptosActivos.filter((d) => !yaTieneFila.has(d.id));
  }, [deptosActivos, asignados, puestoId]);

  function handleSelectPuesto(id: string) {
    setPuestoId(id);
    const p = puestosCatalogo.find((item) => item.id === id);
    // Si el puesto trae un departamento de referencia del catálogo
    // maestro y está disponible en esta sucursal, se preselecciona
    // como conveniencia — sigue siendo editable.
    if (p?.departamentoId) {
      const yaTieneFila = new Set(
        asignados.filter((a) => a.puestoId === id).map((a) => a.departamentoId),
      );
      if (
        deptosActivos.some((d) => d.id === p.departamentoId) &&
        !yaTieneFila.has(p.departamentoId)
      ) {
        setDepartamentosSeleccionados(new Set([p.departamentoId]));
        return;
      }
    }
    setDepartamentosSeleccionados(new Set());
  }

  function toggleDepartamento(id: string) {
    setDepartamentosSeleccionados((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function handleOpenChange(next: boolean) {
    if (!next) {
      setPuestoId('');
      setDepartamentosSeleccionados(new Set());
      setBusquedaPuesto('');
    }
    onOpenChange(next);
  }

  async function handleConfirmar() {
    if (!puestoId || departamentosSeleccionados.size === 0) return;
    setEnviando(true);
    const idsSeleccionados = Array.from(departamentosSeleccionados);
    let exitosos = 0;
    const fallidos: string[] = [];
    const idsFallidos: string[] = [];

    for (const departamentoId of idsSeleccionados) {
      try {
        // Una llamada por departamento (contrato E2); el backend no
        // ofrece batch para esta mutación.
        await asignar.mutateAsync({
          sucursalId,
          puestoId,
          departamentoId,
          idempotencyKey: crypto.randomUUID(),
        });
        exitosos++;
      } catch (error) {
        const depto = deptosActivos.find((d) => d.id === departamentoId);
        const etiqueta = depto?.clave ?? departamentoId;
        fallidos.push(
          esApiError(error)
            ? `${etiqueta}: ${error.problem.title}`
            : `${etiqueta}: error inesperado`,
        );
        idsFallidos.push(departamentoId);
      }
    }

    setEnviando(false);

    if (exitosos > 0) {
      toast.success(
        `${puestoSeleccionado?.clave ?? 'Puesto'} asignado a ${exitosos} ${
          exitosos === 1 ? 'departamento' : 'departamentos'
        }`,
      );
    }
    if (fallidos.length > 0) {
      toast.error(`No se pudo asignar a: ${fallidos.join('; ')}`);
      setDepartamentosSeleccionados(new Set(idsFallidos));
    } else {
      handleOpenChange(false);
    }
  }

  const sinDeptos = deptosActivos.length === 0;
  const puestoSinDeptosDisponibles =
    puestoId !== '' && departamentosDisponibles.length === 0;

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Asignar puesto a departamentos de la sucursal</DialogTitle>
          <DialogDescription>
            Un mismo puesto (p.ej. &ldquo;Gerente&rdquo;) puede asignarse a varios
            departamentos activos de la sucursal en vez de crear un puesto por
            departamento.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-2">
          {catalogoQuery.isLoading ? (
            <div className="space-y-3">
              <Skeleton className="h-9 w-full" />
              <Skeleton className="h-9 w-full" />
            </div>
          ) : puestosCatalogo.length === 0 ? (
            <div className="rounded-md border border-dashed bg-muted/20 p-4 text-center text-sm text-muted-foreground">
              No hay puestos en el catálogo maestro.
            </div>
          ) : (
            <>
              {puestosCatalogo.length > 8 && (
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
                  htmlFor="puesto-select"
                  className="text-xs font-semibold uppercase tracking-wider text-foreground"
                >
                  Puesto *
                </label>
                <select
                  id="puesto-select"
                  aria-label="Puesto a asignar"
                  className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm shadow-xs focus:outline-hidden focus:ring-2 focus:ring-ring"
                  value={puestoId}
                  onChange={(e) => handleSelectPuesto(e.target.value)}
                  disabled={enviando}
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
            </>
          )}

          {sinDeptos ? (
            <div className="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800 dark:border-amber-900/50 dark:bg-amber-950/30 dark:text-amber-300">
              Esta sucursal no tiene departamentos activos asignados. Ve a la
              pestaña &ldquo;Departamentos&rdquo; para asignar departamentos a la
              sucursal antes de asociar puestos.
            </div>
          ) : puestoId && puestoSinDeptosDisponibles ? (
            <div className="rounded-md border border-dashed bg-muted/20 p-3 text-center text-sm text-muted-foreground">
              {puestoSeleccionado?.clave ?? 'Este puesto'} ya está asignado a todos
              los departamentos activos de esta sucursal.
            </div>
          ) : puestoId ? (
            <div className="space-y-1.5">
              <span className="text-xs font-semibold uppercase tracking-wider text-foreground">
                Departamentos * ({departamentosSeleccionados.size} seleccionados)
              </span>
              <div className="max-h-48 space-y-0.5 overflow-y-auto rounded-md border p-2">
                {departamentosDisponibles.map((d) => (
                  <label
                    key={d.id}
                    className="flex items-center gap-2 rounded px-1.5 py-1.5 text-sm hover:bg-muted/40"
                  >
                    <Checkbox
                      checked={departamentosSeleccionados.has(d.id)}
                      onCheckedChange={() => toggleDepartamento(d.id)}
                      disabled={enviando}
                      aria-label={`Departamento ${d.clave}`}
                    />
                    <span className="font-mono text-xs">{d.clave}</span>
                    <span className="text-muted-foreground">— {d.nombre}</span>
                  </label>
                ))}
              </div>
            </div>
          ) : null}
        </div>

        <DialogFooter className="gap-2 sm:gap-0">
          <Button
            variant="outline"
            onClick={() => handleOpenChange(false)}
            disabled={enviando}
          >
            Cancelar
          </Button>
          <Button
            onClick={handleConfirmar}
            disabled={
              enviando ||
              sinDeptos ||
              !puestoId ||
              departamentosSeleccionados.size === 0 ||
              puestosCatalogo.length === 0
            }
          >
            {enviando ? 'Asignando…' : 'Asignar puesto'}
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
        description:
          error.problem.detail ||
          (error.traceId ? `Código: ${error.traceId}` : undefined),
      });
    } else {
      toast.error(`Error al ${accion}.`);
    }
  };
}
