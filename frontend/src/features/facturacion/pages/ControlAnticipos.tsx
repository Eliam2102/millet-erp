import { BarraFiltros, PaginacionTabla, TabsEstado } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { EstadoAnticipo, type ControlAnticipoFila } from '@/features/facturacion/api/types';
import { useControlAnticipos } from '@/features/facturacion/api/useAnticipos';
import { useNuevoAnticipo } from '@/features/facturacion/components/nuevo-anticipo-context';
import { ClienteSelector } from '@/features/facturacion/components/selectors/ClienteSelector';
import type { AnticiposSearch } from '@/features/facturacion/lib/anticipos-search-schema';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { FileSearch, Plus } from 'lucide-react';
import { useState } from 'react';
import { ReporteFacturacion } from '../components/ReporteFacturacion';
import { usePaginacionLocal } from '../lib/use-paginacion-local';

const FROM = '/_app/facturacion/anticipos/' as const;

/**
 * <c>Control de Anticipos — Resumen</c> (FE-F4-PR2). Reporte tabular
 * (ADR-0036) vía <c>&lt;ReporteFacturacion&gt;</c> con filtros cliente/estado/
 * obra/fechas. Si se filtra por cliente, ofrece ver su estado de cuenta
 * detallado (relación 07, NCs de amortización).
 */
export function ControlAnticipos() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const nuevoAnticipo = useNuevoAnticipo();
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionAnticiposEmitir);

  const query = useControlAnticipos({
    clienteId: search.clienteId,
    estado: search.estado,
    obraId: search.obraId,
    desde: search.desde,
    hasta: search.hasta,
  });

  function actualizarSearch(parcial: Partial<AnticiposSearch>) {
    navigate({ to: '/facturacion/anticipos', search: { ...search, ...parcial } });
  }

  const [busqueda, setBusqueda] = useState('');
  const items = (query.data?.filas ?? []).filter((f) =>
    Object.values(f).some((v) =>
      String(v ?? '')
        .toLowerCase()
        .includes(busqueda.toLowerCase()),
    ),
  );
  const paginacion = usePaginacionLocal(items, JSON.stringify(search) + busqueda);
  const filtrosUi = (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-ink-muted">Cliente</label>
        <ClienteSelector
          value={search.clienteId ?? null}
          onChange={(item) => actualizarSearch({ clienteId: item?.id ?? undefined })}
          className="w-72"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-ink-muted">Desde</label>
        <Input
          type="date"
          value={search.desde ?? ''}
          onChange={(e) => actualizarSearch({ desde: e.target.value || undefined })}
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-ink-muted">Hasta</label>
        <Input
          type="date"
          value={search.hasta ?? ''}
          onChange={(e) => actualizarSearch({ hasta: e.target.value || undefined })}
        />
      </div>
      {search.clienteId && (
        <Button variant="outline" asChild>
          <Link to="/facturacion/anticipos/$clienteId" params={{ clienteId: search.clienteId }}>
            <FileSearch className="mr-2 h-4 w-4" />
            Estado de cuenta
          </Link>
        </Button>
      )}
    </div>
  );

  return (
    <div className="space-y-5">
      <PageHeader
        titulo={<>Control de Anticipos</>}
        descripcion={<>Anticipos con su saldo amortizable por cliente.</>}
        acciones={
          <>
            <div className="flex flex-wrap items-center gap-2">
              <Button variant="outline" asChild>
                <Link to="/facturacion/anticipos/facturas">Facturas de anticipo</Link>
              </Button>
              {puedeEmitir && (
                <Button onClick={() => nuevoAnticipo.abrir()}>
                  <Plus className="mr-2 h-4 w-4" />
                  Nuevo anticipo
                </Button>
              )}
            </div>
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={Object.entries(EstadoAnticipo).map(([label, valor]) => ({ label, valor }))}
            valor={search.estado}
            conteos={undefined}
            onChange={(estado) =>
              actualizarSearch({ estado: estado as EstadoAnticipo | undefined })
            }
          />
        }
        filtros={
          <BarraFiltros
            busqueda={
              <Input
                aria-label="Buscar anticipo"
                placeholder="Cliente, folio u obra"
                value={busqueda}
                onChange={(e) => setBusqueda(e.target.value)}
              />
            }
            filtros={filtrosUi}
          />
        }
        pie={
          <PaginacionTabla
            total={items.length}
            offset={paginacion.offset}
            limit={10}
            tamanos={[]}
            onChange={paginacion.onChange}
          />
        }
      >
        <ReporteFacturacion<ControlAnticipoFila>
          reporte={query.data}
          isLoading={query.isLoading}
          error={esApiError(query.error) ? query.error : undefined}
          onRetry={() => query.refetch()}
          filasVisibles={paginacion.visibles}
          nombreArchivo="control-anticipos"
        />
      </ListadoModulo>
    </div>
  );
}
