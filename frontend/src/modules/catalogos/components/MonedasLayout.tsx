import { useMemo, type ReactNode } from 'react';
import { Coins, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { useMonedas } from '@/modules/catalogos/api';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ListaMonedasCompacta } from '@/modules/catalogos/components/ListaMonedasCompacta';
import { useNuevaMoneda } from '@/modules/catalogos/components/nueva-moneda-context';
import { cn } from '@/lib/utils';

/**
 * Layout master-detail (P3) para <c>/admin/catalogos/monedas</c>.
 * Análogo a <see cref="EmpresasLayout"/> pero sin filtros server-side
 * (catálogo pequeño: ~12 monedas seedeadas).
 */
export interface MonedasLayoutProps {
  idActivo: string | null;
  detalle?: ReactNode;
}

export function MonedasLayout({ idActivo, detalle }: MonedasLayoutProps) {
  const nuevaMoneda = useNuevaMoneda();
  const canCrear = useHasPermission(PermisosCanonicos.CatalogosMonedasGestionar);
  const monedasQuery = useMonedas();

  const items = useMemo(() => monedasQuery.data ?? [], [monedasQuery.data]);

  return (
    <div className="flex flex-col gap-4 md:h-[calc(100vh-3.5rem-3rem)] md:flex-row">
      <aside
        className={cn(
          'flex flex-col gap-3 md:w-80 md:shrink-0 md:overflow-hidden',
          idActivo != null ? 'hidden md:flex' : 'flex',
        )}
        aria-label="Lista de monedas"
        data-print="hidden"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="text-xl font-semibold tracking-tight">Monedas</h1>
          {canCrear && (
            <Button size="sm" onClick={() => nuevaMoneda.abrir()}>
              <Plus className="mr-1 h-4 w-4" />
              Nueva
            </Button>
          )}
        </div>

        <div className="flex-1 overflow-y-auto rounded-md border bg-card">
          <RenderLista
            query={monedasQuery}
            items={items}
            idActivo={idActivo}
            canCrear={canCrear}
            onAbrirNueva={() => nuevaMoneda.abrir()}
          />
        </div>
      </aside>

      <section
        className={cn(
          'min-w-0 flex-1 md:overflow-y-auto',
          idActivo == null ? 'hidden md:block' : 'block',
        )}
        aria-label="Detalle de moneda"
      >
        {idActivo == null ? <PlaceholderSinSeleccion /> : detalle}
      </section>
    </div>
  );
}

interface RenderListaProps {
  query: ReturnType<typeof useMonedas>;
  items: ReturnType<typeof useMonedas>['data'] extends infer I
    ? I extends readonly unknown[]
      ? I
      : never
    : never;
  idActivo: string | null;
  canCrear: boolean;
  onAbrirNueva: () => void;
}

function RenderLista({
  query,
  items,
  idActivo,
  canCrear,
  onAbrirNueva,
}: RenderListaProps) {
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
        icon={<Coins className="h-10 w-10" />}
        title="Aún no hay monedas."
        description={
          canCrear
            ? 'Crea la primera para empezar.'
            : 'Cuando se creen, aparecerán aquí.'
        }
        action={
          canCrear ? (
            <Button size="sm" onClick={onAbrirNueva}>
              <Plus className="mr-1 h-4 w-4" />
              Nueva moneda
            </Button>
          ) : undefined
        }
      />
    );
  }

  return <ListaMonedasCompacta items={items} idActivo={idActivo} />;
}

function PlaceholderSinSeleccion() {
  return (
    <div className="flex h-full min-h-64 items-center justify-center rounded-md border border-dashed bg-muted/20 p-8 text-center">
      <div className="space-y-1 text-muted-foreground">
        <Coins className="mx-auto h-8 w-8 opacity-50" aria-hidden="true" />
        <p className="text-sm">Selecciona una moneda de la lista</p>
        <p className="text-xs">para ver su detalle.</p>
      </div>
    </div>
  );
}
