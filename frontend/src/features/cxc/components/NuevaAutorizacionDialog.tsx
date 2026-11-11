import { FormGrid, FormCampo } from '@/components/erp';
import { useEffect } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import {
  Dialog,
  DialogContent,
  DialogBody,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  useBeneficiariosCredito,
  useConfiguracionExcepcionCredito,
  useCrearAutorizacionCredito,
} from '@/features/cxc/api/useLiberaciones';
import { CondicionExcepcionCredito, EstadoAutorizacionCredito } from '@/features/cxc/api/types';
import { ETIQUETA_CONDICION_EXCEPCION } from '@/features/cxc/lib/glosario';
import {
  NuevaAutorizacionSchema,
  type NuevaAutorizacionValues,
} from '@/features/cxc/schemas/liberacion';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * <c>&lt;NuevaAutorizacionDialog/&gt;</c> — el gerente
 * (<c>liberacion.override</c>) crea una autorización consumible para un
 * beneficiario (§4.3, calco de la UX de apertura de caja): folio del
 * pedido, condición, monto y vigencia. Un solo uso y para un solo pedido
 * (C7.3 · E10: el folio debe coincidir exacto al decidir); nunca
 * autoconsumo — el usuario actual no aparece como beneficiario.
 */
export function NuevaAutorizacionDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>Nueva autorización de crédito</DialogTitle>
          <DialogDescription>
            Autoriza a un usuario a liberar un pedido pese al crédito. Sirve para un solo pedido (el
            folio debe coincidir exacto), hasta el monto que indiques, con vigencia limitada y un
            solo uso. Arriba del umbral de firmas queda pendiente de la segunda firma de Dirección.
          </DialogDescription>
        </DialogHeader>
        {open && <FormAutorizacion onClose={() => onOpenChange(false)} />}
      </DialogContent>
    </Dialog>
  );
}

function FormAutorizacion({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearAutorizacionCredito();
  const usuarioActualId = useAuthStore((s) => s.user?.id ?? null);
  const usuarios = useBeneficiariosCredito();
  const config = useConfiguracionExcepcionCredito();

  const candidatos = (usuarios.data ?? []).filter((u) => u.id !== usuarioActualId);

  const form = useForm<NuevaAutorizacionValues>({
    resolver: zodResolver(NuevaAutorizacionSchema),
    defaultValues: {
      beneficiarioUsuarioId: '',
      motivo: '',
      clienteOPedidoRef: '',
      condicion: CondicionExcepcionCredito.CoberturaParcial,
      montoAutorizado: undefined as unknown as number,
      moneda: 'MXN',
      vigenciaHoras: 24,
    },
  });

  // M-239: la vigencia por omisión sale de la configuración (no son 24 h fijas).
  const vigenciaPorOmision = config.data?.vigenciaPorOmisionHoras;
  useEffect(() => {
    if (vigenciaPorOmision) form.setValue('vigenciaHoras', vigenciaPorOmision);
  }, [vigenciaPorOmision, form]);

  const monto = useWatch({ control: form.control, name: 'montoAutorizado' });
  const moneda = useWatch({ control: form.control, name: 'moneda' });
  const umbral = Math.min(...(config.data?.reglas.filter((r) => r.firmasRequeridas === 2).map((r) => r.umbralMxn) ?? [Infinity]));
  const pideSegundaFirma =
    moneda === 'MXN' && Number.isFinite(umbral) && Number(monto) > umbral;

  function onSubmit(values: NuevaAutorizacionValues) {
    crear.mutate(
      {
        command: { ...values, condicion: values.condicion as CondicionExcepcionCredito },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(
            res.estado === EstadoAutorizacionCredito.PendienteSegundaFirma
              ? `Solicitud registrada para ${res.clienteOPedidoRef}: falta la segunda firma de Dirección antes de ${new Date(res.vigenteHasta).toLocaleString('es-MX')}.`
              : `Autorización creada para ${res.clienteOPedidoRef}; vence ${new Date(res.vigenteHasta).toLocaleString('es-MX')}.`,
          );
          onClose();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: error.problem.detail,
            });
            return;
          }
          toast.error('Error inesperado al crear la autorización.');
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="flex min-h-0 flex-1 flex-col"
    >
      <DialogBody>
        <FormGrid>
          <FormCampo span={12} className="space-y-1">
            <Label htmlFor="beneficiario" className="text-xs">
              Beneficiario <span className="text-danger-fg">*</span>
            </Label>
            <select
              id="beneficiario"
              className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-card-flat"
              {...form.register('beneficiarioUsuarioId')}
            >
              <option value="">
                {usuarios.isLoading ? 'Cargando usuarios…' : 'Selecciona un usuario'}
              </option>
              {candidatos.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.nombre} ({u.email})
                </option>
              ))}
            </select>
            {usuarios.isError && (
              <p className="text-xs text-danger-fg">
                No se pudo cargar la lista de beneficiarios con permiso de Crédito y Cobranza.
              </p>
            )}
            {form.formState.errors.beneficiarioUsuarioId && (
              <p className="text-xs text-danger-fg">
                {form.formState.errors.beneficiarioUsuarioId.message}
              </p>
            )}
          </FormCampo>

          <FormCampo span={12} className="space-y-1">
            <Label htmlFor="clienteOPedidoRef" className="text-xs">
              Folio del pedido <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="clienteOPedidoRef"
              placeholder="p. ej. 6100901"
              maxLength={80}
              {...form.register('clienteOPedidoRef')}
            />
            <p className="text-xs text-ink-muted">
              La autorización solo sirve para este pedido: el folio debe coincidir exacto al
              decidir.
            </p>
            {form.formState.errors.clienteOPedidoRef && (
              <p className="text-xs text-danger-fg">
                {form.formState.errors.clienteOPedidoRef.message}
              </p>
            )}
          </FormCampo>

          <FormCampo span={12} className="space-y-1">
            <Label htmlFor="motivoAut" className="text-xs">
              Motivo <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="motivoAut"
              placeholder="p. ej. Cliente con pago en tránsito confirmado"
              maxLength={254}
              {...form.register('motivo')}
            />
            {form.formState.errors.motivo && (
              <p className="text-xs text-danger-fg">{form.formState.errors.motivo.message}</p>
            )}
          </FormCampo>

          <FormCampo span={12} className="space-y-1">
            <Label htmlFor="condicion" className="text-xs">
              Condición excepcional <span className="text-danger-fg">*</span>
            </Label>
            <select
              id="condicion"
              className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-card-flat"
              {...form.register('condicion', { valueAsNumber: true })}
            >
              {Object.values(CondicionExcepcionCredito).map((c) => (
                <option key={c} value={c}>
                  {ETIQUETA_CONDICION_EXCEPCION[c]}
                </option>
              ))}
            </select>
          </FormCampo>

          <FormCampo span={6} className="space-y-1">
            <Label htmlFor="montoAutorizado" className="text-xs">
              Monto que cubre la excepción <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="montoAutorizado"
              type="number"
              step="0.01"
              min="0.01"
              placeholder="Exceso sobre el crédito, o importe del pedido"
              {...form.register('montoAutorizado', { valueAsNumber: true })}
            />
            <p className="text-xs text-ink-muted">
              Limita lo que se libera: un pedido que lo supere no se puede liberar con esta
              autorización.
            </p>
            {form.formState.errors.montoAutorizado && (
              <p className="text-xs text-danger-fg">
                {form.formState.errors.montoAutorizado.message}
              </p>
            )}
            {pideSegundaFirma && (
              <p role="status" className="text-xs text-warning-fg">
                Este monto pide segunda firma de Dirección: la autorización queda pendiente hasta
                que otra persona de Dirección la firme.
              </p>
            )}
          </FormCampo>

          <FormCampo span={6} className="space-y-1">
            <Label htmlFor="monedaAut" className="text-xs">
              Moneda
            </Label>
            <select
              id="monedaAut"
              className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-card-flat"
              {...form.register('moneda')}
            >
              <option value="MXN">MXN</option>
              <option value="USD">USD</option>
            </select>
          </FormCampo>

          <FormCampo span={12} className="space-y-1">
            <Label htmlFor="vigenciaHoras" className="text-xs">
              Vigencia (horas
              {config.data ? `, máx. ${config.data.vigenciaMaximaHoras}` : ''}){' '}
              <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="vigenciaHoras"
              type="number"
              step="1"
              min="1"
              max={config.data?.vigenciaMaximaHoras}
              className="w-32"
              {...form.register('vigenciaHoras', { valueAsNumber: true })}
            />
            {form.formState.errors.vigenciaHoras && (
              <p className="text-xs text-danger-fg">
                {form.formState.errors.vigenciaHoras.message}
              </p>
            )}
          </FormCampo>
        </FormGrid>
      </DialogBody>
      <DialogFooter>
        <Button type="button" variant="ghost" onClick={onClose} disabled={crear.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear autorización'}
        </Button>
      </DialogFooter>
    </form>
  );
}
