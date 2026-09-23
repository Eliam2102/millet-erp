import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { X } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useSucursales } from '@/features/catalogos/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { SucursalDepartamentosTab } from '@/modules/administracion/components/SucursalDepartamentosTab';
import { SucursalPuestosTab } from '@/modules/administracion/components/SucursalPuestosTab';
import { SucursalColaboradoresTab } from '@/modules/administracion/components/SucursalColaboradoresTab';
import { SucursalUsuariosTab } from '@/modules/administracion/components/SucursalUsuariosTab';
import { cn } from '@/lib/utils';

type Tab = 'departamentos' | 'puestos' | 'colaboradores' | 'usuarios';

/**
 * Detalle standalone de una sucursal (F1-ADM-01 Fase 3 frontend).
 * Espejo del patrón cross-módulo de <c>EmpresaDetalle</c> (P3) pero
 * SIN master-detail: los 3 endpoints de asignación
 * (<c>/admin/empresas/sucursales/{sucursalId}/{departamentos,puestos,usuarios}</c>)
 * cuelgan solo de <c>sucursalId</c>. Tabs: Departamentos | Puestos |
 * Colaboradores | Usuarios.
 *
 * <para>El encabezado (clave + nombre de la sucursal) se resuelve del
 * catálogo eager <c>useSucursales()</c> (mismo que usa
 * <c>SucursalSelector</c>) — no existe un <c>GET</c> de sucursal
 * individual en el backend, así que se cruza por id contra el
 * catálogo ya cacheado. Si el catálogo no trae la sucursal (p.ej.
 * inactiva y el catálogo solo expone activas), el header cae a un
 * fallback y las tabs se renderizan igual — cada una resuelve su
 * propio 403/404 contra el backend.</para>
 *
 * <para>Cada tab maneja su propio 403 (<c>SUCURSAL_NO_ASOCIADA</c>,
 * guard de pertenencia Fase 2 sección C) con
 * <c>SucursalTabErrorState</c> — no hay un guard único a nivel de
 * página porque el usuario puede tener acceso a unas asignaciones y a
 * otras no (permisos granulares por recurso).</para>
 */
export function SucursalDetalle() {
  const { id } = useParams({ from: '/_app/admin/sucursales/$id' });
  const [tab, setTab] = useState<Tab>('departamentos');

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
  const canGestionarUsuarios = useHasPermission(
    PermisosCanonicos.AdminSucursalesUsuariosGestionar,
  );

  return (
    <div className="flex flex-col">
      <header
        className="flex flex-wrap items-center gap-3 border-b bg-background px-4 py-2"
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
        className="sticky top-14 z-10 flex items-center gap-1 border-b bg-background/95 px-4 backdrop-blur"
        role="tablist"
        aria-label="Secciones de la sucursal"
        data-print="hidden"
      >
        <TabButton
          activa={tab === 'departamentos'}
          onClick={() => setTab('departamentos')}
          ariaControls="tab-panel-departamentos"
        >
          Departamentos
        </TabButton>
        <TabButton
          activa={tab === 'puestos'}
          onClick={() => setTab('puestos')}
          ariaControls="tab-panel-puestos"
        >
          Puestos
        </TabButton>
        <TabButton
          activa={tab === 'colaboradores'}
          onClick={() => setTab('colaboradores')}
          ariaControls="tab-panel-colaboradores"
        >
          Colaboradores
        </TabButton>
        <TabButton
          activa={tab === 'usuarios'}
          onClick={() => setTab('usuarios')}
          ariaControls="tab-panel-usuarios"
        >
          Usuarios
        </TabButton>
      </nav>

      <div className="px-4 pt-4 pb-6">
        {tab === 'departamentos' && (
          <div id="tab-panel-departamentos" role="tabpanel">
            <SucursalDepartamentosTab
              sucursalId={id}
              canGestionar={canGestionarDeptos}
            />
          </div>
        )}
        {tab === 'puestos' && (
          <div id="tab-panel-puestos" role="tabpanel">
            <SucursalPuestosTab
              sucursalId={id}
              canGestionar={canGestionarPuestos}
            />
          </div>
        )}
        {tab === 'colaboradores' && (
          <div id="tab-panel-colaboradores" role="tabpanel">
            <SucursalColaboradoresTab
              sucursalId={id}
              canGestionar={canGestionarEmpleados}
            />
          </div>
        )}
        {tab === 'usuarios' && (
          <div id="tab-panel-usuarios" role="tabpanel">
            <SucursalUsuariosTab
              sucursalId={id}
              canGestionar={canGestionarUsuarios}
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
