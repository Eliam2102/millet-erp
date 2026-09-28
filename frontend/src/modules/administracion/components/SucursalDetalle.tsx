import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { X } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useSucursales } from '@/features/catalogos/api';
import { TipoSucursal } from '@/modules/administracion/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { SucursalOrganizacionTab } from '@/modules/administracion/components/SucursalOrganizacionTab';
import { SucursalColaboradoresTab } from '@/modules/administracion/components/SucursalColaboradoresTab';
import { cn } from '@/lib/utils';

type Tab = 'departamentos-puestos' | 'empleados';

/**
 * Detalle standalone de una sucursal (F1-ADM-01).
 * Tabs: Departamentos y Puestos | Empleados.
 */
export function SucursalDetalle() {
  const { id } = useParams({ from: '/_app/admin/sucursales/$id' });
  const [tab, setTab] = useState<Tab>('departamentos-puestos');

  const sucursalesQuery = useSucursales();
  const sucursal = sucursalesQuery.data?.items.find((s) => s.id === id);

  const canGestionarDeptos = useHasPermission(
    PermisosCanonicos.AdminSucursalesDepartamentosGestionar,
  );
  const canGestionarPuestos = useHasPermission(
    PermisosCanonicos.AdminSucursalesPuestosGestionar,
  );
  const canGestionarEmpleados = useHasPermission(
    PermisosCanonicos.AdminEmpleadosGestionar,
  );

  return (
    <div className="flex flex-col h-full">
      <header
        className="shrink-0 flex flex-wrap items-center gap-3 border-b bg-background px-4 py-2"
        data-print="hidden"
      >
        <div className="flex min-w-0 items-center gap-2">
          {sucursalesQuery.isLoading ? (
            <span className="text-sm text-muted-foreground">Cargando…</span>
          ) : (
            <>
              <span className="truncate font-mono text-sm font-semibold">
                {sucursal?.clave ?? '—'}
              </span>
              <span className="hidden truncate text-sm text-muted-foreground md:inline">
                · {sucursal?.nombre ?? 'Sucursal'}
              </span>
              {sucursal != null && (
                <Badge
                  variant="outline"
                  className={cn(
                    'text-xs',
                    sucursal.tipo === TipoSucursal.Planta
                      ? 'border-indigo-500/40 bg-indigo-500/10 text-indigo-700 dark:text-indigo-300'
                      : 'border-cyan-500/40 bg-cyan-500/10 text-cyan-700 dark:text-cyan-300',
                  )}
                >
                  {sucursal.tipo === TipoSucursal.Planta ? 'Planta' : 'Taller'}
                </Badge>
              )}
              {sucursal == null && (
                <Badge variant="outline" className="text-muted-foreground">
                  No encontrada en el catálogo activo
                </Badge>
              )}
            </>
          )}
        </div>

        <div className="ml-auto flex items-center gap-1">
          <Button asChild variant="ghost" size="icon">
            <Link to="/admin/sucursales" aria-label="Cerrar">
              <X className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </header>

      <nav
        className="sticky top-0 z-10 shrink-0 flex items-center gap-1 border-b bg-background/95 px-4 backdrop-blur"
        role="tablist"
        aria-label="Secciones de la sucursal"
        data-print="hidden"
      >
        <TabButton
          activa={tab === 'departamentos-puestos'}
          onClick={() => setTab('departamentos-puestos')}
          ariaControls="tab-panel-departamentos-puestos"
        >
          Departamentos y Puestos
        </TabButton>
        <TabButton
          activa={tab === 'empleados'}
          onClick={() => setTab('empleados')}
          ariaControls="tab-panel-empleados"
        >
          Empleados
        </TabButton>
      </nav>

      <div className="flex-1 min-h-0 px-4 pt-3 pb-3">
        {tab === 'departamentos-puestos' && (
          <div id="tab-panel-departamentos-puestos" role="tabpanel" className="h-full">
            <SucursalOrganizacionTab
              sucursalId={id}
              canGestionarDeptos={canGestionarDeptos}
              canGestionarPuestos={canGestionarPuestos}
            />
          </div>
        )}
        {tab === 'empleados' && (
          <div id="tab-panel-empleados" role="tabpanel">
            <SucursalColaboradoresTab
              sucursalId={id}
              canGestionar={canGestionarEmpleados}
            />
          </div>
        )}
      </div>
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
