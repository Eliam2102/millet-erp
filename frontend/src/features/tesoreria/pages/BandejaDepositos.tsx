import { useState } from 'react';
import { toast } from 'sonner';
import { Inbox } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import {
  useDepositos,
  useRechazarDeposito,
} from '@/features/tesoreria/api/useTesoreria';
import {
  ESTADO_DEPOSITO_LABELS,
  EstadoDepositoConfirmacion,
  type DepositoConfirmacionResponse,
} from '@/features/tesoreria/api/types';
import {
  EstadoDepositoBadge,
  ReppTimbradoBadge,
} from '@/features/tesoreria/components/Badges';
import { ConfirmarDepositoDialog } from '@/features/tesoreria/components/ConfirmarDepositoDialog';
import { MotivoDialog } from '@/features/tesoreria/components/MotivoDialog';
import { formatoMonto } from '@/features/tesoreria/lib/formato';
import { formatDate } from '@/lib/datetime';
import { origenDeposito } from '@/features/tesoreria/lib/depositos';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

const FILTRO_TODOS = 'todos';

/**
 * <c>Depósitos por confirmar</c> (TES-FE-PR4b, P2 / §3.3 TES-9): CxC
 * propone el matching depósito ↔ facturas y Caja publica su expectativa;
 * Tesorería confirma aquí el hecho BANCARIO (RN-6) o rechaza con motivo
 * para que CxC re-proponga [T-G7]. Confirmar dispara la emisión del REPP
 * en Facturación; el timbrado es un momento fiscal aparte (badge REPP).
 */
export function BandejaDepositos() {
  const puedeConfirmar = useHasPermission(
    PermisosCanonicos.TesoreriaDepositosConfirmar,
  );
  const puedeRechazar = useHasPermission(
    PermisosCanonicos.TesoreriaDepositosRechazar,
  );

  const [estado, setEstado] = useState<string>(
    String(EstadoDepositoConfirmacion.Pendiente),
  );
  const [origen, setOrigen] = useState<string>(FILTRO_TODOS);

  const query = useDepositos({
    estado: estado === FILTRO_TODOS ? undefined : Number(estado),
    soloPropuestas:
      origen === FILTRO_TODOS ? undefined : origen === 'propuestas',
    limit: 200,
  });

  const rechazar = useRechazarDeposito();

  const [confirmando, setConfirmando] =
    useState<DepositoConfirmacionResponse | null>(null);
  const [rechazando, setRechazando] =
    useState<DepositoConfirmacionResponse | null>(null);

  const items = query.data?.items ?? [];

  function onRechazar(motivo: string) {
    if (rechazando == null) return;
    rechazar.mutate(
      {
        depositoId: rechazando.id,
        motivo,
        versionEsperada: rechazando.version,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Propuesta rechazada', {
            description: 'CxC recibirá el motivo y podrá re-proponer.',
          });
          setRechazando(null);
        },
        onError: (error) => {
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo rechazar',
            {
              description: esApiError(error)
                ? (error.problem.detail ?? `Código: ${error.traceId}`)
                : undefined,
            },
          );
        },
      },
    );
  }

  return (
    <div className="space-y-4 px-4 py-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Depósitos por confirmar
          </h1>
          <p className="text-sm text-muted-foreground">
            Propuestas de CxC y expectativas de Caja. Confirmar registra el
            hecho bancario y dispara la emisión del REPP en Facturación — el
            timbrado fiscal se refleja aparte en el badge REPP.
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Select value={origen} onValueChange={setOrigen}>
            <SelectTrigger className="w-40" aria-label="Origen">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={FILTRO_TODOS}>Todos los orígenes</SelectItem>
              <SelectItem value="propuestas">Propuestas CxC</SelectItem>
              <SelectItem value="caja">Expectativas de Caja</SelectItem>
            </SelectContent>
          </Select>
          <Select value={estado} onValueChange={setEstado}>
            <SelectTrigger className="w-36" aria-label="Estado">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={FILTRO_TODOS}>Todos los estados</SelectItem>
              {Object.entries(ESTADO_DEPOSITO_LABELS).map(([valor, label]) => (
                <SelectItem key={valor} value={valor}>
                  {label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los depósitos"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-28' },
            { width: 'w-56' },
            { width: 'w-32' },
            { width: 'w-24' },
            { width: 'w-24' },
            { width: 'w-28' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          icon={<Inbox className="h-10 w-10" />}
          title="Sin depósitos en este filtro."
          description="Las propuestas aparecen cuando CxC hace el matching depósito ↔ facturas; las expectativas, al cerrar sesiones de caja con efectivo."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 font-medium">Origen</th>
                <th className="px-3 py-2 font-medium">Cliente</th>
                <th className="px-3 py-2 font-medium">Referencia</th>
                <th className="px-3 py-2 text-right font-medium">Monto</th>
                <th className="px-3 py-2 text-right font-medium">Facturas</th>
                <th className="px-3 py-2 font-medium">Recibido</th>
                <th className="px-3 py-2 font-medium">Estado</th>
                <th className="px-3 py-2 font-medium">Fiscal</th>
                <th className="px-3 py-2" />
              </tr>
            </thead>
            <tbody className="divide-y">
              {items.map((d) => (
                <tr key={d.id} className="hover:bg-muted/30">
                  <td className="px-3 py-2 text-xs">{origenDeposito(d)}</td>
                  <td className="max-w-64 px-3 py-2">
                    <p className="truncate">
                      {d.clienteRazonSocial ??
                        (d.cajaSesionId != null ? 'Corte de caja' : '—')}
                    </p>
                    <p className="font-mono text-xs text-muted-foreground">
                      {d.clienteClave ?? ''}
                    </p>
                  </td>
                  <td
                    className="max-w-44 truncate px-3 py-2 font-mono text-xs"
                    title={d.depositoRef ?? undefined}
                  >
                    {d.depositoRef ?? '—'}
                  </td>
                  <td className="px-3 py-2 text-right font-mono tabular-nums">
                    {d.montoEsperado != null
                      ? formatoMonto(d.montoEsperado, d.moneda ?? 'MXN')
                      : '—'}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums">
                    {d.propuestaCxcId != null ? d.facturas.length : '—'}
                  </td>
                  <td className="px-3 py-2 text-xs">
                    {/* recibidoEn es DateTimeOffset (instante), no DateOnly:
                        formatoFecha haría split('-') y saldría basura. */}
                    {formatDate(d.recibidoEn)}
                  </td>
                  <td className="px-3 py-2">
                    <EstadoDepositoBadge estado={d.estado} />
                  </td>
                  <td className="px-3 py-2">
                    {d.estado === EstadoDepositoConfirmacion.Confirmada &&
                    d.propuestaCxcId != null ? (
                      <ReppTimbradoBadge timbrado={d.reppTimbrado} />
                    ) : d.estado === EstadoDepositoConfirmacion.Rechazada &&
                      d.motivoRechazo != null ? (
                      <span
                        className="block max-w-40 truncate text-xs text-muted-foreground"
                        title={d.motivoRechazo}
                      >
                        {d.motivoRechazo}
                      </span>
                    ) : (
                      <span className="text-xs text-muted-foreground">—</span>
                    )}
                  </td>
                  <td className="px-3 py-2 text-right">
                    {d.estado === EstadoDepositoConfirmacion.Pendiente && (
                      <div className="flex justify-end gap-2">
                        {puedeRechazar && (
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => setRechazando(d)}
                          >
                            Rechazar
                          </Button>
                        )}
                        {puedeConfirmar && (
                          <Button size="sm" onClick={() => setConfirmando(d)}>
                            Confirmar
                          </Button>
                        )}
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {query.data != null &&
        query.data.total > (query.data.items?.length ?? 0) && (
          <p className="text-xs text-muted-foreground">
            Mostrando {query.data.items.length} de {query.data.total} depósitos.
          </p>
        )}

      <ConfirmarDepositoDialog
        deposito={confirmando}
        onOpenChange={(o) => {
          if (!o) setConfirmando(null);
        }}
      />
      <MotivoDialog
        open={rechazando != null}
        onOpenChange={(o) => {
          if (!o) setRechazando(null);
        }}
        titulo="Rechazar propuesta de depósito"
        descripcion="El depósito no aparece en banco o el monto no coincide. CxC recibirá el motivo y podrá re-proponer [T-G7]."
        confirmLabel="Rechazar"
        onConfirm={onRechazar}
        pending={rechazar.isPending}
      />
    </div>
  );
}
