import {
  ListadoModulo,
  TabsEstado,
  BarraFiltros,
  PaginacionTabla,
  PageHeader,
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import { Input } from '@/components/ui/input';

import { usePaginacionLocal } from '../lib/use-paginacion-local';

import { useMemo } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { PhoneCall, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';

import { useUsuarios } from '@/features/catalogos/api';
import { useSeguimientosCobranza } from '@/features/cxc/api/useCobranza';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import { TimelineCobranza } from '@/features/cxc/components/TimelineCobranza';
import { useRegistrarGestion } from '@/features/cxc/components/registrar-gestion-context';
import { ResultadoCobranza } from '@/features/cxc/api/types';
import { ETIQUETA_RESULTADO_COBRANZA } from '@/features/cxc/lib/glosario';
import type { CobranzaSearch } from '@/features/cxc/lib/cobranza-search-schema';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

const FROM = '/_app/cxc/cobranza/' as const;

/**
 * <c>Cobranza</c> (CXC-FE-PR4, P2 con filtro de cliente OBLIGATORIO —
 * 05-frontend-diseno §2): la bitácora siempre es por cliente. Selector
 * primario + filtro por resultado + <c>&lt;TimelineCobranza/&gt;</c>;
 * "Registrar gestión" abre el Sheet shell-level con el cliente
 * preseleccionado.
 */
export function CobranzaPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const registrarGestion = useRegistrarGestion();
  const puedeRegistrar = useHasPermission(PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar);

  const clienteId = search.clienteId ?? null;

  const query = useSeguimientosCobranza({
    clienteId,
    resultado: search.resultado,
    limit: search.limit ?? 100,
  });

  const usuarios = useUsuarios();
  const nombreUsuario = useMemo(() => {
    const m = new Map<string, string>();
    for (const u of usuarios.data?.items ?? []) m.set(u.id, u.nombre);
    return (id: string) => m.get(id) ?? id.slice(0, 8);
  }, [usuarios.data]);

  function actualizarSearch(parcial: Partial<CobranzaSearch>) {
    navigate({ to: '/cxc/cobranza', search: { ...search, ...parcial } });
  }

  const q = (search.q ?? '').trim().toLowerCase();
  const items = (query.data?.items ?? []).filter(
    (s) => q.length === 0 || s.nota.toLowerCase().includes(q),
  );

  const pagina = usePaginacionLocal(items, JSON.stringify(search));

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <PageHeader
        titulo={<>Cobranza</>}
        descripcion={
          <>
            Bitácora auditable de gestiones por cliente — llamadas, correos y WhatsApp, con promesas
            de pago.
          </>
        }
        acciones={
          <>
            {puedeRegistrar && (
              <Button onClick={() => registrarGestion.abrir(clienteId ? { clienteId } : undefined)}>
                <Plus className="mr-2 h-4 w-4" />
                Registrar gestión
              </Button>
            )}
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={Object.values(ResultadoCobranza).map((r) => ({
              valor: r,
              label: ETIQUETA_RESULTADO_COBRANZA[r],
            }))}
            valor={search.resultado}
            conteos={undefined}
            onChange={(resultado) =>
              actualizarSearch({ resultado: resultado as ResultadoCobranza | undefined })
            }
          />
        }
        filtros={
          <BarraFiltros
            busqueda={
              <Input
                aria-label="Buscar en los registros recibidos"
                placeholder="Buscar en los registros recibidos"
                value={search.q ?? ''}
                onChange={(e) => actualizarSearch({ q: e.target.value || undefined })}
              />
            }
            filtros={
              <div className="flex flex-wrap items-end gap-3">
                <div className="w-full max-w-md space-y-1">
                  <label className="text-xs text-ink-muted">Cliente *</label>
                  <ClienteSelectorCxc
                    value={clienteId}
                    onChange={(item) => actualizarSearch({ clienteId: item?.id ?? undefined })}
                    placeholder="Elige el cliente para ver su bitácora…"
                  />
                </div>
              </div>
            }
          />
        }
        pie={
          <PaginacionTabla
            total={items.length}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.cambiar}
          />
        }
      >
        {clienteId == null ? (
          <EmptyState
            icon={<PhoneCall className="h-10 w-10" />}
            title="Elige un cliente para ver su bitácora de cobranza."
            description="La bitácora siempre se consulta por cliente; usa el selector de arriba."
          />
        ) : query.isError ? (
          <ErrorState
            title="No se pudo cargar la bitácora"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton rows={5} columns={[{ width: 'w-full' }, { width: 'w-2/3' }]} />
        ) : items.length === 0 ? (
          <EmptyState
            icon={<PhoneCall className="h-10 w-10" />}
            title={
              q ? `Ninguna gestión coincide con "${search.q}".` : 'Sin gestiones para este cliente.'
            }
            description={
              puedeRegistrar
                ? 'Registra la primera con "Registrar gestión".'
                : 'Aún no hay contactos registrados.'
            }
          />
        ) : (
          <div className="min-w-0">
            <TimelineCobranza items={pagina.visibles} nombreUsuario={nombreUsuario} />
            {query.data != null && query.data.total > items.length && q === '' && (
              <p className="mt-4 text-xs text-ink-muted">
                Mostrando {items.length} de {query.data.total} gestiones.
              </p>
            )}
          </div>
        )}
      </ListadoModulo>
    </div>
  );
}
