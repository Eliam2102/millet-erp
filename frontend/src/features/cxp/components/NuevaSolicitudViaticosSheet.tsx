import { hoyLocalISO } from '@/lib/datetime';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { EmpleadoSelector, PuestoSelector } from '@/components/erp';
import { AlertTriangle } from 'lucide-react';
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
import { Label } from '@/components/ui/label';
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
 * <para>PLATFORM-TODO(&lt;PoliticaViaticosCheckEnVivo&gt;): hoy el
 * backend solo evalúa al submit. Cuando exista un endpoint
 * <c>GET /viaticos/preview-politica?puestoId=...&tipoDestino=...&dias=...</c>,
 * agregar un &lt;PoliticaViaticosCheck&gt; live con monto sugerido + flag
 * de exceso antes del POST.</para>
 */
export interface NuevaSolicitudViaticosSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NuevaSolicitudViaticosSheet({
  open,
  onOpenChange,
}: NuevaSolicitudViaticosSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Nueva solicitud de viáticos</SheetTitle>
          <SheetDescription>
            El backend evalúa contra política del puesto y destino. Si excede,
            requerirá firma adicional de Dirección de Finanzas.
          </SheetDescription>
        </SheetHeader>
        {open && <Form onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Form({ onClose }: { onClose: () => void }) {
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

  function onSubmit(values: SolicitarAnticipoViaticosValues) {
    solicitar.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: (res) => {
          const aviso = res.excedePolitica
            ? ` — excede tope de política (${res.topePolitica.toFixed(2)}). Pasa a Dirección de Finanzas.`
            : '';
          toast.success(`Solicitud creada${aviso}`);
          onClose();
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
          <Controller
            control={form.control}
            name="empleadoId"
            render={({ field }) => (
              <EmpleadoSelector
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
        </Campo>
        <Campo
          label="Puesto"
          required
          error={form.formState.errors.puestoId?.message}
        >
          <Controller
            control={form.control}
            name="puestoId"
            render={({ field }) => (
              <PuestoSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
              />
            )}
          />
        </Campo>
        <Campo
          label="Jefe directo"
          required
          error={form.formState.errors.jefeDirectoId?.message}
        >
          <Controller
            control={form.control}
            name="jefeDirectoId"
            render={({ field }) => (
              <EmpleadoSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                excludeId={empleadoSeleccionado || null}
              />
            )}
          />
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
          <Controller
            control={form.control}
            name="tipoDestino"
            render={({ field }) => (
              <Select
                value={String(field.value)}
                onValueChange={(v) => field.onChange(Number(v))}
              >
                <SelectTrigger>
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
        </Campo>
        <Campo label="Moneda" required>
          <Input
            placeholder="MXN"
            maxLength={3}
            className="uppercase"
            {...form.register('moneda')}
          />
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
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register('montoSolicitado', { valueAsNumber: true })}
          />
        </Campo>
      </div>

      {tipoDestino === TipoDestinoViatico.Internacional && (
        <div className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900">
          <AlertTriangle className="mr-1 inline h-3 w-3" />
          Viaje internacional: probable exceso de política. Agrega justificación
          si excedes el tope.
        </div>
      )}

      <Campo
        label="Justificación si excede política"
        error={form.formState.errors.justificacionExceso?.message}
      >
        <Textarea
          rows={3}
          placeholder="Solo necesaria si superas el tope. El backend valida al recibir."
          {...form.register('justificacionExceso')}
        />
      </Campo>

      <SheetFooter className="px-0">
        <Button
          type="button"
          variant="ghost"
          onClick={onClose}
          disabled={solicitar.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={solicitar.isPending}>
          {solicitar.isPending ? 'Solicitando…' : 'Solicitar viáticos'}
        </Button>
      </SheetFooter>
    </form>
  );
}

interface CampoProps {
  label: string;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}

function Campo({ label, required, error, children }: CampoProps) {
  return (
    <div className="space-y-1">
      <Label className="text-xs">
        {label}
        {required && <span className="ml-1 text-destructive">*</span>}
      </Label>
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
