import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { DialogBody } from '@/components/ui/dialog';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { ConceptoSelector } from './ConceptoSelector';
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
import { useConfirmarDeposito, useMovimientos } from '@/features/tesoreria/api/useTesoreria';
import {
  BeneficiarioTipo,
  EstadoAplicacionMovimiento,
  SentidoMovimiento,
  SOPORTE_FICHA_LABELS,
  SoporteFicha,
  type DepositoConfirmacionResponse,
} from '@/features/tesoreria/api/types';
import { CuentaBancariaSelector } from '@/features/tesoreria/components/CuentaBancariaSelector';
import { formatoMonto } from '@/features/tesoreria/lib/formato';
import { useQueryClient } from '@tanstack/react-query';
import { esApiError, esConflictoConcurrencia, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
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
export function ConfirmarDepositoDialog({ deposito, onOpenChange }: ConfirmarDepositoDialogProps) {
  const confirmar = useConfirmarDeposito();
  const queryClient = useQueryClient();
  // La alta rápida y confirmación comparten petición y clave.
  const keyConfirmar = useBodyScopedIdempotencyKey();

  // Candidatos: ingresos sin aplicar (RN-6). El cotejo fino (moneda/monto)
  // se marca visualmente; el backend es la autoridad.
  const candidatos = useMovimientos({
    sentido: SentidoMovimiento.Ingreso,
    estadoAplicacion: EstadoAplicacionMovimiento.NoAplicado,
    limit: 100,
  });
  // C7.4: el sobrante de un depósito ya confirmado (AplicadoParcial) también se
  // confirma, por lo disponible; solo aplica a propuestas de CxC.
  const parciales = useMovimientos({
    sentido: SentidoMovimiento.Ingreso,
    estadoAplicacion: EstadoAplicacionMovimiento.AplicadoParcial,
    limit: 100,
  });

  const [movimientoId, setMovimientoId] = useState<string | null>(null);
  // Corte de caja: 1..N fichas; desde la 2ª se exige vale de caja o corte parcial con folio.
  const [montoAlta, setMontoAlta] = useState('');
  const [soporteTipo, setSoporteTipo] = useState('');
  const [soporteFolio, setSoporteFolio] = useState('');
  // C7.4: preselecciona el movimiento que CxC identificó (derived state por depósito).
  const [depositoPrevio, setDepositoPrevio] = useState<string | null>(null);
  if ((deposito?.id ?? null) !== depositoPrevio) {
    setDepositoPrevio(deposito?.id ?? null);
    setMovimientoId(deposito?.movimientoSugeridoId ?? null);
    setMontoAlta(String(deposito?.saldoPorDepositar ?? deposito?.montoEsperado ?? ''));
    setSoporteTipo('');
    setSoporteFolio('');
  }
  const [altaRapida, setAltaRapida] = useState(false);
  const puedeAltaRapida = useHasPermission(PermisosCanonicos.TesoreriaMovimientosRegistrar);
  const [conceptoId, setConceptoId] = useState<string | null>(null);
  const [cuentaId, setCuentaId] = useState<string | null>(null);
  const [fechaValor, setFechaValor] = useState(() => hoyLocalISO());
  const [referencia, setReferencia] = useState('');

  const esPropuesta = deposito?.propuestaCxcId != null;
  const esCaja = deposito?.cajaSesionId != null;
  const saldo = esCaja ? (deposito?.saldoPorDepositar ?? deposito?.montoEsperado ?? null) : null;
  const fichas = deposito?.fichas ?? [];
  const necesitaSoporte = esCaja && fichas.length >= 1;
  const soporteIncompleto = necesitaSoporte && (soporteTipo === '' || soporteFolio.trim() === '');
  // E8: si CxC ligó la propuesta a un ingreso, solo se confirma contra ese movimiento
  // (el backend responde DEP_MOVIMIENTO_DISTINTO_AL_LIGADO en otro caso).
  const ligado = esPropuesta ? (deposito?.movimientoSugeridoId ?? null) : null;
  const pending = confirmar.isPending;

  const items = useMemo(() => {
    const todos = [
      ...(candidatos.data?.items ?? []),
      ...(deposito?.propuestaCxcId != null ? (parciales.data?.items ?? []) : []),
    ];
    // Moneda igual si el depósito la trae; propuestas además priorizan
    // el monto exacto (los demás siguen visibles — Caja admite aproximado).
    const porMoneda =
      deposito?.moneda != null ? todos.filter((m) => m.moneda === deposito.moneda) : todos;
    // Corte de caja: una ficha no puede exceder el saldo por depositar.
    const filtrados = saldo != null ? porMoneda.filter((m) => m.monto <= saldo) : porMoneda;
    const objetivo = saldo ?? deposito?.montoEsperado;
    if (objetivo == null) return filtrados;
    return [...filtrados].sort((a, b) => Math.abs(a.monto - objetivo) - Math.abs(b.monto - objetivo));
  }, [candidatos.data, parciales.data, deposito, saldo]);

  function cerrar(next: boolean) {
    if (!next) {
      setMovimientoId(null);
      setAltaRapida(false);
      setCuentaId(null);
      setReferencia('');
      setSoporteTipo('');
      setSoporteFolio('');
    }
    onOpenChange(next);
  }

  function confirmarCon(
    id?: string,
    ingreso?: {
      cuentaBancariaId: string;
      monto: number;
      fechaValor: string;
      referenciaBancaria?: string;
      conceptoId?: string;
      beneficiarioTipo?: BeneficiarioTipo;
      beneficiarioRef?: string;
    },
  ) {
    if (deposito == null) return;
    const command = {
      depositoId: deposito.id,
      movimientoBancarioId: id,
      ingreso,
      versionEsperada: deposito.version,
      soporteTipo: soporteTipo === '' ? undefined : (Number(soporteTipo) as SoporteFicha),
      soporteFolio: soporteFolio.trim() || undefined,
    };
    confirmar.mutate(
      {
        ...command,
        idempotencyKey: keyConfirmar(command),
      },
      {
        onSuccess: () => {
          toast.success(esCaja ? 'Ficha agregada al corte' : 'Depósito confirmado', {
            description: esPropuesta
              ? 'Hecho bancario confirmado; el complemento queda en Pendientes de REP para su emisión manual.'
              : 'Corte de caja ligado al depósito en banco.',
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
            esApiError(error) ? error.problem.title : 'No se pudo confirmar el depósito',
            {
              description: esApiError(error)
                ? (error.problem.detail ?? 'Revisa los datos del depósito antes de reintentar.')
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
    const monto = esCaja ? Number(montoAlta) : deposito.montoEsperado;
    if (cuentaId == null || monto == null || !(monto > 0)) return;
    // El servidor valida y guarda ambos documentos en una sola transacción.
    const command = {
      cuentaBancariaId: cuentaId,
      conceptoId: conceptoId ?? undefined,
      monto,
      fechaValor,
      referenciaBancaria: referencia.trim() || (deposito.depositoRef ?? undefined),
      beneficiarioTipo: deposito.clienteId != null ? BeneficiarioTipo.Cliente : undefined,
      beneficiarioRef: deposito.clienteId ?? undefined,
    };
    confirmarCon(undefined, command);
  }

  const puedeEnviar = altaRapida
    ? cuentaId != null &&
      (esCaja ? Number(montoAlta) > 0 : deposito?.montoEsperado != null) &&
      fechaValor !== '' &&
      !soporteIncompleto
    : movimientoId != null && !soporteIncompleto;

  return (
    <Dialog open={deposito != null} onOpenChange={cerrar}>
      <DialogContent size="lg">
        <DialogHeader>
          <DialogTitle>{esCaja ? 'Agregar ficha al corte' : 'Confirmar depósito'}</DialogTitle>
          <DialogDescription>
            {deposito != null &&
              `${esPropuesta ? 'Propuesta de CxC' : 'Expectativa de Caja'} por ${
                deposito.montoEsperado != null
                  ? formatoMonto(deposito.montoEsperado, deposito.moneda ?? 'MXN')
                  : '—'
              }${deposito.depositoRef ? ` · ref ${deposito.depositoRef}` : ''}. ` +
                (esPropuesta
                  ? 'Se requiere un ingreso identificado con monto y moneda exactos; si el banco recibió otro importe, rechaza para que CxC re-proponga.'
                  : `Saldo por depositar ${
                      saldo != null ? formatoMonto(saldo, deposito.moneda ?? 'MXN') : '—'
                    }. Una ficha pertenece a un solo corte y no puede exceder el saldo.`)}
          </DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          {esCaja && fichas.length > 0 && (
            <div className="space-y-1">
              <p className="text-xs font-medium text-ink-muted">Fichas del corte</p>
              <ul className="space-y-0.5 text-xs">
                {fichas.map((f) => (
                  <li key={f.id} className="font-mono">
                    <DateTimeDisplay value={f.fechaFicha} variant="corta" /> ·{' '}
                    {formatoMonto(f.importe, deposito?.moneda ?? 'MXN')}
                    {f.numeroFicha ? ` · ${f.numeroFicha}` : ''}
                    {f.soporteTipo != null
                      ? ` · ${SOPORTE_FICHA_LABELS[f.soporteTipo]} ${f.soporteFolio ?? ''}`
                      : ''}
                  </li>
                ))}
              </ul>
            </div>
          )}
          <div className="space-y-3">
            {!altaRapida ? (
              <div className="space-y-1">
                <label className="text-xs text-ink-muted">
                  Movimiento de ingreso sin aplicar *
                </label>
                <Select value={movimientoId ?? ''} onValueChange={(v) => setMovimientoId(v)} disabled={ligado != null}>
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
                          <DateTimeDisplay value={m.fechaValor} variant="corta" />
                        </span>{' '}
                        · {formatoMonto(m.monto, m.moneda)}
                        {m.referenciaBancaria ? ` · ${m.referenciaBancaria}` : ''}
                        {m.estadoAplicacion === EstadoAplicacionMovimiento.AplicadoParcial
                          ? ' · sobrante'
                          : ''}
                        {m.id === deposito?.movimientoSugeridoId
                          ? ' · identificado por CxC'
                          : deposito?.montoEsperado != null && m.monto === deposito.montoEsperado
                            ? ' ✓'
                            : ''}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {ligado != null && (
                  <p className="text-xs text-ink-muted">
                    La propuesta está ligada al ingreso que CxC identificó; se confirma contra ese
                    movimiento. Si no corresponde, recházala para que CxC re-proponga.
                  </p>
                )}
                {puedeAltaRapida && ligado == null && (
                  <button
                    type="button"
                    className="text-xs text-brand underline-offset-2 hover:underline"
                    onClick={() => setAltaRapida(true)}
                  >
                    El ingreso aún no está registrado — alta rápida
                  </button>
                )}
              </div>
            ) : (
              <div className="space-y-3 rounded-md border border-dashed border-brand/50 p-3">
                <p className="text-xs font-medium text-ink-muted">
                  Alta rápida del ingreso (
                  {esCaja
                    ? `saldo ${saldo != null ? formatoMonto(saldo, deposito?.moneda ?? 'MXN') : '—'}`
                    : deposito?.montoEsperado != null
                      ? formatoMonto(deposito.montoEsperado, deposito?.moneda ?? 'MXN')
                      : '—'}
                  )
                </p>
                <FormSeccion numero={1} titulo="Datos de la operación">
                  <FormCampo span={6}>
                    <div className="space-y-1">
                      <label className="text-xs text-ink-muted">Cuenta bancaria *</label>
                      <CuentaBancariaSelector
                        paraIngresos
                        value={cuentaId}
                        onChange={setCuentaId}
                        moneda={deposito?.moneda}
                      />
                      <ConceptoSelector value={conceptoId} onChange={setConceptoId} />
                    </div>
                  </FormCampo>
                </FormSeccion>
                <FormGrid>
                  {esCaja && (
                    <FormCampo span={6}>
                      <div className="space-y-1">
                        <label className="text-xs text-ink-muted" htmlFor="dep-monto">
                          Importe de la ficha *
                        </label>
                        <Input
                          id="dep-monto"
                          type="number"
                          min="0.01"
                          step="0.01"
                          value={montoAlta}
                          onChange={(e) => setMontoAlta(e.target.value)}
                        />
                      </div>
                    </FormCampo>
                  )}
                  <FormCampo span={6}>
                    <div className="space-y-1">
                      <label className="text-xs text-ink-muted" htmlFor="dep-fecha">
                        Fecha valor *
                      </label>
                      <Input
                        id="dep-fecha"
                        type="date"
                        value={fechaValor}
                        onChange={(e) => setFechaValor(e.target.value)}
                      />
                    </div>
                  </FormCampo>
                  <FormCampo span={6}>
                    <div className="space-y-1">
                      <label className="text-xs text-ink-muted" htmlFor="dep-ref">
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
                  </FormCampo>
                </FormGrid>
                <button
                  type="button"
                  className="text-xs text-brand underline-offset-2 hover:underline"
                  onClick={() => setAltaRapida(false)}
                >
                  Volver a seleccionar un ingreso existente
                </button>
              </div>
            )}
          </div>
          {necesitaSoporte && (
            <FormGrid>
              <FormCampo span={6}>
                <div className="space-y-1">
                  <label className="text-xs text-ink-muted">Documento soporte *</label>
                  <Select value={soporteTipo} onValueChange={setSoporteTipo}>
                    <SelectTrigger aria-label="Documento soporte">
                      <SelectValue placeholder="Vale de caja o corte parcial" />
                    </SelectTrigger>
                    <SelectContent>
                      {Object.entries(SOPORTE_FICHA_LABELS).map(([valor, texto]) => (
                        <SelectItem key={valor} value={valor}>
                          {texto}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              </FormCampo>
              <FormCampo span={6}>
                <div className="space-y-1">
                  <label className="text-xs text-ink-muted" htmlFor="dep-soporte-folio">
                    Folio del soporte *
                  </label>
                  <Input
                    id="dep-soporte-folio"
                    value={soporteFolio}
                    maxLength={40}
                    onChange={(e) => setSoporteFolio(e.target.value)}
                  />
                </div>
              </FormCampo>
            </FormGrid>
          )}
        </DialogBody>
        <DialogFooter>
          <Button variant="ghost" onClick={() => cerrar(false)} disabled={pending}>
            Cancelar
          </Button>
          <Button
            onClick={submit}
            disabled={pending || !puedeEnviar}
            title={
              !puedeEnviar
                ? 'Selecciona el ingreso, completa los datos del alta rápida y, desde la 2ª ficha, el documento soporte con su folio.'
                : undefined
            }
          >
            {pending ? 'Confirmando…' : esCaja ? 'Agregar ficha' : 'Confirmar depósito'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
