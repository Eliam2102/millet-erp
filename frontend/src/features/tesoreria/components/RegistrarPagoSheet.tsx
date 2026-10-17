import { ConceptoSelector } from './ConceptoSelector';
import { hoyLocalISO } from '@/lib/datetime';
import { useMemo, useState } from 'react';
import { toast } from 'sonner';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { CuentaBancariaSelector } from './CuentaBancariaSelector';
import { Link } from '@tanstack/react-router';
import { Checkbox } from '@/components/ui/checkbox';
import {
  useCuentasProveedorPago,
  useRegistrarPago,
} from '@/features/tesoreria/api/useTesoreria';
import type { CuentaProveedorPago, PasivoPendienteResponse } from '@/features/tesoreria/api/types';
import { formatoFecha, formatoMonto } from '@/features/tesoreria/lib/formato';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

export interface RegistrarPagoSheetProps {
  /** Pasivos seleccionados en la bandeja (mismo proveedor y moneda — validado aquí y en backend). */
  pasivos: PasivoPendienteResponse[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess: () => void;
}

/**
 * Sheet "Registrar pago" (TES-FE-PR2, 05-frontend-diseno §4.2): resumen
 * de pasivos con importes editables (default: saldo), cuenta de egreso
 * filtrada por moneda (RN-3 en UI), fecha valor y referencia bancaria.
 * El operador ejecuta la transferencia en la banca y AQUÍ registra el
 * hecho — al confirmar, CxP recibe <c>aplicado.v1</c> por factura.
 */
export function RegistrarPagoSheet({
  pasivos,
  open,
  onOpenChange,
  onSuccess,
}: RegistrarPagoSheetProps) {
  const registrar = useRegistrarPago();
  const keyFor = useBodyScopedIdempotencyKey();

  const [conceptoId, setConceptoId] = useState<string | null>(null);
  const [cuentaId, setCuentaId] = useState<string | null>(null);
  const [fechaValor, setFechaValor] = useState(() =>
    hoyLocalISO(),
  );
  const [referencia, setReferencia] = useState('');
  const [cuentaProvOverride, setCuentaProvOverride] = useState<string | null>(null);
  const [fondosVerificados, setFondosVerificados] = useState(false);
  // Rechazo 422 de cuenta del proveedor: visible y persistente (no solo toast).
  const [errorCuenta, setErrorCuenta] = useState<string | null>(null);
  // Importes: default = saldo del pasivo; el usuario puede sobrescribir.
  // Sin useEffect (lint set-state-in-effect): estado solo de overrides y
  // el efectivo se deriva en render. Se limpian al cerrar el Sheet.
  const [overrides, setOverrides] = useState<Record<string, string>>({});
  const importes = useMemo(
    () =>
      Object.fromEntries(
        pasivos.map((p) => [
          p.facturaProveedorId,
          overrides[p.facturaProveedorId] ?? String(p.saldoPendiente),
        ]),
      ),
    [pasivos, overrides],
  );

  const moneda = pasivos[0]?.moneda ?? null;
  const proveedor = pasivos[0]?.proveedorRazonSocial ?? pasivos[0]?.proveedorId;

  // Los pasivos internos (reposición de caja, viáticos) no se pagan a una cuenta de proveedor.
  const esInterno = pasivos[0]?.tipoBeneficiario != null && pasivos[0].tipoBeneficiario !== 'Proveedor';
  const cuentasQuery = useCuentasProveedorPago(open && !esInterno ? (pasivos[0]?.proveedorId ?? null) : null);
  const cuentas = cuentasQuery.data;
  const elegible = (c: CuentaProveedorPago) => c.estado === 'Activa' && c.moneda === moneda;
  const activas = (cuentas ?? []).filter(elegible);
  // Selección derivada en render (sin useEffect): override del usuario o la
  // predeterminada activa de la moneda del pasivo.
  const cuentaProvId =
    cuentaProvOverride != null && activas.some((c) => c.id === cuentaProvOverride)
      ? cuentaProvOverride
      : (activas.find((c) => c.predeterminada) ?? activas[0])?.id ?? null;
  const sinCuentaActiva = !esInterno && cuentas != null && activas.length === 0;

  const total = useMemo(
    () =>
      pasivos.reduce(
        (acc, p) => acc + (Number(importes[p.facturaProveedorId]) || 0),
        0,
      ),
    [pasivos, importes],
  );

  const importesValidos = pasivos.every((p) => {
    const v = Number(importes[p.facturaProveedorId]);
    return Number.isFinite(v) && v > 0 && v <= p.saldoPendiente;
  });

  const motivoDeshabilitado =
    !esInterno && cuentasQuery.isLoading
      ? 'Cargando cuentas del proveedor…'
      : !esInterno && cuentasQuery.isError
        ? 'No se pudieron cargar las cuentas del proveedor'
        : sinCuentaActiva
          ? `El proveedor no tiene una cuenta activa en ${moneda}`
          : !esInterno && cuentaProvId == null
            ? 'Selecciona la cuenta del proveedor'
            : cuentaId == null
              ? 'Selecciona la cuenta de egreso'
              : !importesValidos
                ? 'Revisa los importes a pagar'
                : !fondosVerificados
                  ? 'Confirma que verificaste fondos en la banca'
                  : null;

  function confirmar() {
    if (cuentaId == null || !importesValidos || (!esInterno && cuentaProvId == null) || !fondosVerificados) return;
    setErrorCuenta(null);
    const command = {
      cuentaProveedorId: esInterno ? undefined : (cuentaProvId ?? undefined),
      fondosVerificados,
      cuentaBancariaId: cuentaId,
      conceptoId: conceptoId ?? undefined,
      fechaValor,
      referenciaBancaria: referencia.trim() || undefined,
      aplicaciones: pasivos.map((p) => ({
        facturaProveedorId: p.facturaProveedorId,
        importe: Number(importes[p.facturaProveedorId]),
      })),
    };
    registrar.mutate(
      {
        command,
        // Key ligada al contenido: un reintento tras timeout re-envía la
        // MISMA key+body y el backend devuelve la respuesta cacheada, en vez
        // de registrar un SEGUNDO desembolso. Corregir el pago tras un
        // rechazo cambia el body → key nueva (sin KEY_REUSED).
        idempotencyKey: keyFor(command),
      },
      {
        onSuccess: (r) => {
          toast.success('Pago registrado', {
            description: `${formatoMonto(r.monto, r.moneda)} aplicado a ${r.aplicaciones.length} pasivo(s). CxP recibirá el evento.`,
          });
          cambiarAbierto(false);
          onSuccess();
        },
        onError: (error) => {
          if (
            esApiError(error) &&
            ['PROVEEDOR_SIN_CUENTA_ACTIVA', 'PROVEEDOR_CUENTA_NO_ACTIVA', 'PROVEEDOR_CUENTA_OTRA_MONEDA'].includes(
              error.code ?? '',
            )
          ) {
            setErrorCuenta(error.problem.detail ?? error.problem.title);
            return;
          }
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo registrar el pago',
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

  function cambiarAbierto(next: boolean) {
    if (!next) {
      // Reset completo al cerrar: el Sheet queda montado en BandejaPagos, así
      // que sin esto la referencia bancaria y la cuenta (¡de otra moneda!)
      // del pago anterior persistían al reabrir para otro proveedor →
      // referencia SPEI equivocada / cuenta de moneda inválida enviable.
      setOverrides({});
      setCuentaId(null);
      setReferencia('');
      setCuentaProvOverride(null);
      setFondosVerificados(false);
      setErrorCuenta(null);
      setFechaValor(hoyLocalISO());
    }
    onOpenChange(next);
  }

  return (
    <Sheet open={open} onOpenChange={cambiarAbierto}>
      <SheetContent side="right" className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>Registrar pago a proveedor</SheetTitle>
          <SheetDescription>
            {pasivos.length} pasivo(s) de {proveedor}. La transferencia se
            ejecuta en la banca; aquí se registra el hecho bancario.
          </SheetDescription>
        </SheetHeader>

        <div className="mt-4 space-y-4">
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 font-medium">Factura</th>
                  <th className="px-3 py-2 font-medium">Vence</th>
                  <th className="px-3 py-2 text-right font-medium">Saldo</th>
                  <th className="px-3 py-2 text-right font-medium">Importe a pagar</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {pasivos.map((p) => {
                  const v = Number(importes[p.facturaProveedorId]);
                  const invalido = !Number.isFinite(v) || v <= 0 || v > p.saldoPendiente;
                  return (
                    <tr key={p.facturaProveedorId}>
                      <td className="px-3 py-2">
                        <p className="font-mono text-xs">
                          {p.folioProveedor ?? p.facturaProveedorId.slice(0, 8)}
                        </p>
                      </td>
                      <td className="px-3 py-2 text-xs">
                        {formatoFecha(p.fechaVencimiento)}
                      </td>
                      <td className="px-3 py-2 text-right font-mono tabular-nums">
                        {formatoMonto(p.saldoPendiente, p.moneda)}
                      </td>
                      <td className="px-3 py-2 text-right">
                        <Input
                          type="number"
                          inputMode="decimal"
                          min={0.01}
                          max={p.saldoPendiente}
                          step="0.01"
                          className={`ml-auto w-36 text-right font-mono ${invalido ? 'border-destructive' : ''}`}
                          aria-label={`Importe para ${p.folioProveedor ?? p.facturaProveedorId}`}
                          value={importes[p.facturaProveedorId] ?? ''}
                          onChange={(e) =>
                            setOverrides((prev) => ({
                              ...prev,
                              [p.facturaProveedorId]: e.target.value,
                            }))
                          }
                        />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <fieldset className="space-y-2">
            <legend className="text-xs text-muted-foreground">Cuenta del proveedor</legend>
            {cuentasQuery.isLoading && (
              <p className="text-xs text-muted-foreground">Cargando cuentas…</p>
            )}
            {cuentasQuery.isError && (
              <p role="alert" className="text-xs text-destructive">
                No se pudieron cargar las cuentas del proveedor.
              </p>
            )}
            {sinCuentaActiva && (
              <div role="alert" className="rounded-md bg-warning-bg px-3 py-2 text-xs text-warning-fg">
                Este proveedor no tiene una cuenta activa en {moneda}. Regístrala y valídala en{' '}
                <Link
                  to="/admin/datos-maestros/proveedores/$id"
                  params={{ id: pasivos[0]?.proveedorId ?? '' }}
                  className="font-medium underline"
                >
                  Datos maestros
                </Link>
                .
              </div>
            )}
            {(cuentas ?? []).map((c) => {
              const ok = elegible(c);
              const motivo =
                c.estado === 'PorValidar'
                  ? 'Por validar'
                  : c.estado === 'Inhabilitada'
                    ? 'Inhabilitada'
                    : c.moneda !== moneda
                      ? `Otra moneda (${c.moneda})`
                      : null;
              return (
                <label
                  key={c.id}
                  className={`flex items-start gap-2 rounded-md border px-3 py-2 text-sm ${ok ? 'cursor-pointer' : 'cursor-not-allowed opacity-60'}`}
                >
                  <input
                    type="radio"
                    name="cuenta-proveedor"
                    className="mt-1"
                    checked={cuentaProvId === c.id}
                    disabled={!ok}
                    onChange={() => {
                      setCuentaProvOverride(c.id);
                      setErrorCuenta(null);
                    }}
                  />
                  <span className="flex-1">
                    <span className="flex flex-wrap items-center gap-2">
                      <span>{c.banco}</span>
                      <span className="font-mono text-xs">{c.numero}</span>
                      <span className="text-xs text-muted-foreground">{c.esClabe ? 'CLABE' : 'Cuenta'} · {c.moneda}</span>
                      {c.predeterminada && <Badge variant="info">Predeterminada</Badge>}
                      {motivo && <Badge variant="warning">{motivo}</Badge>}
                    </span>
                    {c.titular && (
                      <span className="block text-xs text-muted-foreground">{c.titular}</span>
                    )}
                  </span>
                </label>
              );
            })}
            {errorCuenta && (
              <p role="alert" className="rounded-md bg-danger-bg px-3 py-2 text-xs text-danger-fg">
                {errorCuenta}
              </p>
            )}
          </fieldset>

          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div className="space-y-1 sm:col-span-2">
              <label className="text-xs text-muted-foreground">
                Cuenta de egreso {moneda ? `(${moneda})` : ''}
              </label>
              <CuentaBancariaSelector
                value={cuentaId}
                onChange={setCuentaId}
                moneda={moneda}
              />
              <p className="text-xs text-muted-foreground">
                Saldo en libros: movimientos registrados en el ERP, no el saldo del banco.
              </p>
              <ConceptoSelector value={conceptoId} onChange={setConceptoId} />
            </div>
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground" htmlFor="fecha-valor">
                Fecha valor
              </label>
              <Input
                id="fecha-valor"
                type="date"
                value={fechaValor}
                onChange={(e) => setFechaValor(e.target.value)}
              />
            </div>
            <div className="space-y-1">
              <label className="text-xs text-muted-foreground" htmlFor="referencia">
                Referencia bancaria
              </label>
              <Input
                id="referencia"
                value={referencia}
                onChange={(e) => setReferencia(e.target.value)}
                maxLength={120}
                placeholder="SPEI-000123"
              />
            </div>
          </div>

          <div className="flex items-center justify-between rounded-md bg-muted/50 px-4 py-3">
            <span className="text-sm text-muted-foreground">Total a pagar</span>
            <span className="font-mono text-lg font-semibold tabular-nums">
              {moneda ? formatoMonto(total, moneda) : total.toFixed(2)}
            </span>
          </div>

          <div className="flex items-start gap-2">
            <Checkbox
              id="fondos-verificados"
              checked={fondosVerificados}
              onCheckedChange={(v) => setFondosVerificados(v === true)}
            />
            <label htmlFor="fondos-verificados" className="text-sm">
              Verifiqué en la banca que la cuenta tiene fondos para este pago
              <span className="block text-xs text-muted-foreground">
                El ERP no está conectado al banco: el saldo en libros puede no estar al día.
              </span>
            </label>
          </div>

          {motivoDeshabilitado && !registrar.isPending && (
            <p className="text-right text-xs text-muted-foreground">{motivoDeshabilitado}</p>
          )}

          <div className="flex justify-end gap-2">
            <Button
              variant="ghost"
              onClick={() => cambiarAbierto(false)}
              disabled={registrar.isPending}
            >
              Cancelar
            </Button>
            <Button
              onClick={confirmar}
              disabled={
                registrar.isPending || pasivos.length === 0 || motivoDeshabilitado != null
              }
            >
              {registrar.isPending ? 'Registrando…' : 'Registrar pago'}
            </Button>
          </div>
        </div>
      </SheetContent>
    </Sheet>
  );
}
