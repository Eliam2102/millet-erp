import { useMemo, useState, type ReactNode } from 'react';
import { Link } from '@tanstack/react-router';
import { Building2, Plus, Search } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { useEmpresa } from '@/modules/administracion/api';
import { esApiError } from '@/lib/api';
import { useAuth } from '@/lib/auth/useAuth';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ListaSucursalesCompacta } from '@/modules/administracion/components/ListaSucursalesCompacta';
import { SucursalInlineForm } from '@/modules/administracion/components/SucursalInlineForm';
import type { SucursalResponse } from '@/modules/administracion/api/types';
import { cn } from '@/lib/utils';

export interface SucursalesLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function SucursalesLayout({ idActivo, detalle }: SucursalesLayoutProps) {
  const { currentEmpresaId } = useAuth();
  const empresaQuery = useEmpresa(currentEmpresaId);
  const [agregando, setAgregando] = useState(false);
  const [filtro, setFiltro] = useState('');

  const canGestionar = useHasPermission(
    PermisosCanonicos.AdminEmpresasSucursalesGestionar,
  );
  const canVerDatosEmpresa = useHasPermission(
    PermisosCanonicos.AdminEmpresasLeer,
  );

  const sucursales = useMemo(
    () => empresaQuery.data?.sucursales ?? [],
    [empresaQuery.data?.sucursales],
  );

  const sucursalesFiltradas = useMemo(() => {
    if (!filtro.trim()) return sucursales;
    const q = filtro.toLowerCase().trim();
    return sucursales.filter(
      (s) =>
        s.clave.toLowerCase().includes(q) || s.nombre.toLowerCase().includes(q),
    );
  }, [sucursales, filtro]);

  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de sucursales"
        data-print="hidden"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <h1 className="text-xl font-semibold tracking-tight">Sucursales</h1>
            {sucursales.length > 0 && (
              <span className="rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground">
                {sucursales.length}
              </span>
            )}
          </div>
          {canGestionar && !agregando && (
            <Button
              size="sm"
              variant="outline"
              onClick={() => setAgregando(true)}
              className="gap-1 text-xs"
            >
              <Plus className="h-3.5 w-3.5" />
              Agregar sucursal
            </Button>
          )}
        </div>

        {canVerDatosEmpresa && currentEmpresaId && (
          <div className="flex justify-between items-center text-xs text-muted-foreground px-1">
            <span>Vidrios Millet</span>
            <Link
              to="/admin/empresas/$id"
              params={{ id: currentEmpresaId }}
              className="hover:text-foreground underline"
            >
              Datos de la empresa
            </Link>
          </div>
        )}

        {agregando && currentEmpresaId && (
          <div className="rounded-md border border-dashed border-primary p-2 bg-card">
            <SucursalInlineForm
              empresaId={currentEmpresaId}
              onCancel={() => setAgregando(false)}
              onSaved={() => {
                setAgregando(false);
                empresaQuery.refetch();
              }}
            />
          </div>
        )}

        {sucursales.length > 3 && (
          <div className="relative">
            <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
            <Input
              type="search"
              placeholder="Buscar por clave o nombre…"
              value={filtro}
              onChange={(e) => setFiltro(e.target.value)}
              className="h-9 pl-8 text-xs"
            />
          </div>
        )}

        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          <RenderLista
            query={empresaQuery}
            items={sucursalesFiltradas}
            idActivo={idActivo}
            tieneFiltro={Boolean(filtro.trim())}
          />
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto rounded-md border bg-card',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de sucursal"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

interface RenderListaProps {
  query: ReturnType<typeof useEmpresa>;
  items: readonly SucursalResponse[];
  idActivo: string | null;
  tieneFiltro: boolean;
}

function RenderLista({ query, items, idActivo, tieneFiltro }: RenderListaProps) {
  if (query.isLoading) {
    return (
      <div className="p-3">
        <TableSkeleton
          rows={6}
          columns={[{ width: 'w-full' }, { width: 'w-full' }]}
        />
      </div>
    );
  }

  if (query.isError) {
    const problem = esApiError(query.error) ? query.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => query.refetch()} />;
  }

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<Building2 className="h-10 w-10 text-muted-foreground" />}
        title={tieneFiltro ? 'Sin coincidencias' : 'Aún no hay sucursales.'}
        description={
          tieneFiltro
            ? 'Prueba con otro término de búsqueda.'
            : 'Registra la primera sucursal con el botón superior.'
        }
      />
    );
  }

  return <ListaSucursalesCompacta items={items} idActivo={idActivo} />;
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center p-8 text-center">
      <div className="space-y-2 text-muted-foreground">
        <Building2 className="mx-auto h-10 w-10 opacity-40" aria-hidden="true" />
        <p className="text-base font-medium text-foreground">
          Selecciona una sucursal de la lista
        </p>
        <p className="text-xs max-w-sm">
          Consulta y gestiona sus datos generales, departamentos, puestos y colaboradores asignados.
        </p>
      </div>
    </div>
  );
}
