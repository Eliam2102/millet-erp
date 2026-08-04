import { hoyLocalISO } from '@/lib/datetime';
import { useMemo, useState } from 'react';
import { toast } from 'sonner';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  useConfirmarDeposito,
  useMovimientos,
  useRegistrarMovimientoIngreso,
} from '@/features/tesoreria/api/useTesoreria';
import {
  BeneficiarioTipo,
  EstadoAplicacionMovimiento,
  SentidoMovimiento,
  type DepositoConfirmacionResponse,
} from '@/features/tesoreria/api/types';
import { CuentaBancariaSelector } from '@/features/tesoreria/components/CuentaBancariaSelector';
import { formatoFecha, formatoMonto } from '@/features/tesoreria/lib/formato';
import { useQueryClient } from '@tanstack/react-query';
import {
  esApiError,
  esConflictoConcurrencia,
  useBodyScopedIdempotencyKey,
} from '@/lib/api';
import { tesoreriaKeys } from '@/features/tesoreria/api/keys';

export interface ConfirmarDepositoDialogProps {
  deposito: DepositoConfirmacionResponse | null;
  onOpenChange: (open: boolean) => void;
}

/**
 * Dialog de confirmación de depósito (TES-FE-PR4b, §3.3 / RN-6): liga un
 * movimiento bancario de INGRESO identificado a la propuesta de CxC (o a
 * la expectativa de Caja). Dos caminos:
 * (a) seleccionar un ingreso ya registrado sin aplicar, o
 * (b) alta rápida del ingreso (el extracto lo traería con conciliación
 * activa) y confirmación en seguida.
 *
 * Para propuestas el backend exige monto EXACTO y misma moneda — si el
 * banco recibió otro importe, el camino es RECHAZAR para que CxC
 * re-proponga. Confirmar ≠ timbrado: el REPP lo emite Facturación y el
 * badge fiscal llega después vía repp_timbrado.
 */
export function ConfirmarDepositoDialog({
  deposito,
  onOpenChange,
}: ConfirmarDepositoDialogProps) {
  const confirmar = useConfirmarDeposito();
  const queryClient = useQueryClient();
  const registrarIngreso = useRegistrarMovimientoIngreso();
  // Un holder por mutación: confirmar y alta-de-ingreso reintentan por
  // separado sin pisarse la key.
  const keyConfirmar = useBodyScopedIdempotencyKey();
  const keyIngreso = useBodyScopedIdempotencyKey();

  // Candidatos: ingresos sin aplicar (RN-6). El cotejo fino (moneda/monto)
  // se marca visualmente; el backend es la autoridad.
  const candidatos = useMovimientos({
    sentido: SentidoMovimiento.Ingreso,
    estadoAplicacion: EstadoAplicacionMovimiento.NoAplicado,
    limit: 100,
  });

  const [movimientoId, setMovimientoId] = useState<string | null>(null);
  const [altaRapida, setAltaRapida] = useState(false);
  const [cuentaId, setCuentaId] = useState<string | null>(null);
  const [fechaValor, setFechaValor] = useState(() =>
    hoyLocalISO(),
  );
  const [referencia, setReferencia] = useState('');

  const esPropuesta = deposito?.propuestaCxcId != null;
  const pending = confirmar.isPending || registrarIngreso.isPending;

  const items = useMemo(() => {
    const todos = candidatos.data?.items ?? [];
    // Moneda igual si el depósito la trae; propuestas además priorizan
    // el monto exacto (los demás siguen visibles — Caja admite aproximado).
    const filtrados =
      deposito?.moneda != null
        ? todos.filter((m) => m.moneda === deposito.moneda)
        : todos;
    if (deposito?.montoEsperado == null) return filtrados;
    return [...filtrados].sort(
      (a, b) =>
        Math.abs(a.monto - deposito.montoEsperado!) -
        Math.abs(b.monto - deposito.montoEsperado!),
    );
  }, [candidatos.data, deposito]);

  function cerrar(next: boolean) {
    if (!next) {
      setMovimientoId(null);
      setAltaRapida(false);
      setCuentaId(null);
      setReferencia('');
    }
    onOpenChange(next);
  }

  function confirmarCon(id: string) {
    if (deposito == null) return;
    const command = {
      depositoId: deposito.id,
      movimientoBancarioId: id,
      versionEsperada: deposito.version,
    };
    confirmar.mutate(
      {
        ...command,
        idempotencyKey: keyConfirmar(command),
      },
      {
        onSuccess: () => {
          toast.success('Depósito confirmado', {
            description: esPropuesta
              ? 'Hecho bancario confirmado; Facturación emitirá el REPP (el timbrado se refleja aparte).'
              : 'Expectativa de Caja ligada al depósito en banco.',
          });
          cerrar(false);
        },
        onError: (error) => {
          // 409: otra sesión cambió el depósito; la versión del modal quedó
          // vieja y reintentar re-fallaría. Se refresca la lista y se cierra
          // para reabrir con la versión vigente (Tesorería no usa el
          // conflict-dialog de CxC; este es el equivalente para un modal).
          if (esConflictoConcurrencia(error)) {
            queryClient.invalidateQueries({
              queryKey: tesoreriaKeys.depositos(),
            });
            toast.error('El depósito cambió en otra sesión', {
              description:
                'Se actualizó la lista; vuelve a abrirlo para confirmarlo con la versión vigente.',
            });
            cerrar(false);
            return;
          }
          toast.error(
            esApiError(error)
              ? error.problem.title
              : 'No se pudo confirmar el depósito',
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

  function submit() {
    if (deposito == null) return;
    if (!altaRapida) {
      if (movimientoId != null) confirmarCon(movimientoId);
      return;
    }
    if (cuentaId == null || deposito.montoEsperado == null) return;
    // Alta rápida: registrar el ingreso y confirmar en seguida. Si la
    // confirmación falla, el movimiento queda NoAplicado y re-seleccionable.
    const command = {
      cuentaBancariaId: cuentaId,
      monto: deposito.montoEsperado,
      fechaValor,
      referenciaBancaria:
        referencia.trim() || (deposito.depositoRef ?? undefined),
      beneficiarioTipo:
        deposito.clienteId != null ? BeneficiarioTipo.Cliente : undefined,
      beneficiarioRef: deposito.clienteId ?? undefined,
    };
    registrarIngreso.mutate(
      {
        command,
        // Reintentar el alta rápida con el mismo ingreso no crea un SEGUNDO
        // movimiento de ingreso (key ligada al contenido).
        idempotencyKey: keyIngreso(command),
      },
      {
        onSuccess: (movimiento) => confirmarCon(movimiento.id),
        onError: (error) => {
          toast.error(
            esApiError(error)
              ? error.problem.title
              : 'No se pudo registrar el ingreso',
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

  const puedeEnviar = altaRapida
    ? cuentaId != null && deposito?.montoEsperado != null && fechaValor !== ''
    : movimientoId != null;

  return (
    <Dialog open={deposito != null} onOpenChange={cerrar}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Confirmar depósito</DialogTitle>
          <DialogDescription>
            {deposito != null &&
              `${esPropuesta ? 'Propuesta de CxC' : 'Expectativa de Caja'} por ${
                deposito.montoEsperado != null
                  ? formatoMonto(deposito.montoEsperado, deposito.moneda ?? 'MXN')
                  : '—'
              }${deposito.depositoRef ? ` · ref ${deposito.depositoRef}` : ''}. ` +
                (esPropuesta
                  ? 'RN-6: exige un ingreso identificado con monto y moneda exactos; si el banco recibió otro importe, rechaza para que CxC re-proponga.'
                  : 'El monto de Caja es aproximado (el fondo del día siguiente se queda en caja).')}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3">
          {!altaRapida ? (
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground">
                Movimiento de ingreso sin aplicar
              </label>
              <Select
                value={movimientoId ?? ''}
                onValueChange={(v) => setMovimientoId(v)}
              >
                <SelectTrigger aria-label="Movimiento de ingreso">
                  <SelectValue
                    placeholder={
                      candidatos.isLoading
                        ? 'Cargando ingresos…'
                        : items.length === 0
                          ? 'No hay ingresos sin aplicar'
                          : 'Selecciona el ingreso'
                    }
                  />
                </SelectTrigger>
                <SelectContent>
                  {items.map((m) => (
                    <SelectItem key={m.id} value={m.id}>
                      <span className="font-mono text-xs">
                        {formatoFecha(m.fechaValor)}
                      </span>{' '}
                      · {formatoMonto(m.monto, m.moneda)}
                      {m.referenciaBancaria ? ` · ${m.referenciaBancaria}` : ''}
                      {deposito?.montoEsperado != null &&
                      m.monto === deposito.montoEsperado
                        ? ' ✓'
                        : ''}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <button
                type="button"
                className="text-xs text-primary underline-offset-2 hover:underline"
                onClick={() => setAltaRapida(true)}
              >
                El ingreso aún no está registrado — alta rápida
              </button>
            </div>
          ) : (
            <div className="space-y-3 rounded-md border border-dashed border-primary/50 p-3">
              <p className="text-xs font-medium text-muted-foreground">
                Alta rápida del ingreso (
                {deposito?.montoEsperado != null
                  ? formatoMonto(deposito.montoEsperado, deposito?.moneda ?? 'MXN')
                  : '—'}
                )
              </p>
              <div className="space-y-1">
                <label className="text-xs text-muted-foreground">
                  Cuenta bancaria
                </label>
                <CuentaBancariaSelector
                  value={cuentaId}
                  onChange={setCuentaId}
                  moneda={deposito?.moneda}
                />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1">
                  <label
                    className="text-xs text-muted-foreground"
                    htmlFor="dep-fecha"
                  >
                    Fecha valor
                  </label>
                  <Input
                    id="dep-fecha"
                    type="date"
                    value={fechaValor}
                    onChange={(e) => setFechaValor(e.target.value)}
                  />
                </div>
                <div className="space-y-1">
                  <label
                    className="text-xs text-muted-foreground"
                    htmlFor="dep-ref"
                  >
                    Referencia bancaria
                  </label>
                  <Input
                    id="dep-ref"
                    value={referencia}
                    maxLength={120}
                    placeholder={deposito?.depositoRef ?? 'SPEI-…'}
                    onChange={(e) => setReferencia(e.target.value)}
                  />
                </div>
              </div>
              <button
                type="button"
                className="text-xs text-primary underline-offset-2 hover:underline"
                onClick={() => setAltaRapida(false)}
              >
                Volver a seleccionar un ingreso existente
              </button>
            </div>
          )}
        </div>

        <DialogFooter>
          <Button variant="ghost" onClick={() => cerrar(false)} disabled={pending}>
            Cancelar
          </Button>
          <Button onClick={submit} disabled={pending || !puedeEnviar}>
            {pending ? 'Confirmando…' : 'Confirmar depósito'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
