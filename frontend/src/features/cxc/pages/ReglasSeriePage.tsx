import {
  DateTimeDisplay,
  EmptyState,
  ErrorState,
  PageHeader,
  TableSkeleton,
} from '@/components/erp';
import { useState, type FormEvent } from 'react';
import { History, ListChecks, Plus } from 'lucide-react';
import { toast } from 'sonner';
import { useQueryClient } from '@tanstack/react-query';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { cxcKeys } from '@/features/cxc/api/keys';
import {
  ComportamientoSerie,
  type HistorialReglaSerie,
  type ReglaLiberacionSerieAdmin,
} from '@/features/cxc/api/types';
import {
  useCambiarComportamientoSerie,
  useCambiarEstadoReglaSerie,
  useCrearReglaSerie,
  useHistorialReglasSerie,
  useReglasSerieAdmin,
} from '@/features/cxc/api/useReglasSerie';
import { ETIQUETA_COMPORTAMIENTO_SERIE } from '@/features/cxc/lib/glosario';
import { codigoSoporte, esApiError, esConflictoConcurrencia } from '@/lib/api';
import { cn } from '@/lib/utils';

const OPCIONES_COMPORTAMIENTO: ComportamientoSerie[] = [
  ComportamientoSerie.SiempreLibera,
  ComportamientoSerie.NuncaLibera,
  ComportamientoSerie.EvaluaCredito,
];

/**
 * <c>Reglas de serie</c> (C7.2, 86e3kdxy6) — <c>/cxc/reglas-serie</c>.
 * La serie es el prefijo del número de pedido A+W; gana la regla activa
 * más larga y una serie sin regla evalúa crédito. Alta, cambio de
 * comportamiento y activar/desactivar con permiso
 * <c>reglas-serie.gestionar</c>; cada cambio queda en la bitácora central
 * (historial abajo) y aplica a la siguiente evaluación.
 */
export function ReglasSeriePage() {
  const reglas = useReglasSerieAdmin();
  const historial = useHistorialReglasSerie();
  const [agregando, setAgregando] = useState(false);

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <PageHeader
        titulo={<>Reglas de serie</>}
        descripcion={
          <>
            Qué hace cada serie del pedido A+W al decidir la liberación. Las series sin regla
            activa evalúan crédito. Los cambios aplican a la siguiente evaluación.
          </>
        }
        acciones={
          <Button size="sm" onClick={() => setAgregando(true)} disabled={agregando}>
            <Plus className="mr-1.5 h-3.5 w-3.5" />
            Agregar serie
          </Button>
        }
      />

      {agregando && <NuevaReglaInline onCerrar={() => setAgregando(false)} />}

      {reglas.isError ? (
        <ErrorState
          title="No se pudieron cargar las reglas de serie"
          problem={esApiError(reglas.error) ? reglas.error.problem : undefined}
          onRetry={() => reglas.refetch()}
        />
      ) : reglas.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[{ width: 'w-20' }, { width: 'w-64' }, { width: 'w-20' }, { width: 'w-40' }, { width: 'w-24' }]}
        />
      ) : (reglas.data ?? []).length === 0 ? (
        <EmptyState
          icon={<ListChecks className="h-10 w-10" />}
          title="Sin reglas de serie."
          description="Todas las series evalúan crédito hasta que agregues una regla."
        />
      ) : (
        <TablaReglas reglas={reglas.data ?? []} />
      )}

      <section className="space-y-2" aria-labelledby="historial-reglas-serie">
        <h2 id="historial-reglas-serie" className="flex items-center gap-1.5 text-sm font-semibold text-ink">
          <History className="h-4 w-4 text-ink-muted" />
          Historial de cambios
        </h2>
        {historial.isError ? (
          <ErrorState
            title="No se pudo cargar el historial"
            problem={esApiError(historial.error) ? historial.error.problem : undefined}
            onRetry={() => historial.refetch()}
          />
        ) : historial.isLoading ? (
          <TableSkeleton rows={3} columns={[{ width: 'w-32' }, { width: 'w-20' }, { width: 'w-full' }, { width: 'w-40' }]} />
        ) : (historial.data ?? []).length === 0 ? (
          <p className="text-xs text-ink-muted">Sin cambios registrados.</p>
        ) : (
          <TablaHistorial filas={historial.data ?? []} />
        )}
      </section>
    </div>
  );
}

function useManejarError() {
  const queryClient = useQueryClient();
  const conflictDialog = useConflictDialog();
  return (error: unknown, fallback: string) => {
    if (esConflictoConcurrencia(error)) {
      conflictDialog.openSimple({
        onRefrescar: () => queryClient.invalidateQueries({ queryKey: cxcKeys.reglasSerie() }),
        traceId: error.traceId,
      });
      return;
    }
    if (esApiError(error)) {
      toast.error(error.problem.title, {
        description: error.problem.detail ?? codigoSoporte(error),
      });
      return;
    }
    toast.error(fallback);
  };
}

function NuevaReglaInline({ onCerrar }: { onCerrar: () => void }) {
  const crear = useCrearReglaSerie();
  const manejarError = useManejarError();
  const [prefijo, setPrefijo] = useState('');
  const [comportamiento, setComportamiento] = useState<ComportamientoSerie>(
    ComportamientoSerie.EvaluaCredito,
  );
  const limpio = prefijo.trim();
  const error =
    limpio.length === 0
      ? null
      : !/^[0-9]{1,10}$/.test(limpio)
        ? 'La serie debe tener solo dígitos (máximo 10).'
        : null;

  function enviar(e: FormEvent) {
    e.preventDefault();
    if (limpio.length === 0 || error) return;
    crear.mutate(
      { prefijo: limpio, comportamiento, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (r) => {
          toast.success(`Serie ${r.prefijo} agregada.`);
          onCerrar();
        },
        onError: (err) => manejarError(err, 'No se pudo agregar la serie.'),
      },
    );
  }

  return (
    <form
      onSubmit={enviar}
      className="flex flex-wrap items-end gap-3 rounded-md border border-dashed border-primary p-3"
      aria-label="Agregar serie"
    >
      <div className="space-y-1">
        <Label htmlFor="nueva-serie-prefijo" className="text-xs text-ink-muted">
          Serie
        </Label>
        <Input
          id="nueva-serie-prefijo"
          value={prefijo}
          onChange={(e) => setPrefijo(e.target.value)}
          inputMode="numeric"
          placeholder="p. ej. 61"
          className="w-32 font-mono"
          aria-invalid={error != null}
          autoFocus
        />
      </div>
      <div className="space-y-1">
        <Label className="text-xs text-ink-muted">Comportamiento</Label>
        <SelectComportamiento
          valor={comportamiento}
          onChange={setComportamiento}
          etiqueta="Comportamiento de la serie nueva"
        />
      </div>
      <div className="flex gap-2">
        <Button type="submit" size="sm" disabled={limpio.length === 0 || error != null || crear.isPending}>
          Guardar
        </Button>
        <Button type="button" size="sm" variant="ghost" onClick={onCerrar}>
          Cancelar
        </Button>
      </div>
      {error && <p role="alert" className="w-full text-xs text-danger-fg">{error}</p>}
    </form>
  );
}

function SelectComportamiento({
  valor,
  onChange,
  etiqueta,
  disabled,
}: {
  valor: ComportamientoSerie;
  onChange: (v: ComportamientoSerie) => void;
  etiqueta: string;
  disabled?: boolean;
}) {
  return (
    <Select
      value={String(valor)}
      onValueChange={(v) => onChange(Number(v) as ComportamientoSerie)}
      disabled={disabled}
    >
      <SelectTrigger aria-label={etiqueta} className="w-72">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {OPCIONES_COMPORTAMIENTO.map((c) => (
          <SelectItem key={c} value={String(c)}>
            {ETIQUETA_COMPORTAMIENTO_SERIE[c]}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

function TablaReglas({ reglas }: { reglas: ReglaLiberacionSerieAdmin[] }) {
  const cambiarComportamiento = useCambiarComportamientoSerie();
  const cambiarEstado = useCambiarEstadoReglaSerie();
  const manejarError = useManejarError();
  const ocupado = cambiarComportamiento.isPending || cambiarEstado.isPending;

  return (
    <div className="overflow-x-auto rounded-md border">
      <Table className="w-full text-sm">
        <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
          <TableRow>
            <TableHead className="px-3 py-2 font-medium">Serie</TableHead>
            <TableHead className="px-3 py-2 font-medium">Comportamiento</TableHead>
            <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
            <TableHead className="px-3 py-2 font-medium">Último cambio</TableHead>
            <TableHead className="px-3 py-2" />
          </TableRow>
        </TableHeader>
        <TableBody className="divide-y">
          {reglas.map((r) => (
            <TableRow key={r.id} className={cn(!r.activo && 'opacity-60')}>
              <TableCell className="px-3 py-2 font-mono">{r.prefijo}</TableCell>
              <TableCell className="px-3 py-2">
                <SelectComportamiento
                  valor={r.comportamiento}
                  etiqueta={`Comportamiento de la serie ${r.prefijo}`}
                  disabled={ocupado}
                  onChange={(c) => {
                    if (c === r.comportamiento) return;
                    cambiarComportamiento.mutate(
                      {
                        id: r.id,
                        versionEsperada: r.version,
                        comportamiento: c,
                        idempotencyKey: crypto.randomUUID(),
                      },
                      {
                        onSuccess: () =>
                          toast.success(
                            `Serie ${r.prefijo}: ${ETIQUETA_COMPORTAMIENTO_SERIE[c].toLowerCase()}. Aplica a la siguiente evaluación.`,
                          ),
                        onError: (e) => manejarError(e, 'No se pudo cambiar la regla.'),
                      },
                    );
                  }}
                />
              </TableCell>
              <TableCell className="px-3 py-2">
                <Badge variant={r.activo ? 'success' : 'neutral'}>{r.activo ? 'Activa' : 'Inactiva'}</Badge>
              </TableCell>
              <TableCell className="px-3 py-2 text-xs text-ink-muted">
                <DateTimeDisplay value={r.actualizadoEn} />
                {r.actualizadoPor && <p>{r.actualizadoPor}</p>}
              </TableCell>
              <TableCell className="px-3 py-2 text-right">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={ocupado}
                  onClick={() =>
                    cambiarEstado.mutate(
                      {
                        id: r.id,
                        versionEsperada: r.version,
                        activo: !r.activo,
                        idempotencyKey: crypto.randomUUID(),
                      },
                      {
                        onSuccess: (res) =>
                          toast.success(
                            res.activo
                              ? `Serie ${res.prefijo} activada.`
                              : `Serie ${res.prefijo} desactivada: evaluará crédito.`,
                          ),
                        onError: (e) => manejarError(e, 'No se pudo cambiar el estado de la regla.'),
                      },
                    )
                  }
                >
                  {r.activo ? 'Desactivar' : 'Activar'}
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

function describirCambio(h: HistorialReglaSerie): string {
  if (h.operacion === 'crear') {
    const c = h.comportamientoDespues != null ? ETIQUETA_COMPORTAMIENTO_SERIE[h.comportamientoDespues] : '—';
    return `Alta: ${c.toLowerCase()}`;
  }
  const partes: string[] = [];
  if (h.comportamientoAntes != null && h.comportamientoDespues != null) {
    partes.push(
      `${ETIQUETA_COMPORTAMIENTO_SERIE[h.comportamientoAntes]} → ${ETIQUETA_COMPORTAMIENTO_SERIE[h.comportamientoDespues]}`,
    );
  }
  if (h.activoDespues != null) partes.push(h.activoDespues ? 'Activada' : 'Desactivada');
  return partes.length > 0 ? partes.join(' · ') : 'Cambio';
}

function TablaHistorial({ filas }: { filas: HistorialReglaSerie[] }) {
  return (
    <div className="overflow-x-auto rounded-md border">
      <Table className="w-full text-sm">
        <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
          <TableRow>
            <TableHead className="px-3 py-2 font-medium">Fecha</TableHead>
            <TableHead className="px-3 py-2 font-medium">Serie</TableHead>
            <TableHead className="px-3 py-2 font-medium">Cambio</TableHead>
            <TableHead className="px-3 py-2 font-medium">Quién</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody className="divide-y">
          {filas.map((h) => (
            <TableRow key={h.id}>
              <TableCell className="px-3 py-2 text-xs text-ink-muted">
                <DateTimeDisplay value={h.fecha} />
              </TableCell>
              <TableCell className="px-3 py-2 font-mono">{h.prefijo}</TableCell>
              <TableCell className="px-3 py-2 text-xs">{describirCambio(h)}</TableCell>
              <TableCell className="px-3 py-2 text-xs">{h.actorNombre}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
