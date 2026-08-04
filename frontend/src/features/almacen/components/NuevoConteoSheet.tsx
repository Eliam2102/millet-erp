import { hoyLocalISO } from '@/lib/datetime';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import { toast } from 'sonner';
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { UsuarioSelector } from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  CrearConteoSchema,
  type CrearConteoValues,
} from '@/features/almacen/schemas/conteo';
import { useCrearConteo, useSubAlmacenes } from '@/features/almacen/api';
import {
  TipoConteo,
  TipoConteoLabels,
} from '@/features/almacen/api/types';
import { Field } from '@/features/almacen/components/internal/Field';

export interface NuevoConteoSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * <c>&lt;NuevoConteoSheet/&gt;</c> — slide-from-right para planificar
 * un conteo (FE-F5-PR1). Tipo Rotativo o Anual, fecha planificada,
 * responsable, sub-almacén opcional y filtro de familia opcional.
 *
 * <para>El snapshot se toma al pasar a EnCurso (acción separada
 * "Iniciar"), no al crear.</para>
 */
export function NuevoConteoSheet({
  open,
  onOpenChange,
}: NuevoConteoSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Nuevo conteo</SheetTitle>
          <SheetDescription>
            Planifica un conteo rotativo o anual. El snapshot del
            inventario teórico se toma al pasar a "En curso" (acción
            separada).
          </SheetDescription>
        </SheetHeader>

        {open && (
          <FormBody
            onSuccess={() => onOpenChange(false)}
            onCancel={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function FormBody({
  onSuccess,
  onCancel,
}: {
  onSuccess: () => void;
  onCancel: () => void;
}) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearConteo();
  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });

  const hoyIso = hoyLocalISO();

  const form = useForm<CrearConteoValues>({
    resolver: zodResolver(CrearConteoSchema),
    defaultValues: {
      tipo: TipoConteo.Rotativo,
      fechaPlanificada: hoyIso,
      responsableId: '',
      subAlmacenId: null,
      filtroFamilia: null,
    },
  });

  function onSubmit(values: CrearConteoValues) {
    crear.mutate(
      {
        command: {
          tipo: values.tipo,
          fechaPlanificada: values.fechaPlanificada,
          responsableId: values.responsableId,
          subAlmacenId: values.subAlmacenId ?? null,
          filtroFamilia: values.filtroFamilia ?? null,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success('Conteo planificado', {
            description: 'Pasa a "En curso" para tomar el snapshot.',
          });
          onSuccess();
          navigate({
            to: '/almacen/inventarios/$id',
            params: { id: resp.conteoId },
          });
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
              description: error.traceId ? `Código: ${error.traceId}` : undefined,
            });
            return;
          }
          toast.error('Error inesperado al planificar el conteo.');
        },
      },
    );
  }

  return (
    <>
      <form
        id="nuevo-conteo-form"
        onSubmit={form.handleSubmit(onSubmit)}
        noValidate
        className="flex-1 space-y-3 overflow-y-auto px-6"
      >
        <Field
          label="Tipo de conteo"
          required
          error={form.formState.errors.tipo?.message}
        >
          <Controller
            name="tipo"
            control={form.control}
            render={({ field }) => (
              <Select
                value={String(field.value)}
                onValueChange={(v) => field.onChange(Number(v))}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={String(TipoConteo.Rotativo)}>
                    {TipoConteoLabels[TipoConteo.Rotativo]}
                  </SelectItem>
                  <SelectItem value={String(TipoConteo.Anual)}>
                    {TipoConteoLabels[TipoConteo.Anual]} (bloquea salidas)
                  </SelectItem>
                </SelectContent>
              </Select>
            )}
          />
        </Field>

        <Field
          label="Fecha planificada"
          required
          error={form.formState.errors.fechaPlanificada?.message}
        >
          <Controller
            name="fechaPlanificada"
            control={form.control}
            render={({ field }) => (
              <Input
                {...field}
                type="date"
                value={field.value ?? ''}
              />
            )}
          />
        </Field>

        <Field
          label="Responsable (contador)"
          required
          error={form.formState.errors.responsableId?.message}
        >
          <Controller
            name="responsableId"
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
          label="Sub-almacén (opcional)"
          error={form.formState.errors.subAlmacenId?.message}
        >
          <Controller
            name="subAlmacenId"
            control={form.control}
            render={({ field }) => (
              <Select
                value={field.value ?? ''}
                onValueChange={(v) => field.onChange(v || null)}
                disabled={subAlmacenesQuery.isLoading}
              >
                <SelectTrigger>
                  <SelectValue placeholder="Todos" />
                </SelectTrigger>
                <SelectContent>
                  {(subAlmacenesQuery.data?.items ?? []).map((s) => (
                    <SelectItem key={s.id} value={s.id}>
                      {s.clave} · {s.nombre}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
        </Field>

        <Field
          label="Filtro de familia (opcional)"
          error={form.formState.errors.filtroFamilia?.message}
        >
          <Controller
            name="filtroFamilia"
            control={form.control}
            render={({ field }) => (
              <Input
                {...field}
                value={field.value ?? ''}
                maxLength={50}
                placeholder="Ej. SELLANTES, PINTURAS"
              />
            )}
          />
        </Field>
      </form>

      <SheetFooter>
        <Button
          type="button"
          variant="ghost"
          onClick={onCancel}
          disabled={crear.isPending}
        >
          Cancelar
        </Button>
        <Button
          type="submit"
          form="nuevo-conteo-form"
          disabled={crear.isPending}
        >
          {crear.isPending ? 'Planificando…' : 'Planificar conteo'}
        </Button>
      </SheetFooter>
    </>
  );
}
