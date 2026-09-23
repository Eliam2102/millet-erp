import { useMemo, type ReactNode } from 'react';
import { Building2 } from 'lucide-react';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { useEmpresas } from '@/modules/administracion/api';
import { esApiError } from '@/lib/api';
import { ListaEmpresasCompacta } from '@/modules/administracion/components/ListaEmpresasCompacta';
import { cn } from '@/lib/utils';

/**
 * Layout master-detail (P3 del patrón cross-módulo) para
 * <c>/admin/empresas</c> — el link "avanzado" (sin card propia en el
 * nav, ver ADR-0051) para ver/editar los datos de la única empresa
 * (Millet). No hay alta de empresas desde la UI.
 *
 * <list>
 *   <item><b>Master</b> (320px sticky a la izquierda) — lista compacta
 *   de empresas con highlight del id activo.</item>
 *   <item><b>Panel detalle</b> (resto del ancho) — recibe via
 *   <c>detalle</c> prop el contenido para el id seleccionado, o un
 *   placeholder cuando <c>idActivo</c> es <c>null</c>.</item>
 * </list>
 *
 * <para>La ruta <c>/admin/empresas</c> renderiza con
 * <c>idActivo=null</c>; <c>/admin/empresas/$id</c> con
 * <c>idActivo</c> = id de la URL y pasa
 * <c>&lt;EmpresaDetalle/&gt;</c> como <c>detalle</c>. Mobile sub-md
 * aplica drill-down (master oculto cuando hay id activo).</para>
 */
export interface EmpresasLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function EmpresasLayout({ idActivo, detalle }: EmpresasLayoutProps) {
  const empresasQuery = useEmpresas({ limit: 200 });

  const items = useMemo(
    () => empresasQuery.data?.items ?? [],
    [empresasQuery.data],
  );

  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de empresas"
        data-print="hidden"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="text-xl font-semibold tracking-tight">
            Datos de la empresa
          </h1>
        </div>

        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          <RenderLista query={empresasQuery} items={items} idActivo={idActivo} />
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de empresa"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

interface RenderListaProps {
  query: ReturnType<typeof useEmpresas>;
  items: ReturnType<typeof useEmpresas>['data'] extends
    | { items: infer I }
    | undefined
    ? I
    : never;
  idActivo: string | null;
}

function RenderLista({ query, items, idActivo }: RenderListaProps) {
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
        icon={<Building2 className="h-10 w-10" />}
        title="Aún no hay empresas."
        description="Contacta al administrador de plataforma."
      />
    );
  }

  return <ListaEmpresasCompacta items={items} idActivo={idActivo} />;
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-muted/20 p-8 text-center">
      <div className="space-y-1 text-muted-foreground">
        <Building2 className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona una empresa de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
