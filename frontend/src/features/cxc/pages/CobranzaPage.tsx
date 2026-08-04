import { useMemo } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { PhoneCall, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
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
const SENTINEL_ALL = '__all__';

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
  const puedeRegistrar = useHasPermission(
    PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar,
  );

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

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Cobranza</h1>
          <p className="text-sm text-muted-foreground">
            Bitácora auditable de gestiones por cliente — llamadas, correos
            y WhatsApp, con promesas de pago.
          </p>
        </div>
        {puedeRegistrar && (
          <Button
            onClick={() =>
              registrarGestion.abrir(
                clienteId ? { clienteId } : undefined,
              )
            }
          >
            <Plus className="mr-2 h-4 w-4" />
            Registrar gestión
          </Button>
        )}
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="w-full max-w-md space-y-1">
          <label className="text-xs text-muted-foreground">
            Cliente (obligatorio)
          </label>
          <ClienteSelectorCxc
            value={clienteId}
            onChange={(item) =>
              actualizarSearch({ clienteId: item?.id ?? undefined })
            }
            placeholder="Elige el cliente para ver su bitácora…"
          />
        </div>
        <div className="space-y-1">
          <label className="text-xs text-muted-foreground">Resultado</label>
          <Select
            value={
              search.resultado != null ? String(search.resultado) : SENTINEL_ALL
            }
            onValueChange={(v) =>
              actualizarSearch({
                resultado:
                  v === SENTINEL_ALL
                    ? undefined
                    : (Number(v) as ResultadoCobranza),
              })
            }
          >
            <SelectTrigger aria-label="Filtrar por resultado" className="w-48">
              <SelectValue placeholder="Todos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
              {Object.values(ResultadoCobranza).map((r) => (
                <SelectItem key={r} value={String(r)}>
                  {ETIQUETA_RESULTADO_COBRANZA[r]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

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
        <TableSkeleton
          rows={5}
          columns={[{ width: 'w-full' }, { width: 'w-2/3' }]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<PhoneCall className="h-10 w-10" />}
          title={
            q
              ? `Ninguna gestión coincide con "${search.q}".`
              : 'Sin gestiones para este cliente.'
          }
          description={
            puedeRegistrar
              ? 'Registra la primera con "Registrar gestión".'
              : 'Aún no hay contactos registrados.'
          }
        />
      ) : (
        <div className="max-w-3xl pt-2">
          <TimelineCobranza items={items} nombreUsuario={nombreUsuario} />
          {query.data != null && query.data.total > items.length && q === '' && (
            <p className="mt-4 text-xs text-muted-foreground">
              Mostrando {items.length} de {query.data.total} gestiones.
            </p>
          )}
        </div>
      )}
    </div>
  );
}
