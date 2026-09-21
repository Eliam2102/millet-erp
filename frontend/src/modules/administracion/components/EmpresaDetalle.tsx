import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { PowerOff, X } from 'lucide-react';
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
import { useEmpresa, useDesactivarEmpresa } from '@/modules/administracion/api';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useAuth } from '@/lib/auth/useAuth';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmpresaDatosForm } from '@/modules/administracion/components/EmpresaDatosForm';
import { SucursalesPanel } from '@/modules/administracion/components/SucursalesPanel';
import { DepartamentosPanel } from '@/modules/administracion/components/DepartamentosPanel';
import type {
  DepartamentoResponse,
  EmpresaResponse,
  SucursalResponse,
} from '@/modules/administracion/api/types';
import { cn } from '@/lib/utils';

type Tab = 'datos' | 'sucursales' | 'departamentos';

/**
 * Detalle de empresa (P3 del patrón cross-módulo). Sub-topbar sticky
 * con identidad (RFC + razón social + badge activa) + acción
 * "Desactivar" + botón cerrar. Tabs: Datos | Sucursales | Departamentos.
 *
 * <para>Lee <c>$id</c> del path con <c>useParams({ from:
 * '/_app/admin/empresas/$id' })</c> — el componente solo se monta
 * dentro de esa ruta (no comparte URL con otro <c>$id</c>), así que
 * la forma estricta da typing exacto y evita que <c>id</c> se cuele
 * <c>undefined</c> en runtime (lo cual dejaría el query con
 * <c>enabled: false</c> y la UI atascada en el TableSkeleton — bug
 * F-Admin-PR2.4). 403/404 caen al <c>ErrorState</c>; tab "Datos"
 * renderiza el form siempre.</para>
 */
export function EmpresaDetalle() {
  const { id } = useParams({ from: '/_app/admin/empresas/$id' });
  const empresaQuery = useEmpresa(id);
  const [tab, setTab] = useState<Tab>('datos');
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const canDesactivar = useHasPermission(
    PermisosCanonicos.AdminEmpresasDesactivar,
  );
  const idempotencyKey = useFormIdempotencyKey();
  const desactivar = useDesactivarEmpresa();

  if (empresaQuery.isError) {
    const problem = esApiError(empresaQuery.error)
      ? empresaQuery.error.problem
      : undefined;
    return <ErrorState problem={problem} onRetry={() => empresaQuery.refetch()} />;
  }

  if (empresaQuery.isLoading || empresaQuery.data == null) {
    return (
      <div className="space-y-4 p-4">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  const { empresa, sucursales, departamentos } = empresaQuery.data;

  return (
    <EmpresaDetalleContenido
      empresa={empresa}
      sucursales={sucursales}
      departamentos={departamentos}
      tab={tab}
      setTab={setTab}
      confirmDesactivar={confirmDesactivar}
      setConfirmDesactivar={setConfirmDesactivar}
      canDesactivar={canDesactivar}
      desactivar={desactivar}
      idempotencyKey={idempotencyKey}
    />
  );
}

interface EmpresaDetalleContenidoProps {
  empresa: EmpresaResponse;
  sucursales: readonly SucursalResponse[];
  departamentos: readonly DepartamentoResponse[];
  tab: Tab;
  setTab: (tab: Tab) => void;
  confirmDesactivar: boolean;
  setConfirmDesactivar: (value: boolean) => void;
  canDesactivar: boolean;
  desactivar: ReturnType<typeof useDesactivarEmpresa>;
  idempotencyKey: string;
}

function EmpresaDetalleContenido({
  empresa,
  sucursales,
  departamentos,
  tab,
  setTab,
  confirmDesactivar,
  setConfirmDesactivar,
  canDesactivar,
  desactivar,
  idempotencyKey,
}: EmpresaDetalleContenidoProps) {
  const { currentEmpresaId, currentEmpresa, empresas, changeEmpresa } =
    useAuth();
  const empresaActivaMismatch =
    currentEmpresaId != null && currentEmpresaId !== empresa.id;
  const [cambiandoEmpresa, setCambiandoEmpresa] = useState(false);

  async function handleCambiarEmpresaActiva() {
    setCambiandoEmpresa(true);
    try {
      await changeEmpresa(empresa.id);
    } catch (err) {
      toast.error(
        err instanceof Error
          ? err.message
          : 'No se pudo cambiar la empresa activa.',
      );
    } finally {
      setCambiandoEmpresa(false);
    }
  }

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      { id: empresa.id, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(`Empresa ${empresa.rfc} desactivada`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          if (esApiError(error)) {
            // El handler valida que no haya sucursales activas; si la hay,
            // backend devuelve 422 SUCURSALES_ACTIVAS_PENDIENTES.
            toast.error(error.problem.title, {
              description:
                error.code === 'SUCURSALES_ACTIVAS_PENDIENTES'
                  ? 'Desactiva primero las sucursales activas.'
                  : error.traceId
                    ? `Código: ${error.traceId}`
                    : undefined,
            });
          } else {
            toast.error('Error al desactivar la empresa.');
          }
          setConfirmDesactivar(false);
        },
      },
    );
  }

  return (
    <div className="flex flex-col">
      {/* Header NO sticky: viaja con el scroll. Sólo el <nav> queda
          pinneado para que los tabs estén siempre visibles sin que el
          bloque sticky tape la primera fila del form al anclar — el
          header puede crecer (flex-wrap, acciones extra) sin obstruir
          el contenido inferior. */}
      <header
        className="flex flex-wrap items-center gap-3 border-b bg-background px-4 py-2"
        data-print="hidden"
      >
        <div className="flex min-w-0 items-center gap-2">
          <span className="truncate font-mono text-sm font-semibold">
            {empresa.rfc}
          </span>
          {empresa.activa ? (
            <Badge variant="secondary">Activa</Badge>
          ) : (
            <Badge variant="outline" className="text-muted-foreground">
              Inactiva
            </Badge>
          )}
          <span className="hidden truncate text-sm text-muted-foreground md:inline">
            · {empresa.razonSocial}
          </span>
        </div>

        <div className="ml-auto flex items-center gap-1">
          {canDesactivar && empresa.activa && (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              onClick={() => setConfirmDesactivar(true)}
            >
              <PowerOff className="mr-1.5 h-4 w-4" />
              Desactivar
            </Button>
          )}
          <Button asChild variant="ghost" size="icon">
            <Link to="/admin/empresas" aria-label="Cerrar">
              <X className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </header>

      <nav
        className="sticky top-14 z-10 flex items-center gap-1 border-b bg-background/95 px-4 backdrop-blur"
        role="tablist"
        aria-label="Secciones de la empresa"
        data-print="hidden"
      >
          <TabButton
            activa={tab === 'datos'}
            onClick={() => setTab('datos')}
            ariaControls="tab-panel-datos"
          >
            Datos
          </TabButton>
          <TabButton
            activa={tab === 'sucursales'}
            onClick={() => setTab('sucursales')}
            ariaControls="tab-panel-sucursales"
          >
            Sucursales{' '}
            <span className="ml-1 text-xs text-muted-foreground">
              ({sucursales.length})
            </span>
          </TabButton>
          <TabButton
            activa={tab === 'departamentos'}
            onClick={() => setTab('departamentos')}
            ariaControls="tab-panel-departamentos"
          >
            Departamentos{' '}
            <span className="ml-1 text-xs text-muted-foreground">
              ({departamentos.length})
            </span>
          </TabButton>
          <span className="ml-auto flex items-center gap-3 text-xs text-muted-foreground">
            <Link
              to="/admin"
              className="hover:text-foreground hover:underline"
            >
              Series y settings →
            </Link>
          </span>
        </nav>

      <div className="px-4 pt-4 pb-6">
        {empresaActivaMismatch &&
          (tab === 'sucursales' || tab === 'departamentos') && (
            <div className="mb-4 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">
              <p>
                Estás viendo {empresa.razonSocial}, pero tu empresa activa es{' '}
                {currentEmpresa?.razonSocial ?? 'otra empresa'}. Agregar o
                editar aquí afectaría a tu empresa activa, no a la que ves.
              </p>
              {empresas.some((e) => e.id === empresa.id) ? (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="mt-2"
                  onClick={handleCambiarEmpresaActiva}
                  disabled={cambiandoEmpresa}
                >
                  {cambiandoEmpresa
                    ? 'Cambiando…'
                    : `Cambiar empresa activa a ${empresa.razonSocial}`}
                </Button>
              ) : (
                <p className="mt-2 text-amber-800 dark:text-amber-300">
                  No tienes esta empresa asignada en tu sesión — no puedes
                  cambiar tu empresa activa a ella.
                </p>
              )}
            </div>
          )}
        {tab === 'datos' && (
          <div id="tab-panel-datos" role="tabpanel">
            <EmpresaDatosForm empresa={empresa} />
          </div>
        )}
        {tab === 'sucursales' && (
          <div id="tab-panel-sucursales" role="tabpanel">
            <SucursalesPanel
              empresaId={empresa.id}
              sucursales={sucursales}
              bloqueadoPorEmpresaActiva={empresaActivaMismatch}
            />
          </div>
        )}
        {tab === 'departamentos' && (
          <div id="tab-panel-departamentos" role="tabpanel">
            <DepartamentosPanel
              empresaId={empresa.id}
              departamentos={departamentos}
              bloqueadoPorEmpresaActiva={empresaActivaMismatch}
            />
          </div>
        )}
      </div>

      <AlertDialog
        open={confirmDesactivar}
        onOpenChange={setConfirmDesactivar}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar empresa</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar la empresa{' '}
              <span className="font-mono font-semibold">{empresa.rfc}</span>?
              Las sucursales activas deben desactivarse primero. La acción
              se puede revertir contactando al administrador.
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

interface TabButtonProps {
  activa: boolean;
  onClick: () => void;
  ariaControls: string;
  children: React.ReactNode;
}

function TabButton({ activa, onClick, ariaControls, children }: TabButtonProps) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={activa}
      aria-controls={ariaControls}
      onClick={onClick}
      className={cn(
        'border-b-2 px-3 py-2 text-sm font-medium transition-colors',
        activa
          ? 'border-primary text-foreground'
          : 'border-transparent text-muted-foreground hover:text-foreground',
      )}
    >
      {children}
    </button>
  );
}
