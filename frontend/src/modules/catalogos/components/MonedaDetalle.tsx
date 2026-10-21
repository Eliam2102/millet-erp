import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { Power, PowerOff, X } from 'lucide-react';
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
import {
  useMoneda,
  useDesactivarMoneda,
  useReactivarMoneda,
  useTiposCambio,
} from '@/modules/catalogos/api';
import { esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { MonedaDatosForm } from '@/modules/catalogos/components/MonedaDatosForm';
import { HistoricoTiposCambioPanel } from '@/modules/catalogos/components/HistoricoTiposCambioPanel';
import { cn } from '@/lib/utils';

type Tab = 'datos' | 'tipos-cambio';

/**
 * Detalle de moneda (P3 del patrón cross-módulo). Replica EXACTO el
 * patrón sticky nav-only de <see cref="EmpresaDetalle"/>: header NO
 * sticky (viaja con el scroll), <c>nav</c> sticky-top-14 con backdrop
 * blur. 2 tabs: Datos | Histórico tipos de cambio (N).
 *
 * <para>Lee <c>$id</c> con <c>useParams({ from:
 * '/_app/admin/catalogos/monedas/$id' })</c>. Acción del header
 * "Activar"/"Desactivar" usa PATCH parcial con <c>activa</c> toggle.</para>
 */
export function MonedaDetalle() {
  const { id } = useParams({ from: '/_app/admin/catalogos/monedas/$id' });
  const monedaQuery = useMoneda(id);
  // Pre-fetch para mostrar el contador (N) en el tab.
  const tiposCambioQuery = useTiposCambio(id, { offset: 0, limit: 50 });

  const [tab, setTab] = useState<Tab>('datos');
  const [confirmToggle, setConfirmToggle] = useState(false);

  const canGestionar = useHasPermission(PermisosCanonicos.CatalogosMonedasGestionar);
  const idempotencyKey = useFormIdempotencyKey();
  const desactivar = useDesactivarMoneda();
  const reactivar = useReactivarMoneda();

  if (monedaQuery.isError) {
    const problem = esApiError(monedaQuery.error) ? monedaQuery.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => monedaQuery.refetch()} />;
  }

  if (monedaQuery.isLoading || monedaQuery.data == null) {
    return (
      <div className="space-y-5">
        <TableSkeleton
          rows={4}
          columns={[{ width: 'w-48' }, { width: 'w-64' }, { width: 'w-32' }]}
        />
      </div>
    );
  }

  const moneda = monedaQuery.data;
  const totalTC = tiposCambioQuery.data?.total ?? 0;

  function handleConfirmarToggle() {
    if (moneda == null) return;
    const action = moneda.activa ? desactivar : reactivar;
    action.mutate(
      { id: moneda.id, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(`Moneda ${moneda.codigo} ${moneda.activa ? 'desactivada' : 'reactivada'}`);
          setConfirmToggle(false);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: codigoSoporte(error),
            });
          } else {
            toast.error('Error al cambiar el estatus de la moneda.');
          }
          setConfirmToggle(false);
        },
      },
    );
  }

  return (
    <div className="flex flex-col gap-5">
      {/* Header NO sticky: viaja con el scroll. Sólo el <nav> queda
          pinneado para que los tabs estén siempre visibles sin que el
          bloque sticky tape la primera fila del form al anclar — mismo
          patrón que EmpresaDetalle. */}
      <PageHeader
        mono
        titulo={<>{moneda.codigo}</>}
        descripcion={<>{moneda.nombre}</>}
        estado={
          <>
            {moneda.activa ? (
              <Badge variant="success">Activa</Badge>
            ) : (
              <Badge variant="neutral" className="text-ink-muted">
                Inactiva
              </Badge>
            )}
          </>
        }
        acciones={
          <>
            {canGestionar && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={() => setConfirmToggle(true)}
              >
                {moneda.activa ? (
                  <>
                    <PowerOff className="mr-1.5 h-4 w-4" />
                    Desactivar
                  </>
                ) : (
                  <>
                    <Power className="mr-1.5 h-4 w-4" />
                    Reactivar
                  </>
                )}
              </Button>
            )}
            <Button asChild variant="ghost" size="icon">
              <Link to="/admin/catalogos/monedas" aria-label="Cerrar">
                <X className="h-4 w-4" />
              </Link>
            </Button>
          </>
        }
      />
      <dl className="grid grid-cols-2 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat">
        <div>
          <dt className="text-xs text-ink-muted">Precisión decimal</dt>
          <dd className="font-mono text-sm">{moneda.decimales ?? '—'}</dd>
        </div>
      </dl>

      <nav
        className="sticky top-14 z-10 flex items-center gap-1 border-b bg-surface-page/95 px-4 "
        role="tablist"
        aria-label="Secciones de la moneda"
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
          activa={tab === 'tipos-cambio'}
          onClick={() => setTab('tipos-cambio')}
          ariaControls="tab-panel-tipos-cambio"
        >
          Histórico tipos de cambio <span className="ml-1 text-xs text-ink-muted">({totalTC})</span>
        </TabButton>
      </nav>

      <div className="space-y-5">
        {tab === 'datos' && (
          <div id="tab-panel-datos" role="tabpanel">
            <MonedaDatosForm moneda={moneda} />
          </div>
        )}
        {tab === 'tipos-cambio' && (
          <div id="tab-panel-tipos-cambio" role="tabpanel">
            <HistoricoTiposCambioPanel monedaId={moneda.id} />
          </div>
        )}
      </div>

      <AlertDialog open={confirmToggle} onOpenChange={setConfirmToggle}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {moneda.activa ? 'Desactivar moneda' : 'Reactivar moneda'}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {moneda.activa ? (
                <>
                  ¿Confirmas desactivar la moneda{' '}
                  <span className="font-mono font-semibold">{moneda.codigo}</span>? Los históricos
                  siguen siendo visibles, pero la moneda dejará de ofrecerse en selectores nuevos.
                </>
              ) : (
                <>
                  ¿Confirmas reactivar la moneda{' '}
                  <span className="font-mono font-semibold">{moneda.codigo}</span>? Volverá a
                  ofrecerse en selectores.
                </>
              )}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desactivar.isPending || reactivar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={handleConfirmarToggle}
              disabled={desactivar.isPending || reactivar.isPending}
            >
              {desactivar.isPending || reactivar.isPending
                ? 'Procesando…'
                : moneda.activa
                  ? 'Desactivar'
                  : 'Reactivar'}
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
        activa ? 'border-brand text-ink' : 'border-transparent text-ink-muted hover:text-ink',
      )}
    >
      {children}
    </button>
  );
}
