import { hoyLocalISO } from '@/lib/datetime';
import { useEffect } from 'react';
import { Link } from '@tanstack/react-router';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import {
  AvisoEnLinea,
  EmpleadoSelector,
  Field,
  MoneyField,
  PuestoSelector,
} from '@/components/erp';
import { useConfirmarDescarte } from '@/components/erp/forms/useConfirmarDescarte';
import { MoneyDisplay } from '@/components/erp';
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  SolicitarAnticipoViaticosSchema,
  type SolicitarAnticipoViaticosValues,
} from '@/features/cxp/schemas/viaticos';
import { useSolicitarAnticipoViaticos } from '@/features/cxp/api/useViaticos';
import { usePoliticasViaticos } from '@/features/cxp/api/useAdminCxp';
import {
  TipoDestinoViatico,
  TipoDestinoViaticoLabels,
} from '@/features/cxp/api/types';

/**
 * <c>&lt;NuevaSolicitudViaticosSheet/&gt;</c> — el empleado captura una
 * solicitud de viático. El backend valida contra política puesto+destino
 * y devuelve <c>topePolitica</c> + <c>excedePolitica</c> en la respuesta.
 *
 * <para>UI muestra hint visual para la justificación cuando el destino
 * es Internacional o cuando el usuario ingresa un monto alto (el backend
 * decide la verdad). Justificación marcada con asterisco contextual.</para>
 *
 * <para>El tope y el exceso se muestran en vivo con la misma query que
 * <c>PoliticasViaticosPage</c> (tope = monto máx/día × días). Sin política para
 * puesto + destino, el envío se bloquea con motivo (el backend responde
 * <c>VIA_POLITICA_NO_DEFINIDA</c>). Si la query falla (p. ej. sin permiso),
 * solo se avisa y el backend sigue siendo la verdad.</para>
 */
export interface NuevaSolicitudViaticosSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NuevaSolicitudViaticosSheet({
  open,
  onOpenChange,
}: NuevaSolicitudViaticosSheetProps) {
  const { setDirty, solicitarCierre, dialogo } = useConfirmarDescarte(() =>
    onOpenChange(false),
  );
  return (
    <Sheet
      open={open}
      onOpenChange={(next) => (next ? onOpenChange(next) : solicitarCierre())}
    >
      <SheetContent side="right" size="form">
        <SheetHeader>
          <SheetTitle>Nueva solicitud de viáticos</SheetTitle>
          <SheetDescription>
            El sistema evalúa contra política del puesto y destino. Si excede,
            requerirá firma adicional de Dirección de Finanzas.
          </SheetDescription>
        </SheetHeader>
        {open && (
          <Form
            onCancel={() => solicitarCierre()}
            onSolicitada={() => solicitarCierre(true)}
            onDirtyChange={setDirty}
          />
        )}
      </SheetContent>
      {dialogo}
    </Sheet>
  );
}

function Form({
  onCancel,
  onSolicitada,
  onDirtyChange,
}: {
  onCancel: () => void;
  onSolicitada: () => void;
  onDirtyChange: (dirty: boolean) => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const solicitar = useSolicitarAnticipoViaticos();
  const hoy = hoyLocalISO();

  const form = useForm<SolicitarAnticipoViaticosValues>({
    resolver: zodResolver(SolicitarAnticipoViaticosSchema),
    defaultValues: {
      empleadoId: '',
      puestoId: '',
      jefeDirectoId: '',
      destino: '',
      tipoDestino: TipoDestinoViatico.Nacional,
      fechaSalida: hoy,
      fechaRegreso: hoy,
      moneda: 'MXN',
      montoSolicitado: 0,
      justificacionExceso: null,
    },
  });

  const tipoDestino = useWatch({
    control: form.control,
    name: 'tipoDestino',
  });
  const empleadoSeleccionado = useWatch({
    control: form.control,
    name: 'empleadoId',
  });
  const puestoId = useWatch({ control: form.control, name: 'puestoId' });
  const moneda = useWatch({ control: form.control, name: 'moneda' });
  const monto = useWatch({ control: form.control, name: 'montoSolicitado' });
  const fechaSalida = useWatch({ control: form.control, name: 'fechaSalida' });
  const fechaRegreso = useWatch({ control: form.control, name: 'fechaRegreso' });
  const dias =
    fechaSalida && fechaRegreso
      ? Math.max(
          1,
          (Date.parse(fechaRegreso) - Date.parse(fechaSalida)) /
            (1000 * 60 * 60 * 24) +
            1,
        )
      : 0;


  const isDirty = form.formState.isDirty;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  // Política vigente para el par puesto + destino (misma query que la admin).
  const politicas = usePoliticasViaticos({
    puestoId: puestoId || undefined,
    tipoDestino,
    limit: 200,
  });
  const politica = puestoId
    ? politicas.data?.items.find(
        (p) => p.puestoId === puestoId && p.tipoDestino === tipoDestino,
      )
    : undefined;
  const sinPolitica =
    !!puestoId && politicas.isSuccess && politica === undefined;
  const tope = politica ? politica.montoMaxDia * dias : null;
  const excede =
    tope !== null && politica?.moneda === moneda && monto > tope;
  const motivoBloqueo = sinPolitica
    ? 'No hay política de viáticos para este puesto y destino; pide a RH que la dé de alta.'
    : undefined;

  function onSubmit(values: SolicitarAnticipoViaticosValues) {
    solicitar.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: (res) => {
          const aviso = res.excedePolitica
            ? ` — excede tope de política (${res.topePolitica.toFixed(2)}). Pasa a Dirección de Finanzas.`
            : '';
          toast.success(`Solicitud creada${aviso}`);
          onSolicitada();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            )
              return;
            toast.error(error.problem.title);
            return;
          }
          toast.error('Error al solicitar viáticos.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Campo
          label="Empleado"
          required
          error={form.formState.errors.empleadoId?.message}
        >
          {/* Al elegir empleado se prellenan puesto (tope de la política)
              y jefe directo (autorizador N1), ambos editables. */}
          {(control) => (
          <Controller
            control={form.control}
            name="empleadoId"
            render={({ field }) => (
              <EmpleadoSelector
                triggerProps={control}
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                onSelectItem={(empleado) => {
                  if (!empleado) return;
                  if (empleado.puestoId) {
                    form.setValue('puestoId', empleado.puestoId, {
                      shouldValidate: true,
                    });
                  }
                  if (empleado.jefeDirectoId) {
                    form.setValue('jefeDirectoId', empleado.jefeDirectoId, {
                      shouldValidate: true,
                    });
                  }
                }}
              />
            )}
          />
          )}
        </Campo>
        <Campo
          label="Puesto"
          required
          error={form.formState.errors.puestoId?.message}
        >
          {(control) => (
          <Controller
            control={form.control}
            name="puestoId"
            render={({ field }) => (
              <PuestoSelector
                triggerProps={control}
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
              />
            )}
          />
          )}
        </Campo>
        <Campo
          label="Jefe directo"
          required
          error={form.formState.errors.jefeDirectoId?.message}
        >
          {(control) => (
          <Controller
            control={form.control}
            name="jefeDirectoId"
            render={({ field }) => (
              <EmpleadoSelector
                triggerProps={control}
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                excludeId={empleadoSeleccionado || null}
              />
            )}
          />
          )}
        </Campo>
        <Campo
          label="Destino"
          required
          error={form.formState.errors.destino?.message}
        >
          <Input
            placeholder="Ej.: Monterrey, NL"
            {...form.register('destino')}
          />
        </Campo>
        <Campo label="Tipo destino" required>
          {(control) => (
          <Controller
            control={form.control}
            name="tipoDestino"
            render={({ field }) => (
              <Select
                value={String(field.value)}
                onValueChange={(v) => field.onChange(Number(v))}
              >
                <SelectTrigger {...control}>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {[
                    TipoDestinoViatico.Nacional,
                    TipoDestinoViatico.Internacional,
                  ].map((t) => (
                    <SelectItem key={t} value={String(t)}>
                      {TipoDestinoViaticoLabels[t]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
          )}
        </Campo>
        <Campo
          label="Fecha salida"
          required
          error={form.formState.errors.fechaSalida?.message}
        >
          <Input type="date" {...form.register('fechaSalida')} />
        </Campo>
        <Campo
          label="Fecha regreso"
          required
          error={form.formState.errors.fechaRegreso?.message}
        >
          <Input type="date" {...form.register('fechaRegreso')} />
        </Campo>
        <Campo
          label={`Monto solicitado${dias > 0 ? ` (${dias} día${dias === 1 ? '' : 's'})` : ''}`}
          required
          error={form.formState.errors.montoSolicitado?.message}
        >
          {(control) => (
            <MoneyField
              value={monto > 0 ? { amount: monto, currency: moneda } : null}
              defaultCurrency={moneda}
              onChange={(m) => {
                form.setValue('montoSolicitado', m?.amount ?? 0, {
                  shouldDirty: true,
                  shouldValidate: form.formState.isSubmitted,
                });
                if (m) form.setValue('moneda', m.currency, { shouldDirty: true });
              }}
              inputProps={{ ...control, min: 0 }}
            />
          )}
        </Campo>
      </div>

      {sinPolitica && (
        <div id="aviso-sin-politica">
          <AvisoEnLinea
            tono="error"
            titulo="Sin política de viáticos"
            accion={
              <Link
                to="/cxp/admin/politicas-viaticos"
                className="font-medium underline"
              >
                Ver políticas
              </Link>
            }
          >
            No hay política para este puesto y destino; pide a RH que la dé de
            alta. No se puede solicitar hasta entonces.
          </AvisoEnLinea>
        </div>
      )}
      {puestoId && politicas.isError && (
        <AvisoEnLinea tono="advertencia">
          No se pudo verificar la política del puesto; el sistema la validará al
          enviar.
        </AvisoEnLinea>
      )}
      {politica && tope !== null && (
        <AvisoEnLinea tono="advertencia" titulo={excede ? 'Excede el tope de la política' : 'Tope de la política'}>
          Tope: <MoneyDisplay money={{ amount: tope, currency: politica.moneda }} /> (
          <MoneyDisplay
            money={{ amount: politica.montoMaxDia, currency: politica.moneda }}
          />{' '}
          por día × {dias} día{dias === 1 ? '' : 's'}).
          {excede &&
            ' El monto solicitado lo supera: agrega justificación y pasará a Dirección de Finanzas.'}
        </AvisoEnLinea>
      )}
      {tipoDestino === TipoDestinoViatico.Internacional && !politica && !sinPolitica && (
        <AvisoEnLinea tono="advertencia">
          Viaje internacional: probable exceso de política. Agrega justificación
          si excedes el tope.
        </AvisoEnLinea>
      )}

      <Campo
        label="Justificación si excede política"
        error={form.formState.errors.justificacionExceso?.message}
      >
        <Textarea
          rows={3}
          placeholder="Solo necesaria si superas el tope. Se valida al enviar."
          {...form.register('justificacionExceso')}
        />
      </Campo>

      <SheetFooter className="px-0">
        <Button
          type="button"
          variant="ghost"
          onClick={onCancel}
          disabled={solicitar.isPending}
        >
          Cancelar
        </Button>
        <Button
          type="submit"
          disabled={solicitar.isPending || sinPolitica}
          title={motivoBloqueo}
          aria-describedby={sinPolitica ? 'aviso-sin-politica' : undefined}
        >
          {solicitar.isPending ? 'Solicitando…' : 'Solicitar viáticos'}
        </Button>
      </SheetFooter>
    </form>
  );
}

/** Alias local: `Field` accesible (label asociado, aria-*). */
const Campo = Field;
