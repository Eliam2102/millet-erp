import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { useId, useState, type FormEvent } from 'react';
import { Loader2, Plus, TriangleAlert } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  DialogBody,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import {
  SheetBody,
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { MotivoBajaDialog } from '@/components/erp/adjuntos/MotivoBajaDialog';
import { EstadoAdjunto, useAdjuntos } from '@/components/erp/adjuntos/api';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import {
  useAgregarCuentaBancariaProveedor,
  useCambiarCuentaBancariaProveedor,
  useCuentasBancariasProveedor,
  useValidarCuentaBancariaProveedor,
} from '@/modules/datos-maestros/api';
import {
  EstadoCuentaBancariaProveedor,
  EstadoCuentaBancariaProveedorLabels,
  type AccionCuentaBancariaProveedor,
  type CuentaBancariaProveedor,
} from '@/modules/datos-maestros/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** Código del tipo de documento del expediente que respalda una cuenta. */
const TIPO_ESTADO_CUENTA = 'estado_cuenta_bancaria';

const BADGE_ESTADO: Record<
  EstadoCuentaBancariaProveedor,
  'warning' | 'success' | 'neutral' | 'danger'
> = {
  0: 'warning',
  1: 'success',
  2: 'neutral',
  3: 'danger',
};

const ACCION_TEXTOS: Record<
  Exclude<AccionCuentaBancariaProveedor, 'predeterminada'>,
  { titulo: string; descripcion: string; etiqueta: string; accion: string; ok: string }
> = {
  rechazar: {
    titulo: 'Rechazar cuenta',
    descripcion:
      'La cuenta quedará Rechazada y no se podrá pagar a ella. El motivo se guarda en la bitácora.',
    etiqueta: 'Motivo del rechazo',
    accion: 'Rechazar',
    ok: 'Cuenta rechazada',
  },
  inhabilitar: {
    titulo: 'Inhabilitar cuenta',
    descripcion:
      'Tesorería dejará de pagar a esta cuenta hasta que se reactive. El motivo se guarda en la bitácora.',
    etiqueta: 'Motivo de la inhabilitación',
    accion: 'Inhabilitar',
    ok: 'Cuenta inhabilitada',
  },
  reactivar: {
    titulo: 'Reactivar cuenta',
    descripcion: 'La cuenta volverá a quedar Activa. El motivo se guarda en la bitácora.',
    etiqueta: 'Motivo de la reactivación',
    accion: 'Reactivar',
    ok: 'Cuenta reactivada',
  },
};

function mensajeError(error: unknown): string {
  if (esApiError(error)) {
    return error.problem.detail ?? error.problem.title;
  }
  return 'Error inesperado';
}

function fmtFecha(iso: string | null): string {
  if (iso == null) return '';
  return new Date(iso).toLocaleDateString('es-MX', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

/** «Rechazada/Inhabilitada/Reactivada por X · fecha», o quién la capturó/validó; el motivo va en otra línea. */
function textoHistorial(c: CuentaBancariaProveedor): string | null {
  const etiqueta =
    c.estado === EstadoCuentaBancariaProveedor.Rechazada
      ? 'Rechazada'
      : c.estado === EstadoCuentaBancariaProveedor.Inhabilitada
        ? 'Inhabilitada'
        : c.estado === EstadoCuentaBancariaProveedor.Activa && c.ultimoCambioEn
          ? 'Reactivada'
          : null;
  if (etiqueta && (c.ultimoCambioPorNombre || c.ultimoCambioEn)) {
    return `${etiqueta}${c.ultimoCambioPorNombre ? ` por ${c.ultimoCambioPorNombre}` : ''}${c.ultimoCambioEn ? ` · ${fmtFecha(c.ultimoCambioEn)}` : ''}`;
  }
  if (c.estado === EstadoCuentaBancariaProveedor.PorValidar) {
    return c.capturadaPorNombre ? `Capturó ${c.capturadaPorNombre}` : null;
  }
  return c.validadaPorNombre
    ? `Validó ${c.validadaPorNombre}${c.validadaEn ? ` · ${fmtFecha(c.validadaEn)}` : ''}`
    : null;
}

export interface ProveedorCuentasBancariasSectionProps {
  proveedorId: string;
  /** Prellena el titular de la cuenta nueva. */
  razonSocial?: string;
}

/**
 * Cuentas bancarias del proveedor (varias por proveedor). Visible con
 * <c>bancarios-ver</c>; agregar con <c>bancarios-editar</c>;
 * validar/rechazar/inhabilitar/reactivar y marcar predeterminada con
 * <c>cuentas-administrar</c>. Quien captura una cuenta no puede validarla
 * (el backend lo exige; la UI lo anticipa con <c>capturadaPorMi</c>).
 */
export function ProveedorCuentasBancariasSection({
  proveedorId,
  razonSocial,
}: ProveedorCuentasBancariasSectionProps) {
  const canVer = useHasPermission(PermisosCanonicos.DatosMaestrosProveedoresBancariosVer);
  const canEditar = useHasPermission(PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar);
  const canAdministrar = useHasPermission(
    PermisosCanonicos.DatosMaestrosProveedoresCuentasAdministrar,
  );
  const query = useCuentasBancariasProveedor(canVer ? proveedorId : null);
  const [agregando, setAgregando] = useState(false);
  const [validando, setValidando] = useState<CuentaBancariaProveedor | null>(null);
  const [motivoDe, setMotivoDe] = useState<{
    cuenta: CuentaBancariaProveedor;
    accion: Exclude<AccionCuentaBancariaProveedor, 'predeterminada'>;
  } | null>(null);
  const [errorMotivo, setErrorMotivo] = useState<string | null>(null);
  const cambiar = useCambiarCuentaBancariaProveedor();

  if (!canVer) return null;

  if (query.isError) {
    return (
      <div className="mt-4">
        <ErrorState
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      </div>
    );
  }

  if (query.isLoading || query.data == null) {
    return (
      <div className="mt-4">
        <TableSkeleton rows={3} columns={[{ width: 'w-40' }, { width: 'w-56' }]} />
      </div>
    );
  }

  const cuentas = query.data;

  function hacerPredeterminada(c: CuentaBancariaProveedor) {
    cambiar.mutate(
      {
        proveedorId,
        cuentaId: c.id,
        version: c.version,
        accion: 'predeterminada',
        // Key fresca por acción: una sola sección sirve a varias cuentas/acciones.
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => toast.success('Cuenta predeterminada actualizada'),
        onError: (e) => toast.error(mensajeError(e)),
      },
    );
  }

  async function confirmarMotivo(motivo: string) {
    if (motivoDe == null) return;
    setErrorMotivo(null);
    try {
      await cambiar.mutateAsync({
        proveedorId,
        cuentaId: motivoDe.cuenta.id,
        version: motivoDe.cuenta.version,
        accion: motivoDe.accion,
        motivo,
        // Key fresca por acción: una sola sección sirve a varias cuentas/acciones.
        idempotencyKey: crypto.randomUUID(),
      });
      toast.success(ACCION_TEXTOS[motivoDe.accion].ok);
      setMotivoDe(null);
    } catch (e) {
      setErrorMotivo(mensajeError(e));
    }
  }

  return (
    <section
      aria-labelledby="cuentas-bancarias-titulo"
      className="mt-4 rounded-lg bg-surface-card p-4 shadow-card-flat"
      data-component="proveedor-cuentas-bancarias"
    >
      <div className="mb-3 flex items-center justify-between gap-2">
        <h3 id="cuentas-bancarias-titulo" className="text-md font-semibold text-ink">
          Cuentas bancarias
        </h3>
        {canEditar && (
          <Button type="button" variant="secondary" onClick={() => setAgregando(true)}>
            <Plus />
            Agregar cuenta
          </Button>
        )}
      </div>

      {cuentas.length === 0 ? (
        <p className="py-3 text-sm text-ink-muted">
          Este proveedor aún no tiene cuentas bancarias registradas.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <Table className="w-full text-sm">
            <TableHeader>
              <TableRow className="bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <TableHead scope="col" className="px-3 py-2">
                  Banco
                </TableHead>
                <TableHead scope="col" className="px-3 py-2">
                  Número
                </TableHead>
                <TableHead scope="col" className="px-3 py-2">
                  Moneda
                </TableHead>
                <TableHead scope="col" className="px-3 py-2">
                  Titular
                </TableHead>
                <TableHead scope="col" className="px-3 py-2">
                  Uso
                </TableHead>
                <TableHead scope="col" className="px-3 py-2">
                  Estado
                </TableHead>
                <TableHead scope="col" className="px-3 py-2">
                  Acciones
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {cuentas.map((c) => (
                <FilaCuenta
                  key={c.id}
                  cuenta={c}
                  canAdministrar={canAdministrar}
                  onValidar={() => setValidando(c)}
                  onMotivo={(accion) => {
                    setErrorMotivo(null);
                    setMotivoDe({ cuenta: c, accion });
                  }}
                  onPredeterminada={() => hacerPredeterminada(c)}
                  pendiente={cambiar.isPending}
                />
              ))}
            </TableBody>
          </Table>
        </div>
      )}
      <p className="mt-3 text-xs text-ink-muted">
        Tesorería solo paga a cuentas activas. Sin cuenta activa no se le puede pagar al proveedor.
      </p>

      {agregando && (
        <AgregarCuentaSheet
          proveedorId={proveedorId}
          razonSocial={razonSocial}
          onCerrar={() => setAgregando(false)}
        />
      )}
      {validando && (
        <ValidarCuentaDialog
          proveedorId={proveedorId}
          cuenta={validando}
          onCerrar={() => setValidando(null)}
        />
      )}
      {motivoDe && (
        <MotivoBajaDialog
          nombreArchivo={motivoDe.cuenta.banco}
          {...ACCION_TEXTOS[motivoDe.accion]}
          variante={motivoDe.accion === 'reactivar' ? 'default' : 'secondary-danger'}
          error={errorMotivo}
          onConfirmar={confirmarMotivo}
          onCerrar={() => setMotivoDe(null)}
        />
      )}
    </section>
  );
}

interface FilaCuentaProps {
  cuenta: CuentaBancariaProveedor;
  canAdministrar: boolean;
  pendiente: boolean;
  onValidar: () => void;
  onMotivo: (accion: Exclude<AccionCuentaBancariaProveedor, 'predeterminada'>) => void;
  onPredeterminada: () => void;
}

function FilaCuenta({
  cuenta: c,
  canAdministrar,
  pendiente,
  onValidar,
  onMotivo,
  onPredeterminada,
}: FilaCuentaProps) {
  const motivoId = useId();
  const quien = textoHistorial(c);
  const bloqueadaPorMi = c.capturadaPorMi;

  return (
    <TableRow className="border-b border-line-row align-top" data-cuenta-id={c.id}>
      <TableCell className="px-3 py-2.5">{c.banco}</TableCell>
      <TableCell className="px-3 py-2.5">
        <span className="font-mono text-xs">{c.numero}</span>
        <span className="block text-2xs text-ink-muted">
          {c.esClabe ? 'CLABE' : `Cuenta extranjera${c.swift ? ` · SWIFT ${c.swift}` : ''}`}
        </span>
      </TableCell>
      <TableCell className="px-3 py-2.5">{c.moneda}</TableCell>
      <TableCell className="px-3 py-2.5">{c.titular}</TableCell>
      <TableCell className="px-3 py-2.5">{c.predeterminada ? 'Predeterminada' : '—'}</TableCell>
      <TableCell className="px-3 py-2.5">
        <Badge variant={BADGE_ESTADO[c.estado]}>
          {EstadoCuentaBancariaProveedorLabels[c.estado]}
        </Badge>
        {quien && <span className="mt-1 block text-2xs text-ink-muted">{quien}</span>}
        {c.motivoEstado && (
          <span className="mt-0.5 block text-2xs text-ink-muted">{c.motivoEstado}</span>
        )}
      </TableCell>
      <TableCell className="px-3 py-2.5">
        <div className="flex flex-wrap gap-1.5">
          {canAdministrar && c.estado === EstadoCuentaBancariaProveedor.PorValidar && (
            <>
              <Button
                type="button"
                size="sm"
                variant="secondary"
                onClick={onValidar}
                disabled={bloqueadaPorMi}
                aria-describedby={bloqueadaPorMi ? motivoId : undefined}
                title={bloqueadaPorMi ? 'Tú la capturaste; pide a otro administrador' : undefined}
              >
                Validar
              </Button>
              <Button
                type="button"
                size="sm"
                variant="secondary-danger"
                onClick={() => onMotivo('rechazar')}
              >
                Rechazar
              </Button>
            </>
          )}
          {canAdministrar && c.estado === EstadoCuentaBancariaProveedor.Activa && (
            <Button
              type="button"
              size="sm"
              variant="secondary-danger"
              onClick={() => onMotivo('inhabilitar')}
            >
              Inhabilitar
            </Button>
          )}
          {canAdministrar && c.estado === EstadoCuentaBancariaProveedor.Inhabilitada && (
            <Button
              type="button"
              size="sm"
              variant="secondary"
              onClick={() => onMotivo('reactivar')}
            >
              Reactivar
            </Button>
          )}
          {canAdministrar &&
            c.estado === EstadoCuentaBancariaProveedor.Activa &&
            !c.predeterminada && (
              <Button
                type="button"
                size="sm"
                variant="secondary"
                onClick={onPredeterminada}
                disabled={pendiente}
              >
                Hacer predeterminada
              </Button>
            )}
        </div>
        {canAdministrar &&
          c.estado === EstadoCuentaBancariaProveedor.PorValidar &&
          bloqueadaPorMi && (
            <p id={motivoId} className="mt-1 text-2xs text-ink-muted">
              Tú la capturaste; pide a otro administrador.
            </p>
          )}
      </TableCell>
    </TableRow>
  );
}

// ─── Agregar cuenta ─────────────────────────────────────────────────

const CAMPO_POR_CODIGO: Record<string, 'clabe' | 'numeroCuenta'> = {
  CLABE_DIGITO_CONTROL: 'clabe',
  CLABE_FORMATO: 'clabe',
  PROVEEDOR_CUENTA_SIN_NUMERO: 'numeroCuenta',
  // Nacional: se pinta en la CLABE; extranjera: onError lo pasa a numeroCuenta.
  PROVEEDOR_CUENTA_DUPLICADA: 'clabe',
};

function AgregarCuentaSheet({
  proveedorId,
  razonSocial,
  onCerrar,
}: {
  proveedorId: string;
  razonSocial?: string;
  onCerrar: () => void;
}) {
  const uid = useId();
  const agregar = useAgregarCuentaBancariaProveedor();
  const idempotencyKey = useFormIdempotencyKey();
  const [banco, setBanco] = useState('');
  const [extranjera, setExtranjera] = useState(false);
  const [clabe, setClabe] = useState('');
  const [numeroCuenta, setNumeroCuenta] = useState('');
  const [swift, setSwift] = useState('');
  const [moneda, setMoneda] = useState<'MXN' | 'USD'>('MXN');
  const [titular, setTitular] = useState(razonSocial ?? '');
  const [errores, setErrores] = useState<
    Partial<Record<'clabe' | 'numeroCuenta' | 'general', string>>
  >({});

  const clabeInvalida = !extranjera && clabe !== '' && !/^\d{18}$/.test(clabe);
  // El SWIFT es opcional (el backend lo acepta vacío); si se captura debe tener 8 u 11 caracteres.
  const swiftInvalido =
    extranjera && swift.trim() !== '' && !/^[A-Za-z0-9]{8}([A-Za-z0-9]{3})?$/.test(swift.trim());
  const faltante =
    banco.trim() === '' ||
    titular.trim() === '' ||
    swiftInvalido ||
    (extranjera ? numeroCuenta.trim() === '' : !/^\d{18}$/.test(clabe));
  const motivoDeshabilitado = faltante
    ? 'Completa banco, titular y el número de cuenta (CLABE de 18 dígitos o número de cuenta; el SWIFT es opcional).'
    : null;

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (faltante) return;
    setErrores({});
    agregar.mutate(
      {
        proveedorId,
        idempotencyKey,
        payload: {
          banco: banco.trim(),
          clabe: extranjera ? null : clabe,
          numeroCuenta: extranjera ? numeroCuenta.trim() : null,
          swift: extranjera && swift.trim() !== '' ? swift.trim().toUpperCase() : null,
          moneda,
          titular: titular.trim(),
        },
      },
      {
        onSuccess: (cuenta) => {
          toast.success('Cuenta agregada; queda Por validar');
          // La misma cuenta en otro proveedor no bloquea el alta, pero quien valida debe saberlo.
          if (cuenta.advertencia) toast.warning(cuenta.advertencia, { duration: 15_000 });
          onCerrar();
        },
        onError: (error) => {
          const codigos = esApiError(error)
            ? [error.problem.code, ...(error.problem.errores ?? []).map((x) => x.codigo)]
            : [];
          const codigo = codigos.find((c) => c != null && c in CAMPO_POR_CODIGO);
          const mensaje = mensajeError(error);
          if (codigo) {
            let campo = CAMPO_POR_CODIGO[codigo];
            if (campo === 'clabe' && extranjera) campo = 'numeroCuenta';
            setErrores({ [campo]: mensaje });
          } else {
            setErrores({ general: mensaje });
          }
        },
      },
    );
  }

  const ctlSelect =
    'h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand';

  return (
    <Sheet open onOpenChange={(o) => !o && !agregar.isPending && onCerrar()}>
      <SheetContent size="lg" aria-label="Agregar cuenta bancaria">
        <SheetHeader>
          <SheetTitle className="text-xl font-semibold">Agregar cuenta</SheetTitle>
          <SheetDescription className="text-xs text-ink-muted">
            La cuenta queda Por validar; la valida un administrador distinto de quien la captura.
          </SheetDescription>
        </SheetHeader>

        <form
          onSubmit={onSubmit}
          className="flex min-h-0 flex-1 flex-col overflow-hidden"
          noValidate
        >
          <SheetBody>
            <FormSeccion titulo="Datos del registro">
              <FormCampo span={12}>
                <div className="grid content-start gap-3.5">
                  <div className="grid gap-1.5">
                    <Label htmlFor={`${uid}-banco`} className="text-xs font-medium text-ink-strong">
                      Banco
                    </Label>
                    <Input
                      id={`${uid}-banco`}
                      maxLength={120}
                      value={banco}
                      onChange={(e) => setBanco(e.target.value)}
                    />
                  </div>
                  <div className="grid gap-1.5">
                    <Label htmlFor={`${uid}-tipo`} className="text-xs font-medium text-ink-strong">
                      Tipo de cuenta
                    </Label>
                    <select
                      id={`${uid}-tipo`}
                      className={ctlSelect}
                      value={extranjera ? 'extranjera' : 'nacional'}
                      onChange={(e) => setExtranjera(e.target.value === 'extranjera')}
                    >
                      <option value="nacional">Nacional (CLABE de 18 dígitos)</option>
                      <option value="extranjera">Extranjera (número de cuenta y SWIFT)</option>
                    </select>
                  </div>
                  {extranjera ? (
                    <>
                      <div className="grid gap-1.5">
                        <Label
                          htmlFor={`${uid}-num`}
                          className="text-xs font-medium text-ink-strong"
                        >
                          Número de cuenta
                        </Label>
                        <Input
                          id={`${uid}-num`}
                          className="font-mono"
                          maxLength={34}
                          value={numeroCuenta}
                          onChange={(e) => setNumeroCuenta(e.target.value)}
                          aria-invalid={errores.numeroCuenta != null}
                          aria-describedby={errores.numeroCuenta ? `${uid}-num-err` : undefined}
                        />
                        {errores.numeroCuenta && (
                          <p id={`${uid}-num-err`} className="text-xs text-danger-fg">
                            {errores.numeroCuenta}
                          </p>
                        )}
                      </div>
                      <div className="grid gap-1.5">
                        <Label
                          htmlFor={`${uid}-swift`}
                          className="text-xs font-medium text-ink-strong"
                        >
                          SWIFT <span className="font-normal text-ink-muted">(opcional)</span>
                        </Label>
                        <Input
                          id={`${uid}-swift`}
                          className="font-mono"
                          maxLength={11}
                          value={swift}
                          onChange={(e) => setSwift(e.target.value)}
                          aria-invalid={swiftInvalido}
                          aria-describedby={`${uid}-swift-ayuda`}
                        />
                        <p
                          id={`${uid}-swift-ayuda`}
                          className={
                            swiftInvalido ? 'text-xs text-danger-fg' : 'text-xs text-ink-muted'
                          }
                        >
                          {swiftInvalido
                            ? 'El SWIFT/BIC debe tener 8 u 11 caracteres alfanuméricos.'
                            : '8 u 11 caracteres.'}
                        </p>
                      </div>
                    </>
                  ) : (
                    <div className="grid gap-1.5">
                      <Label
                        htmlFor={`${uid}-clabe`}
                        className="text-xs font-medium text-ink-strong"
                      >
                        CLABE
                      </Label>
                      <Input
                        id={`${uid}-clabe`}
                        className="font-mono"
                        inputMode="numeric"
                        maxLength={18}
                        value={clabe}
                        onChange={(e) => setClabe(e.target.value.replace(/\D/g, ''))}
                        aria-invalid={clabeInvalida || errores.clabe != null}
                        aria-describedby={`${uid}-clabe-ayuda`}
                      />
                      <p
                        id={`${uid}-clabe-ayuda`}
                        className={
                          errores.clabe || clabeInvalida
                            ? 'text-xs text-danger-fg'
                            : 'text-xs text-ink-muted'
                        }
                      >
                        {errores.clabe ??
                          (clabeInvalida ? 'La CLABE debe tener 18 dígitos.' : '18 dígitos.')}
                      </p>
                    </div>
                  )}
                  <div className="grid gap-1.5">
                    <Label
                      htmlFor={`${uid}-moneda`}
                      className="text-xs font-medium text-ink-strong"
                    >
                      Moneda
                    </Label>
                    <select
                      id={`${uid}-moneda`}
                      className={ctlSelect}
                      value={moneda}
                      onChange={(e) => setMoneda(e.target.value as 'MXN' | 'USD')}
                    >
                      <option value="MXN">MXN</option>
                      <option value="USD">USD</option>
                    </select>
                  </div>
                  <div className="grid gap-1.5">
                    <Label
                      htmlFor={`${uid}-titular`}
                      className="text-xs font-medium text-ink-strong"
                    >
                      Titular
                    </Label>
                    <Input
                      id={`${uid}-titular`}
                      maxLength={254}
                      value={titular}
                      onChange={(e) => setTitular(e.target.value)}
                    />
                  </div>
                  {errores.general && (
                    <p role="alert" className="text-xs text-danger-fg">
                      {errores.general}
                    </p>
                  )}
                </div>
              </FormCampo>
            </FormSeccion>
          </SheetBody>
          <SheetFooter className="items-center">
            {motivoDeshabilitado && (
              <p className="mr-auto text-xs text-ink-muted">{motivoDeshabilitado}</p>
            )}
            <Button
              type="button"
              variant="ghost"
              size="lg"
              onClick={onCerrar}
              disabled={agregar.isPending}
            >
              Cancelar
            </Button>
            <Button
              type="submit"
              size="lg"
              disabled={faltante || agregar.isPending}
              title={motivoDeshabilitado ?? undefined}
            >
              {agregar.isPending && <Loader2 className="animate-spin" />}
              Agregar cuenta
            </Button>
          </SheetFooter>
        </form>
      </SheetContent>
    </Sheet>
  );
}

// ─── Validar cuenta ─────────────────────────────────────────────────

const COTEJOS = [
  'El titular coincide con la razón social del proveedor.',
  'La CLABE o número de cuenta coincide con el documento.',
  'El banco y la moneda coinciden con el documento.',
  'El documento tiene fecha de los últimos 3 meses.',
] as const;

function ValidarCuentaDialog({
  proveedorId,
  cuenta,
  onCerrar,
}: {
  proveedorId: string;
  cuenta: CuentaBancariaProveedor;
  onCerrar: () => void;
}) {
  const uid = useId();
  const validar = useValidarCuentaBancariaProveedor();
  const idempotencyKey = useFormIdempotencyKey();
  const canAdjuntosVer = useHasPermission(PermisosCanonicos.DatosMaestrosProveedoresAdjuntosVer);
  const adjuntos = useAdjuntos(`/api/v1/datos-maestros/proveedores/${proveedorId}`, {
    enabled: canAdjuntosVer,
  });
  const documentos = (adjuntos.data ?? []).filter(
    (a) =>
      a.tipoDocumentoCodigo === TIPO_ESTADO_CUENTA &&
      (a.estado === EstadoAdjunto.Vigente || a.estado === EstadoAdjunto.PorVencer),
  );
  const [documentoId, setDocumentoId] = useState('');
  const [cotejos, setCotejos] = useState<boolean[]>(COTEJOS.map(() => false));
  const [nota, setNota] = useState('');
  const [error, setError] = useState<string | null>(null);

  const cotejoCompleto = cotejos.every(Boolean);
  const motivoDeshabilitado =
    documentoId === ''
      ? 'Selecciona el estado de cuenta que respalda la cuenta.'
      : !cotejoCompleto
        ? 'Marca los cuatro puntos del cotejo para validar.'
        : null;

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (motivoDeshabilitado) return;
    setError(null);
    validar.mutate(
      {
        proveedorId,
        cuentaId: cuenta.id,
        version: cuenta.version,
        idempotencyKey,
        payload: {
          documentoAdjuntoId: documentoId,
          cotejoCompleto,
          nota: nota.trim() === '' ? null : nota.trim(),
        },
      },
      {
        onSuccess: () => {
          toast.success('Cuenta validada; ya está Activa');
          onCerrar();
        },
        onError: (err) => setError(mensajeError(err)),
      },
    );
  }

  return (
    <Dialog open onOpenChange={(o) => !o && !validar.isPending && onCerrar()}>
      <DialogContent size="sm" closeLabel="Cerrar" data-component="validar-cuenta-dialog">
        <form onSubmit={onSubmit} className="flex min-h-0 flex-1 flex-col overflow-hidden">
          <DialogBody>
            <FormSeccion titulo="Datos del registro">
              <FormCampo span={12}>
                <DialogHeader>
                  <DialogTitle>Validar cuenta bancaria</DialogTitle>
                  <DialogDescription>
                    Coteja la cuenta contra un estado de cuenta vigente. Al validar queda Activa y
                    Tesorería podrá pagar a ella.
                  </DialogDescription>
                </DialogHeader>
              </FormCampo>
              <FormCampo span={12}>
                <dl className="grid grid-cols-2 gap-2 rounded-md bg-surface-subtle p-3 text-sm">
                  <div>
                    <dt className="text-xs text-ink-muted">Banco</dt>
                    <dd>{cuenta.banco}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-ink-muted">Número</dt>
                    <dd className="font-mono text-xs">{cuenta.numero}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-ink-muted">Moneda</dt>
                    <dd>{cuenta.moneda}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-ink-muted">Titular</dt>
                    <dd>{cuenta.titular}</dd>
                  </div>
                </dl>
              </FormCampo>

              <FormCampo span={12}>
                <div className="grid gap-1.5">
                  <Label htmlFor={`${uid}-doc`} className="text-xs font-medium text-ink-strong">
                    Estado de cuenta
                  </Label>
                  {documentos.length > 0 ? (
                    <select
                      id={`${uid}-doc`}
                      className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
                      value={documentoId}
                      onChange={(e) => setDocumentoId(e.target.value)}
                    >
                      <option value="">Selecciona un documento</option>
                      {documentos.map((d) => (
                        <option key={d.id} value={d.id}>
                          {d.nombreArchivo}
                          {d.vigenteHasta ? ` · vigente hasta ${d.vigenteHasta}` : ''}
                        </option>
                      ))}
                    </select>
                  ) : (
                    <p
                      role="note"
                      className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-xs text-warning-note-fg"
                    >
                      <TriangleAlert className="size-4 shrink-0" aria-hidden="true" />
                      <span>
                        {canAdjuntosVer
                          ? 'No hay un estado de cuenta vigente en el expediente. '
                          : 'No tienes permiso para ver el expediente del proveedor. '}
                        <a
                          href="#expediente-proveedor"
                          className="font-medium text-brand underline"
                        >
                          Adjúntalo en la sección Expediente
                        </a>{' '}
                        (tipo Estado de cuenta bancaria) y vuelve a validar.
                      </span>
                    </p>
                  )}
                </div>
              </FormCampo>

              <FormCampo span={12}>
                <fieldset className="grid gap-2">
                  <legend className="mb-1 text-sm font-semibold text-ink">Lista de cotejo</legend>
                  {COTEJOS.map((texto, i) => (
                    <label
                      key={texto}
                      className="flex cursor-pointer items-start gap-2 text-sm text-ink"
                    >
                      <Checkbox
                        checked={cotejos[i]}
                        onCheckedChange={(v) =>
                          setCotejos((prev) => prev.map((x, j) => (j === i ? v === true : x)))
                        }
                      />
                      {texto}
                    </label>
                  ))}
                </fieldset>
              </FormCampo>

              <FormCampo span={12}>
                <div className="grid gap-1.5">
                  <Label htmlFor={`${uid}-nota`} className="text-xs font-medium text-ink-strong">
                    Nota <span className="font-normal text-ink-muted">(opcional)</span>
                  </Label>
                  <Textarea
                    id={`${uid}-nota`}
                    rows={2}
                    maxLength={500}
                    value={nota}
                    onChange={(e) => setNota(e.target.value)}
                    placeholder="Ej. Llamada al banco o depósito de prueba"
                  />
                </div>
              </FormCampo>

              <FormCampo span={12}>
                {error && (
                  <p role="alert" className="text-xs text-danger-fg">
                    {error}
                  </p>
                )}
              </FormCampo>
            </FormSeccion>
          </DialogBody>
          <DialogFooter className="items-center">
            {motivoDeshabilitado && (
              <p className="mr-auto text-xs text-ink-muted">{motivoDeshabilitado}</p>
            )}
            <Button type="button" variant="ghost" onClick={onCerrar} disabled={validar.isPending}>
              Cancelar
            </Button>
            <Button
              type="submit"
              disabled={motivoDeshabilitado != null || validar.isPending}
              title={motivoDeshabilitado ?? undefined}
            >
              {validar.isPending && <Loader2 className="animate-spin" />}
              Validar
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
