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
import { useCrearCuenta, useCuentas, useEditarCuenta, useSiguienteCodigo } from '../api/hooks';
import type { Cuenta } from '../api/types';
import { aBody, CuentaSchema, VALORES_VACIOS, type CuentaValues } from '../schemas/cuenta';
import { manejarErrorCuenta } from '../lib/errores';
import { SELECT_CLASS } from '../lib/estilos';
import { ETIQUETA_COLECTIVA, ETIQUETA_TIPO } from '../lib/textos';
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

const COLECTIVAS = ['Ninguna', 'Clientes', 'Deudores', 'Proveedores', 'Acreedores'] as const;

export const MOTIVO_BLOQUEO =
  'La cuenta (o una de sus hijas) ya tiene movimientos: cambiar su padre o su naturaleza alteraría la interpretación de saldos históricos. ' +
  'Para reclasificar, crea una cuenta nueva y desactiva esta (procedimiento de impacto aprobado por Contabilidad).';

function valoresDe(c: Cuenta): CuentaValues {
  return {
    codigo: c.codigo,
    nombre: c.nombre,
    padreId: c.padreId ?? '',
    naturaleza: c.naturaleza ?? '',
    cuentaControl: c.cuentaControl,
    rubroId: c.rubroId ?? '',
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

  // P19: el tipo lo calcula el sistema; aquí solo se explica. Alta sin padre = nivel 1 (acumula); con padre = afectable.
  const esRubro = cuenta?.clase === 'Rubro';
  const tipoMostrado = editando && cuenta.tipo ? cuenta.tipo : padreId ? 'Afectable' : 'Titulo';
  const ayudaTipo = esRubro
    ? 'Un rubro es una agrupación de reporte: no recibe movimientos.'
    : editando
      ? 'Lo calcula el sistema: acumula si es de nivel 1 o tiene cuentas debajo; si no, recibe movimientos.'
      : padreId
        ? 'Recibirá movimientos mientras no tenga cuentas debajo. Si la cuenta padre hoy recibe movimientos y no tiene ninguno registrado, pasará a acumular.'
        : 'Las cuentas de nivel 1 (sin cuenta padre) acumulan: sus movimientos se registran en las cuentas de debajo.';
  // P24: el rubro solo aplica a cuentas de nivel 1 (sin padre); un rubro no pertenece a otro rubro.
  const conRubro = !padreId && !esRubro;
  const rubros = useCuentas({ clase: 'Rubro', estatus: 'Activo', limit: 200 }, conRubro);

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
        <Field label="Tipo" htmlFor="cta-tipo">
          <Input id="cta-tipo" readOnly aria-describedby="cta-tipo-ayuda" value={esRubro ? 'Rubro de reporte' : ETIQUETA_TIPO[tipoMostrado]} />
          <p id="cta-tipo-ayuda" className="text-xs text-ink-muted">{ayudaTipo}</p>
        </Field>

        {!esRubro && (
          <Field label="Cuenta colectiva" htmlFor="cta-control" error={errors.cuentaControl?.message}>
            <select id="cta-control" className={SELECT} aria-describedby="cta-control-ayuda" {...form.register('cuentaControl')}>
              {COLECTIVAS.map((c) => <option key={c} value={c}>{ETIQUETA_COLECTIVA[c]}</option>)}
            </select>
            <p id="cta-control-ayuda" className="text-xs text-ink-muted">
              Una cuenta colectiva no admite captura manual: la afecta solo su módulo, que lleva el detalle por persona.
            </p>
          </Field>
        )}
        {conRubro && (
          <Field label="Rubro de reporte" opcional htmlFor="cta-rubro" error={errors.rubroId?.message}>
            {/* Controlado: las opciones llegan después del valor inicial al editar. */}
            <Controller
              control={form.control}
              name="rubroId"
              render={({ field }) => (
                <select id="cta-rubro" className={SELECT} value={field.value} onChange={field.onChange} onBlur={field.onBlur}>
                  <option value="">Sin rubro</option>
                  {(rubros.data?.items ?? []).map((r) => <option key={r.id} value={r.id}>{r.codigo} — {r.nombre}</option>)}
                </select>
              )}
            />
            <p className="text-xs text-ink-muted">Agrupa la cuenta en el reporte; el rubro suma el saldo de sus cuentas de nivel 1 una sola vez.</p>
          </Field>
        )}
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
