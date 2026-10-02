import { useEffect, useRef, useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { Label } from '@/components/ui/label';
import { cn } from '@/lib/utils';
import { useCrearCuenta, useEditarCuenta, useSiguienteCodigo } from '../api/hooks';
import type { Cuenta } from '../api/types';
import { aBody, CuentaSchema, VALORES_VACIOS, type CuentaValues } from '../schemas/cuenta';
import { manejarErrorCuenta } from '../lib/errores';
import { SELECT_CLASS } from '../lib/estilos';
import { AvisoNota } from './AvisoNota';
import { CuentaPadreSelector } from './CuentaPadreSelector';

/** Label (shared) + control + error inline, receta DESIGN 4.2. */
function Field({ label, htmlFor, required, opcional, error, full, children }: {
  label: string; htmlFor: string; required?: boolean; opcional?: boolean; error?: string; full?: boolean; children: React.ReactNode;
}) {
  return (
    <div className={cn('flex flex-col gap-1.5', full && 'md:col-span-2')}>
      <Label htmlFor={htmlFor}>
        {label}
        {required && <span aria-hidden="true" className="text-danger-fg">*</span>}
        {opcional && <span className="font-normal text-ink-muted">(opcional)</span>}
      </Label>
      {children}
      {error != null && <p role="alert" className="text-xs text-danger-fg">{error}</p>}
    </div>
  );
}

const SELECT = `w-full ${SELECT_CLASS}`;

export const MOTIVO_BLOQUEO =
  'La cuenta (o una de sus hijas) ya tiene movimientos: cambiar su padre, naturaleza o tipo alteraría la interpretación de saldos históricos. ' +
  'Para reclasificar, crea una cuenta nueva y desactiva esta (procedimiento de impacto aprobado por Contabilidad).';

function valoresDe(c: Cuenta): CuentaValues {
  return {
    codigo: c.codigo,
    nombre: c.nombre,
    padreId: c.padreId ?? '',
    naturaleza: c.naturaleza ?? '',
    tipo: c.tipo ?? '',
    cuentaControl: c.cuentaControl,
    codigoAgrupador: c.codigoAgrupador ?? '',
    grupoReporte: c.grupoReporte ?? '',
  };
}

interface Props {
  /** Presente = edición inline (If-Match); ausente = alta. */
  cuenta?: Cuenta;
  /** Padre actual (para mostrarlo en el selector aunque no esté en la página de resultados). */
  padreActual?: Pick<Cuenta, 'id' | 'codigo' | 'nombre'> | null;
  onGuardada: () => void;
  onCancelar: () => void;
  onDirtyChange?: (dirty: boolean) => void;
}

/** Formulario de cuenta (alta en Sheet, edición inline). Conserva los datos ante 422/409/fallos. */
export function CuentaForm({ cuenta, padreActual, onGuardada, onCancelar, onDirtyChange }: Props) {
  const editando = cuenta !== undefined;
  const bloqueado = editando && cuenta.usada === true;
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();
  const crear = useCrearCuenta();
  const editar = useEditarCuenta();
  const [mensaje, setMensaje] = useState<string | null>(null);

  const form = useForm<CuentaValues>({
    resolver: zodResolver(CuentaSchema),
    defaultValues: cuenta ? valoresDe(cuenta) : VALORES_VACIOS,
  });
  const { isDirty, errors } = form.formState;
  useEffect(() => {
    onDirtyChange?.(isDirty);
  }, [isDirty, onDirtyChange]);

  // Opción 2: al elegir el padre (solo en alta) se propone el siguiente código libre de su rama. Es editable:
  // solo se rellena si el campo está vacío o conserva la sugerencia anterior (nunca pisa lo que escribió el usuario).
  const padreId = useWatch({ control: form.control, name: 'padreId' });
  const codigoActual = useWatch({ control: form.control, name: 'codigo' });
  const sugerencia = useSiguienteCodigo(padreId || null, !editando);
  const ultimaSugerencia = useRef<string | null>(null);
  const codigoSugerido = sugerencia.data?.codigo ?? null;
  useEffect(() => {
    if (editando || !codigoSugerido) return;
    const actual = form.getValues('codigo').trim();
    if (actual === '' || actual === ultimaSugerencia.current) {
      form.setValue('codigo', codigoSugerido, { shouldDirty: true });
      ultimaSugerencia.current = codigoSugerido;
    }
  }, [editando, codigoSugerido, form]);
  const ayudaCodigo = editando
    ? 'El código no se modifica.'
    : !padreId
      ? 'Elige primero la cuenta padre para sugerir el código (una cuenta raíz se escribe a mano).'
      : sugerencia.isFetching
        ? 'Calculando el código sugerido…'
        : sugerencia.data?.motivo
          ? sugerencia.data.motivo
          : codigoSugerido && codigoActual.trim() === codigoSugerido
            ? 'Sugerido según la cuenta padre; puedes cambiarlo.'
            : null;

  const guardando = crear.isPending || editar.isPending;

  function onSubmit(values: CuentaValues) {
    setMensaje(null);
    const opts = {
      // Toast SOLO tras 2xx: nada optimista.
      onSuccess: () => {
        toast.success(editando ? 'Cuenta actualizada' : 'Cuenta creada');
        onGuardada();
      },
      onError: (error: unknown) =>
        manejarErrorCuenta(error, { form: form as never, conflictDialog, queryClient, setMensaje }),
    };
    if (editando) editar.mutate({ id: cuenta.id, body: aBody(values, false) }, opts);
    else crear.mutate(aBody(values, true), opts);
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      aria-label={editando ? 'Editar cuenta' : 'Nueva cuenta'}
      className={
        editando
          ? 'space-y-4 rounded-lg border border-dashed border-warning bg-warning-note-bg/40 p-4'
          : 'space-y-4'
      }
    >
      {mensaje && (
        <Alert variant="destructive" role="alert">
          <AlertDescription>{mensaje}</AlertDescription>
        </Alert>
      )}
      {bloqueado && <AvisoNota>{MOTIVO_BLOQUEO}</AvisoNota>}

      <div className="grid grid-cols-1 gap-x-4 gap-y-3.5 md:grid-cols-2">
        <Field label="Cuenta padre" htmlFor="cta-padre" full error={errors.padreId?.message}>
          <Controller
            control={form.control}
            name="padreId"
            render={({ field }) => (
              <CuentaPadreSelector
                id="cta-padre"
                value={field.value}
                onChange={field.onChange}
                excluirId={cuenta?.id}
                padreActual={padreActual}
                disabled={bloqueado}
                title={bloqueado ? MOTIVO_BLOQUEO : undefined}
              />
            )}
          />
        </Field>

        <Field label="Código" htmlFor="cta-codigo" required error={errors.codigo?.message}>
          <Input id="cta-codigo" maxLength={30} readOnly={editando} className="font-mono" {...form.register('codigo')} />
          {ayudaCodigo && <p className="text-xs text-ink-muted" aria-live="polite">{ayudaCodigo}</p>}
        </Field>
        <Field label="Nombre" htmlFor="cta-nombre" required error={errors.nombre?.message}>
          <Input id="cta-nombre" maxLength={254} {...form.register('nombre')} />
        </Field>

        <Field label="Naturaleza" htmlFor="cta-naturaleza" error={errors.naturaleza?.message}>
          <select id="cta-naturaleza" className={SELECT} disabled={bloqueado} title={bloqueado ? MOTIVO_BLOQUEO : undefined} {...form.register('naturaleza')}>
            <option value="">Pendiente de validación</option>
            <option value="Deudora">Deudora</option>
            <option value="Acreedora">Acreedora</option>
          </select>
        </Field>
        <Field label="Tipo" htmlFor="cta-tipo" error={errors.tipo?.message}>
          <select id="cta-tipo" className={SELECT} disabled={bloqueado} title={bloqueado ? MOTIVO_BLOQUEO : undefined} {...form.register('tipo')}>
            <option value="">Pendiente de validación</option>
            <option value="Titulo">Título</option>
            <option value="Afectable">Afectable</option>
          </select>
        </Field>

        <Field label="Cuenta de control" htmlFor="cta-control" error={errors.cuentaControl?.message}>
          <select id="cta-control" className={SELECT} {...form.register('cuentaControl')}>
            <option value="Ninguna">Ninguna</option>
            <option value="Clientes">Clientes</option>
            <option value="Proveedores">Proveedores</option>
          </select>
        </Field>
        <Field label="Código agrupador" opcional htmlFor="cta-agrupador" error={errors.codigoAgrupador?.message}>
          <Input id="cta-agrupador" maxLength={30} {...form.register('codigoAgrupador')} />
        </Field>
        <Field label="Grupo de reporte" opcional htmlFor="cta-grupo" error={errors.grupoReporte?.message}>
          <Input id="cta-grupo" maxLength={60} {...form.register('grupoReporte')} />
        </Field>
      </div>

      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" size="lg" onClick={onCancelar} disabled={guardando}>
          Cancelar
        </Button>
        <Button type="submit" size="lg" disabled={guardando}>
          {guardando ? 'Guardando…' : editando ? 'Guardar cambios' : 'Crear cuenta'}
        </Button>
      </div>
    </form>
  );
}
