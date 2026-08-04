import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  DepartamentoSelector,
  TextAreaField,
  UsuarioSelector,
} from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  DesignarAprobadorSchema,
  type DesignarAprobadorValues,
} from '@/features/compras/schemas/designar-aprobador';
import { useDesignarAprobador } from '@/features/compras/api/useAprobadores';
import {
  ROL_APROBADOR_LABEL,
  RolAprobador,
} from '@/features/compras/api/types';

/**
 * <c>&lt;DesignarAprobadorDialog/&gt;</c> — modal para designar un
 * aprobador en un (depto, rol). Doc 05 §11.4.
 *
 * <para>Re-designar al mismo usuario en el mismo (depto, rol) es
 * idempotente: el backend devuelve la vigencia actual sin crear una
 * nueva. Si se designa a un usuario distinto, el backend cierra la
 * vigencia actual automáticamente y abre una nueva (ver Application
 * handler).</para>
 *
 * <para>Errores manejados:</para>
 * <list>
 *   <item><b>404 USUARIO_NO_ENCONTRADO</b>: inline error en el
 *   <c>&lt;UsuarioSelector/&gt;</c> (el usuario fue desactivado en
 *   Identidad mientras estaba abierto el dialog).</item>
 *   <item><b>422 con errores[]</b>: <c>applyServerErrors</c> mapea a
 *   los campos del form.</item>
 *   <item><b>otros</b>: toast genérico con traceId.</item>
 * </list>
 */
export interface DesignarAprobadorDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Callback opcional al éxito (ej. cerrar otros panels). */
  onSuccess?: () => void;
}

export function DesignarAprobadorDialog({
  open,
  onOpenChange,
  onSuccess,
}: DesignarAprobadorDialogProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const designar = useDesignarAprobador();

  const form = useForm<DesignarAprobadorValues>({
    resolver: zodResolver(DesignarAprobadorSchema),
    defaultValues: {
      departamentoId: '',
      rol: RolAprobador.JefeDpto,
      usuarioId: '',
      motivo: null,
    },
  });

  // Reset al abrir/cerrar (mismo patrón que ModalMotivo).
  useEffect(() => {
    if (!open) {
      form.reset({
        departamentoId: '',
        rol: RolAprobador.JefeDpto,
        usuarioId: '',
        motivo: null,
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  function onSubmit(values: DesignarAprobadorValues) {
    designar.mutate(
      { values, idempotencyKey },
      {
        onSuccess: (resp) => {
          toast.success('Aprobador designado', {
            description: `Vigencia abierta el ${new Date(resp.vigenteDesde).toLocaleDateString()}.`,
          });
          onOpenChange(false);
          onSuccess?.();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'USUARIO_NO_ENCONTRADO') {
              form.setError('usuarioId', {
                type: error.code,
                message: 'Usuario no encontrado o inactivo en Identidad.',
              });
              return;
            }
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
            return;
          }
          toast.error('Error inesperado al designar aprobador.');
        },
      },
    );
  }

  const isPending = designar.isPending;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Designar aprobador</DialogTitle>
          <DialogDescription>
            Asigna un usuario a un rol de aprobación dentro de un
            departamento. La vigencia abre desde ahora; si ya hay otro
            usuario en el mismo (depto, rol), su vigencia se cierra
            automáticamente.
          </DialogDescription>
        </DialogHeader>

        <form
          id="designar-aprobador-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="space-y-3"
        >
          <Field
            label="Departamento"
            required
            error={form.formState.errors.departamentoId?.message}
          >
            <Controller
              name="departamentoId"
              control={form.control}
              render={({ field }) => (
                <DepartamentoSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                />
              )}
            />
          </Field>

          <Field
            label="Rol"
            required
            error={form.formState.errors.rol?.message}
          >
            <Controller
              name="rol"
              control={form.control}
              render={({ field }) => (
                <Select
                  value={String(field.value)}
                  onValueChange={(v) => field.onChange(Number(v))}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="Seleccionar rol" />
                  </SelectTrigger>
                  <SelectContent>
                    {(
                      [
                        RolAprobador.JefeDpto,
                        RolAprobador.JefeAlmacen,
                        RolAprobador.AutorizadorN2,
                      ] as const
                    ).map((r) => (
                      <SelectItem key={r} value={String(r)}>
                        {ROL_APROBADOR_LABEL[r]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </Field>

          <Field
            label="Usuario"
            required
            error={form.formState.errors.usuarioId?.message}
          >
            <Controller
              name="usuarioId"
              control={form.control}
              render={({ field }) => (
                <UsuarioSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                />
              )}
            />
          </Field>

          <Field
            label="Motivo (opcional)"
            error={form.formState.errors.motivo?.message}
          >
            <Controller
              name="motivo"
              control={form.control}
              render={({ field }) => (
                <TextAreaField
                  value={field.value ?? null}
                  onChange={(v) => field.onChange(v)}
                  maxLength={500}
                  minRows={2}
                />
              )}
            />
          </Field>
        </form>

        <DialogFooter className="gap-2">
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={isPending}
          >
            Cancelar
          </Button>
          <Button
            type="submit"
            form="designar-aprobador-form"
            disabled={isPending}
          >
            {isPending ? 'Guardando…' : 'Designar'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function Field({
  label,
  required,
  error,
  children,
}: {
  label: string;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <label className="flex items-center gap-1 text-sm font-medium">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
