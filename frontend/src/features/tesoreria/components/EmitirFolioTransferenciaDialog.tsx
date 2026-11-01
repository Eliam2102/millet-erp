import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { useEffect, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { z } from 'zod';
import {
  Sheet,
  SheetBody,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { AvisoEnLinea, Field } from '@/components/erp';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { applyServerErrors, esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useEmitirFolioTransferencia } from '@/features/tesoreria/api/useTesoreria';
import { BeneficiarioTipo } from '@/features/tesoreria/api/types';
import { ClienteTesoreriaSelector } from './ClienteTesoreriaSelector';

const Schema = z.object({
  clienteId: z.string().min(1, 'Selecciona el cliente que pagó.'),
  importe: z.number('Captura el importe.').positive('El importe debe ser mayor a cero.'),
  pedido: z.string().trim().max(60).optional(),
});
type Values = z.infer<typeof Schema>;

export interface EmitirFolioMovimiento {
  id: string;
  monto: number;
  moneda: string;
  beneficiarioTipo: BeneficiarioTipo | null;
  beneficiarioRef: string | null;
  beneficiarioNombre?: string | null;
}

/**
 * F6.6 (R16 · Manual §2.1): Ingresos valida el abono en el banco y emite el
 * folio de confirmación desde el ingreso ya registrado. Caja solo aplica el
 * cobro por transferencia con ese folio, del mismo cliente y por el importe
 * exacto. Un ingreso admite varios folios hasta su total (pagos parciales).
 */
export function EmitirFolioTransferenciaDialog({
  movimiento,
  abierto,
  onCerrar,
}: {
  movimiento: EmitirFolioMovimiento;
  abierto: boolean;
  onCerrar: () => void;
}) {
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(onCerrar);
  return (
    <Sheet open={abierto} onOpenChange={(next) => (next ? undefined : solicitarCierre())}>
      <SheetContent side="right" size="lg">
        <SheetHeader>
          <SheetTitle>Emitir folio de transferencia</SheetTitle>
          <SheetDescription>
            Confirma que el abono llegó al banco. Caja aplicará el pago por transferencia solo con
            este folio, del mismo cliente y por el mismo importe.
          </SheetDescription>
        </SheetHeader>
        {abierto && (
          <Formulario
            movimiento={movimiento}
            onCancel={() => solicitarCierre()}
            onEmitido={() => solicitarCierre(true)}
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
  onEmitido,
  onDirtyChange,
}: {
  movimiento: EmitirFolioMovimiento;
  onCancel: () => void;
  onEmitido: () => void;
  onDirtyChange: (dirty: boolean) => void;
}) {
  const keyFor = useBodyScopedIdempotencyKey();
  const emitir = useEmitirFolioTransferencia();
  const [aviso, setAviso] = useState<string | null>(null);
  const identificado =
    movimiento.beneficiarioTipo === BeneficiarioTipo.Cliente && movimiento.beneficiarioRef != null;

  const form = useForm<Values>({
    resolver: zodResolver(Schema),
    defaultValues: {
      clienteId: identificado ? movimiento.beneficiarioRef! : '',
      importe: movimiento.monto,
      pedido: '',
    },
  });
  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: Values) {
    setAviso(null);
    const body = {
      movimientoId: movimiento.id,
      clienteId: values.clienteId,
      importe: values.importe,
      pedido: values.pedido?.trim() ? values.pedido.trim() : null,
    };
    emitir.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: (folio) => {
          toast.success(`Folio ${folio.folio} emitido`, {
            description: 'Compártelo con el vendedor: Caja lo pide al cobrar la transferencia.',
          });
          onEmitido();
        },
        onError: (error) => {
          if (!esApiError(error)) {
            toast.error('No se pudo emitir el folio.');
            return;
          }
          const r = applyServerErrors(
            form as unknown as Parameters<typeof applyServerErrors>[0],
            error,
            {
              codigos: {
                FOLIO_TR_EXCEDE_INGRESO: { campo: 'importe' },
                FOLIO_TR_IMPORTE_INVALIDO: { campo: 'importe' },
                FOLIO_TR_CLIENTE_DISTINTO: { campo: 'clienteId' },
                FOLIO_TR_CLIENTE_NO_ENCONTRADO: { campo: 'clienteId' },
                FOLIO_TR_MONEDA: 'callout',
                FOLIO_TR_NO_CLIENTE: 'callout',
                FOLIO_TR_INGRESO_REVERTIDO: 'callout',
              },
            },
          );
          if (r === 'campo' || r === true) return;
          setAviso(error.problem.detail ?? error.problem.title);
        },
      },
    );
  }

  const errores = form.formState.errors;
  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="flex min-h-0 flex-1 flex-col" noValidate>
      <SheetBody className="space-y-4">
        {aviso && (
          <AvisoEnLinea tono="error" titulo="No se pudo emitir el folio">
            {aviso}
          </AvisoEnLinea>
        )}
        <FormSeccion numero={1} titulo="Transferencia confirmada">
          <FormCampo span={6}>
            <Field
              label="Cliente"
              required
              error={errores.clienteId?.message}
              hint={identificado ? 'El ingreso ya está identificado con este cliente' : undefined}
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
          </FormCampo>
          <FormCampo span={6}>
            <Field
              label={`Importe (${movimiento.moneda})`}
              required
              error={errores.importe?.message}
              hint="Por el importe exacto que cobrará Caja. Para pagos parciales emite varios folios."
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
          </FormCampo>
          <FormCampo span={6}>
            <Field label="Pedido" error={errores.pedido?.message} hint="Opcional · el que indicó el vendedor">
              <Input maxLength={60} {...form.register('pedido')} />
            </Field>
          </FormCampo>
        </FormSeccion>
      </SheetBody>
      <SheetFooter>
        <Button type="button" variant="ghost" onClick={onCancel}>
          Cancelar
        </Button>
        <Button type="submit" disabled={emitir.isPending}>
          Emitir folio
        </Button>
      </SheetFooter>
    </form>
  );
}
