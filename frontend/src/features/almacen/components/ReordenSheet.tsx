import { useEffect } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
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
import { Checkbox } from '@/components/ui/checkbox';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ArticuloSelector, EntidadReordenSelector } from '@/components/erp';
import { applyServerErrors, esApiError } from '@/lib/api';
import {
  CrearReordenSchema,
  type CrearReordenValues,
} from '@/features/almacen/schemas/reorden';
import {
  useCrearReorden,
  useEditarReorden,
} from '@/features/almacen/api/useReorden';
import {
  NivelReorden,
  NivelReordenLabels,
  ObjetivoReposicion,
  ObjetivoReposicionLabels,
  type ConfiguracionReordenListItem,
} from '@/features/almacen/api/types';

/**
 * <c>&lt;ReordenSheet/&gt;</c> — slide-from-right para crear o editar una
 * configuración de reabasto (código = reorden, ADR-0047 PR5.A/5.F). Un solo
 * componente para ambos modos (molde <c>AlmacenSheet</c>).
 *
 * <para>Layout: la <b>llave</b> (artículo · nivel · entidad) arriba —
 * inmutable al editar (la política se edita, la llave no; el backend rechaza
 * cambiarla) — un divisor, y luego la <b>política</b> (objetivo · mín · máx ·
 * punto de reorden · genera requisición automática).</para>
 */
export interface ReordenSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Si viene, modo edición (llave bloqueada); si es null, modo crear. */
  editando: ConfiguracionReordenListItem | null;
}

const NIVEL_OPTIONS: readonly NivelReorden[] = [
  NivelReorden.Sucursal,
  NivelReorden.Almacen,
];

const OBJETIVO_OPTIONS: readonly ObjetivoReposicion[] = [
  ObjetivoReposicion.Minimo,
  ObjetivoReposicion.Maximo,
  ObjetivoReposicion.Reorden,
];

const DEFAULTS: CrearReordenValues = {
  articuloId: '',
  nivel: NivelReorden.Almacen,
  entidadId: '',
  minimo: 0,
  maximo: 0,
  puntoReorden: 0,
  autoRequisicion: false,
  objetivo: ObjetivoReposicion.Maximo,
};

export function ReordenSheet({ open, onOpenChange, editando }: ReordenSheetProps) {
  const crear = useCrearReorden();
  const editar = useEditarReorden();

  const form = useForm<CrearReordenValues>({
    resolver: zodResolver(CrearReordenSchema),
    defaultValues: DEFAULTS,
  });

  useEffect(() => {
    if (open) {
      form.reset(
        editando
          ? {
              articuloId: editando.articuloId,
              nivel: editando.nivel,
              entidadId: editando.entidadId,
              minimo: editando.minimo,
              maximo: editando.maximo,
              puntoReorden: editando.puntoReorden,
              autoRequisicion: editando.autoRequisicion,
              objetivo: editando.objetivo,
            }
          : DEFAULTS,
      );
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, editando?.id]);

  const nivel = useWatch({ control: form.control, name: 'nivel' });

  function onSubmit(values: CrearReordenValues) {
    // Idempotency-Key por submit (no por montaje): si el primer intento falla
    // (p.ej. 422 REORDEN_SIN_ASIGNACION, esperado) el sheet queda abierto; un
    // reintento con la MISMA llave daría 409 (el backend la quema tras un fallo
    // lanzado, ADR-0020). Llave fresca por submit → cada reintento del usuario
    // es un request lógico nuevo. Va en las variables del mutate, nunca dentro
    // del mutationFn. Mismo patrón que LineaInlineForm / AccionesOC.
    const idempotencyKey = crypto.randomUUID();
    if (editando) {
      editar.mutate(
        {
          command: {
            id: editando.id,
            minimo: values.minimo,
            maximo: values.maximo,
            puntoReorden: values.puntoReorden,
            autoRequisicion: values.autoRequisicion,
            objetivo: values.objetivo,
          },
          idempotencyKey,
        },
        {
          onSuccess: () => {
            toast.success('Reabasto actualizado');
            onOpenChange(false);
          },
          onError: manejarError,
        },
      );
    } else {
      crear.mutate(
        { command: values, idempotencyKey },
        {
          onSuccess: () => {
            toast.success('Reabasto configurado');
            onOpenChange(false);
          },
          onError: manejarError,
        },
      );
    }
  }

  function manejarError(error: unknown) {
    if (esApiError(error)) {
      if (error.code === 'REORDEN_SIN_ASIGNACION') {
        toast.error(
          'El artículo no está asignado a ninguna ubicación bajo esa entidad. Asígnalo primero.',
        );
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
        description: error.traceId ? `Código: ${error.traceId}` : undefined,
      });
      return;
    }
    toast.error('Error inesperado al guardar el reabasto.');
  }

  const isPending = crear.isPending || editar.isPending;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>
            {editando ? 'Editar reabasto' : 'Nuevo reabasto'}
          </SheetTitle>
          <SheetDescription>
            {editando
              ? 'La llave (artículo, nivel y entidad) no se puede cambiar; solo la política de reabasto.'
              : 'Define el punto de reabasto de un artículo en una sucursal o almacén.'}
          </SheetDescription>
        </SheetHeader>

        <form
          id="reorden-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="flex-1 space-y-3 overflow-y-auto px-6"
        >
          {/* ── Llave: artículo · nivel · entidad (inmutable al editar) ── */}
          <Field
            label="Artículo"
            required
            error={form.formState.errors.articuloId?.message}
          >
            <Controller
              name="articuloId"
              control={form.control}
              render={({ field }) => (
                <ArticuloSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                  disabled={editando != null}
                  initialLabel={
                    editando
                      ? [editando.articuloClave, editando.articuloDescripcion]
                          .filter(Boolean)
                          .join(' · ') || undefined
                      : undefined
                  }
                />
              )}
            />
          </Field>

          <Field
            label="Nivel"
            required
            error={form.formState.errors.nivel?.message}
          >
            <Controller
              name="nivel"
              control={form.control}
              render={({ field }) => (
                <Select
                  value={String(field.value)}
                  onValueChange={(v) => field.onChange(Number(v))}
                  disabled={editando != null}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="Seleccionar nivel" />
                  </SelectTrigger>
                  <SelectContent>
                    {NIVEL_OPTIONS.map((n) => (
                      <SelectItem key={n} value={String(n)}>
                        {NivelReordenLabels[n]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </Field>

          <Field
            label={nivel === NivelReorden.Sucursal ? 'Sucursal' : 'Almacén'}
            required
            error={form.formState.errors.entidadId?.message}
          >
            <Controller
              name="entidadId"
              control={form.control}
              render={({ field }) => (
                <EntidadReordenSelector
                  nivel={nivel}
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                  disabled={editando != null}
                />
              )}
            />
          </Field>

          <div className="border-t pt-3" />

          {/* ── Política: objetivo · mín/máx/punto-reorden · auto-RQ ── */}
          <Field
            label="Objetivo"
            required
            error={form.formState.errors.objetivo?.message}
          >
            <Controller
              name="objetivo"
              control={form.control}
              render={({ field }) => (
                <Select
                  value={String(field.value)}
                  onValueChange={(v) => field.onChange(Number(v))}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="Seleccionar objetivo" />
                  </SelectTrigger>
                  <SelectContent>
                    {OBJETIVO_OPTIONS.map((o) => (
                      <SelectItem key={o} value={String(o)}>
                        {ObjetivoReposicionLabels[o]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </Field>

          <div className="grid grid-cols-3 gap-2">
            <Field
              label="Mínimo"
              required
              error={form.formState.errors.minimo?.message}
            >
              <NumberField name="minimo" control={form.control} />
            </Field>
            <Field
              label="Máximo"
              required
              error={form.formState.errors.maximo?.message}
            >
              <NumberField name="maximo" control={form.control} />
            </Field>
            <Field
              label="Punto reorden"
              required
              error={form.formState.errors.puntoReorden?.message}
            >
              <NumberField name="puntoReorden" control={form.control} />
            </Field>
          </div>

          <Controller
            name="autoRequisicion"
            control={form.control}
            render={({ field }) => (
              <label className="flex items-center gap-2 text-sm">
                <Checkbox
                  checked={field.value}
                  onCheckedChange={(v) => field.onChange(v === true)}
                />
                Genera requisición automática al caer por debajo del objetivo
              </label>
            )}
          />
        </form>

        <SheetFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={isPending}
          >
            Cancelar
          </Button>
          <Button type="submit" form="reorden-form" disabled={isPending}>
            {isPending
              ? 'Guardando…'
              : editando
                ? 'Guardar cambios'
                : 'Configurar reabasto'}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}

/** Campo numérico decimal &gt;= 0 (mín/máx/punto-reorden). */
function NumberField({
  name,
  control,
}: {
  name: 'minimo' | 'maximo' | 'puntoReorden';
  control: ReturnType<typeof useForm<CrearReordenValues>>['control'];
}) {
  return (
    <Controller
      name={name}
      control={control}
      render={({ field }) => (
        <Input
          type="number"
          inputMode="decimal"
          min={0}
          step="any"
          value={Number.isNaN(field.value) ? '' : field.value}
          onChange={(e) => field.onChange(e.target.valueAsNumber)}
          onBlur={field.onBlur}
        />
      )}
    />
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
