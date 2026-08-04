import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
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
import { Label } from '@/components/ui/label';
import { useUsuarios } from '@/features/catalogos/api';
import { useCrearAutorizacionCredito } from '@/features/cxc/api/useLiberaciones';
import {
  NuevaAutorizacionSchema,
  type NuevaAutorizacionValues,
} from '@/features/cxc/schemas/liberacion';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * <c>&lt;NuevaAutorizacionDialog/&gt;</c> — el gerente
 * (<c>liberacion.override</c>) crea una autorización consumible para un
 * beneficiario (§4.3, calco de la UX de apertura de caja): cliente o
 * pedido, motivo y vigencia ≤ 24 h. Un solo uso; nunca autoconsumo — el
 * usuario actual no aparece como beneficiario.
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
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Nueva autorización de crédito</DialogTitle>
          <DialogDescription>
            Autoriza a un usuario a liberar un pedido o cliente pese al
            crédito. Vigencia máxima 24 horas, un solo uso; puedes
            cancelarla mientras siga vigente.
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
  const usuarios = useUsuarios();

  const candidatos = (usuarios.data?.items ?? []).filter(
    (u) => u.activo && u.id !== usuarioActualId,
  );

  const form = useForm<NuevaAutorizacionValues>({
    resolver: zodResolver(NuevaAutorizacionSchema),
    defaultValues: {
      beneficiarioUsuarioId: '',
      motivo: '',
      clienteOPedidoRef: '',
      vigenciaHoras: 24,
    },
  });

  function onSubmit(values: NuevaAutorizacionValues) {
    crear.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: (res) => {
          toast.success(
            `Autorización creada para ${res.clienteOPedidoRef}; vence ${new Date(res.vigenteHasta).toLocaleString('es-MX')}.`,
          );
          onClose();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description:
                error.problem.detail ??
                (error.traceId ? `Código: ${error.traceId}` : undefined),
            });
            return;
          }
          toast.error('Error inesperado al crear la autorización.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="space-y-3">
      <div className="space-y-1">
        <Label htmlFor="beneficiario" className="text-xs">
          Beneficiario <span className="text-destructive">*</span>
        </Label>
        <select
          id="beneficiario"
          className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
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
          <p className="text-xs text-destructive">
            No se pudo cargar el catálogo de usuarios (se requiere
            identidad.usuarios.leer).
          </p>
        )}
        {form.formState.errors.beneficiarioUsuarioId && (
          <p className="text-xs text-destructive">
            {form.formState.errors.beneficiarioUsuarioId.message}
          </p>
        )}
      </div>

      <div className="space-y-1">
        <Label htmlFor="clienteOPedidoRef" className="text-xs">
          Cliente o folio de pedido <span className="text-destructive">*</span>
        </Label>
        <Input
          id="clienteOPedidoRef"
          placeholder="p. ej. 3000123456 o ACME SA"
          maxLength={80}
          {...form.register('clienteOPedidoRef')}
        />
        {form.formState.errors.clienteOPedidoRef && (
          <p className="text-xs text-destructive">
            {form.formState.errors.clienteOPedidoRef.message}
          </p>
        )}
      </div>

      <div className="space-y-1">
        <Label htmlFor="motivoAut" className="text-xs">
          Motivo <span className="text-destructive">*</span>
        </Label>
        <Input
          id="motivoAut"
          placeholder="p. ej. Cliente con pago en tránsito confirmado"
          maxLength={254}
          {...form.register('motivo')}
        />
        {form.formState.errors.motivo && (
          <p className="text-xs text-destructive">
            {form.formState.errors.motivo.message}
          </p>
        )}
      </div>

      <div className="space-y-1">
        <Label htmlFor="vigenciaHoras" className="text-xs">
          Vigencia (horas, máx. 24) <span className="text-destructive">*</span>
        </Label>
        <Input
          id="vigenciaHoras"
          type="number"
          step="1"
          min="1"
          max="24"
          className="w-32"
          {...form.register('vigenciaHoras', { valueAsNumber: true })}
        />
        {form.formState.errors.vigenciaHoras && (
          <p className="text-xs text-destructive">
            {form.formState.errors.vigenciaHoras.message}
          </p>
        )}
      </div>

      <DialogFooter>
        <Button
          type="button"
          variant="ghost"
          onClick={onClose}
          disabled={crear.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear autorización'}
        </Button>
      </DialogFooter>
    </form>
  );
}
