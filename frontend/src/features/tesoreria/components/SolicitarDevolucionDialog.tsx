import { useEffect, useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import { toast } from 'sonner';
import { z } from 'zod';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { AvisoEnLinea, Field } from '@/components/erp';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { applyServerErrors, esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useSolicitarDevolucionDeposito } from '@/features/tesoreria/api/useTesoreria';
import { BeneficiarioTipo } from '@/features/tesoreria/api/types';
import { ClienteTesoreriaSelector } from './ClienteTesoreriaSelector';

const Schema = z.object({
  clienteId: z.string().min(1, 'Selecciona el cliente.'),
  importe: z.number('Captura el importe.').positive('El importe debe ser mayor a cero.'),
  motivo: z.string().trim().min(1, 'Indica el motivo.').max(400),
  banco: z.string().trim().min(1, 'Captura el banco.').max(80),
  clabe: z.string().regex(/^\d{18}$/, 'La CLABE debe tener 18 dígitos.'),
  titular: z.string().trim().min(1, 'Captura el titular.').max(200),
});
type Values = z.infer<typeof Schema>;

const CAMPOS: ReadonlyArray<[keyof Values, string]> = [
  ['clienteId', 'el cliente'],
  ['importe', 'el importe'],
  ['motivo', 'el motivo'],
  ['banco', 'el banco'],
  ['clabe', 'la CLABE (18 dígitos)'],
  ['titular', 'el titular'],
];

export interface SolicitarDevolucionMovimiento {
  id: string;
  monto: number;
  moneda: string;
  beneficiarioTipo: BeneficiarioTipo | null;
  beneficiarioRef: string | null;
  /** Razón social del cliente identificado, para no mostrar «Cliente seleccionado». */
  beneficiarioNombre?: string | null;
}

export interface SolicitarDevolucionDialogProps {
  movimiento: SolicitarDevolucionMovimiento;
  abierto: boolean;
  onCerrar: () => void;
}

/**
 * Solicitud de devolución de un depósito erróneo (T9.6). Nunca se devuelve más
 * que el depósito (T9.6-a); la autorización la da otra persona (Gerente o Jefe
 * de Tesorería).
 */
export function SolicitarDevolucionDialog({ movimiento, abierto, onCerrar }: SolicitarDevolucionDialogProps) {
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(onCerrar);
  return (
    <Sheet open={abierto} onOpenChange={(next) => (next ? undefined : solicitarCierre())}>
      <SheetContent side="right" size="form">
        <SheetHeader>
          <SheetTitle>Solicitar devolución</SheetTitle>
          <SheetDescription>
            Devuelve al cliente un depósito erróneo. Queda pendiente de autorización del Gerente o Jefe de Tesorería.
          </SheetDescription>
        </SheetHeader>
        {abierto && (
          <Formulario
            movimiento={movimiento}
            onCancel={() => solicitarCierre()}
            onCreated={() => solicitarCierre(true)}
            onDirtyChange={setDirty}
          />
        )}
      </SheetContent>
      {dialogo}
    </Sheet>
  );
}

function Formulario({
  movimiento,
  onCancel,
  onCreated,
  onDirtyChange,
}: {
  movimiento: SolicitarDevolucionMovimiento;
  onCancel: () => void;
  onCreated: () => void;
  onDirtyChange: (dirty: boolean) => void;
}) {
  const navigate = useNavigate();
  const keyFor = useBodyScopedIdempotencyKey();
  const solicitar = useSolicitarDevolucionDeposito();
  const [aviso, setAviso] = useState<string | null>(null);
  const identificado =
    movimiento.beneficiarioTipo === BeneficiarioTipo.Cliente && movimiento.beneficiarioRef != null;

  const form = useForm<Values>({
    resolver: zodResolver(Schema),
    defaultValues: {
      clienteId: identificado ? movimiento.beneficiarioRef! : '',
      importe: movimiento.monto,
      motivo: '',
      banco: '',
      clabe: '',
      titular: '',
    },
  });
  const valores = useWatch({ control: form.control });
  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  const faltantes = CAMPOS.filter(([campo]) => {
    const v = valores[campo];
    if (campo === 'importe') return !(typeof v === 'number' && v > 0);
    if (campo === 'clabe') return !/^\d{18}$/.test(String(v ?? ''));
    return String(v ?? '').trim() === '';
  }).map(([, etiqueta]) => etiqueta);

  function onSubmit(values: Values) {
    setAviso(null);
    const body = { movimientoId: movimiento.id, ...values };
    solicitar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: (res) => {
          toast.success('Devolución solicitada', {
            description: 'Queda pendiente de autorización del Gerente o Jefe de Tesorería.',
          });
          onCreated();
          navigate({ to: '/tesoreria/devoluciones/$id', params: { id: res.id } });
        },
        onError: (error) => {
          if (!esApiError(error)) {
            toast.error('No se pudo solicitar la devolución.');
            return;
          }
          const r = applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error, {
            codigos: {
              DEV_IMPORTE_EXCEDE_DEPOSITO: { campo: 'importe' },
              DEV_IMPORTE_INVALIDO: { campo: 'importe' },
              DEV_CLIENTE_DISTINTO_AL_DEPOSITO: { campo: 'clienteId' },
              DEV_CLIENTE_NO_ENCONTRADO: { campo: 'clienteId' },
              CLABE_FORMATO: { campo: 'clabe' },
              CLABE_DIGITO_CONTROL: { campo: 'clabe' },
              DEV_DEPOSITO_CONFIRMADO: 'callout',
            },
          });
          if (r === 'campo' || r === true) return;
          setAviso(error.problem.detail ?? error.problem.title);
        },
      },
    );
  }

  const errores = form.formState.errors;
  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4" noValidate>
      {aviso && (
        <AvisoEnLinea tono="error" titulo="No se pudo solicitar la devolución">
          {aviso}
        </AvisoEnLinea>
      )}
      <Field
        label="Cliente"
        required
        error={errores.clienteId?.message}
        hint={identificado ? 'El depósito ya está identificado con este cliente' : undefined}
      >
        {(control) => (
          <Controller
            control={form.control}
            name="clienteId"
            render={({ field }) => (
              <ClienteTesoreriaSelector
                id={control.id}
                value={field.value || null}
                onChange={(c) => field.onChange(c?.id ?? '')}
                etiquetaInicial={movimiento.beneficiarioNombre ?? undefined}
                disabled={identificado}
              />
            )}
          />
        )}
      </Field>
      <Field
        label={`Importe (${movimiento.moneda})`}
        required
        error={errores.importe?.message}
        hint={`El depósito es de ${movimiento.monto.toLocaleString('es-MX', { minimumFractionDigits: 2 })} ${movimiento.moneda}.`}
      >
        <Input
          type="number"
          step="0.01"
          min="0"
          inputMode="decimal"
          className="tabular-nums"
          {...form.register('importe', { valueAsNumber: true })}
        />
      </Field>
      <Field label="Motivo" required error={errores.motivo?.message}>
        <Textarea rows={3} maxLength={400} placeholder="Ej.: Depósito duplicado, el cliente pidió la devolución." {...form.register('motivo')} />
      </Field>
      <Field label="Banco destino" required error={errores.banco?.message}>
        <Input maxLength={80} {...form.register('banco')} />
      </Field>
      <Field label="CLABE destino" required error={errores.clabe?.message}>
        <Input inputMode="numeric" maxLength={18} className="font-mono" {...form.register('clabe')} />
      </Field>
      <Field label="Titular de la cuenta" required error={errores.titular?.message}>
        <Input maxLength={200} {...form.register('titular')} />
      </Field>

      <SheetFooter className="px-0">
        {faltantes.length > 0 && (
          <p id="solicitar-devolucion-faltantes" className="mr-auto text-xs text-ink-muted">
            Falta capturar {faltantes.join(', ')}.
          </p>
        )}
        <Button type="button" variant="ghost" onClick={onCancel} disabled={solicitar.isPending}>
          Cancelar
        </Button>
        <Button
          type="submit"
          disabled={solicitar.isPending || faltantes.length > 0}
          aria-describedby={faltantes.length > 0 ? 'solicitar-devolucion-faltantes' : undefined}
        >
          {solicitar.isPending ? 'Solicitando…' : 'Solicitar devolución'}
        </Button>
      </SheetFooter>
    </form>
  );
}
