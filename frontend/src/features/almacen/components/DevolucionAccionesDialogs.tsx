import { hoyLocalISO } from '@/lib/datetime';
import { useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
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
import { TextAreaField } from '@/components/erp';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import {
  RechazarDevolucionSchema,
  RegistrarSalidaDevolucionSchema,
  type RechazarDevolucionValues,
  type RegistrarSalidaDevolucionValues,
} from '@/features/almacen/schemas/devolucion';
import {
  useAutorizarDevolucionProveedor,
  useDevolucionProveedor,
  useRechazarDevolucionProveedor,
  useRegistrarSalidaDevolucionProveedor,
  useSolicitarAutorizacionProveedor,
  useSubAlmacenes,
} from '@/features/almacen/api';
import { Field } from '@/features/almacen/components/internal/Field';
import { UbicacionBinSelector } from '@/components/erp/selectors/UbicacionBinSelector';

// ─── Solicitar autorización (confirm simple) ────────────────────────────────

export function SolicitarAutorizacionConfirm({
  devolucionId,
  open,
  onOpenChange,
}: {
  devolucionId: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const solicitar = useSolicitarAutorizacionProveedor();
  function ejecutar() {
    if (!devolucionId) return;
    solicitar.mutate(
      { devolucionId },
      {
        onSuccess: () => {
          toast.success('Solicitud de autorización enviada a Dirección');
          onOpenChange(false);
        },
        onError: (e) => manejarError(e),
      },
    );
  }
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>¿Solicitar autorización?</AlertDialogTitle>
          <AlertDialogDescription>
            La devolución pasará a estado <b>En autorización</b>. Dirección
            la revisará junto con las evidencias adjuntas. Asegúrate de
            haber agregado al menos una evidencia.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={solicitar.isPending}>
            Cancelar
          </AlertDialogCancel>
          <AlertDialogAction
            disabled={solicitar.isPending}
            onClick={ejecutar}
          >
            {solicitar.isPending ? 'Enviando…' : 'Solicitar'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

// ─── Autorizar (confirm simple, Dirección) ──────────────────────────────────

export function AutorizarDevolucionConfirm({
  devolucionId,
  open,
  onOpenChange,
}: {
  devolucionId: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const autorizar = useAutorizarDevolucionProveedor();
  function ejecutar() {
    if (!devolucionId) return;
    autorizar.mutate(
      { devolucionId },
      {
        onSuccess: () => {
          toast.success('Devolución autorizada');
          onOpenChange(false);
        },
        onError: (e) => manejarError(e),
      },
    );
  }
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>¿Autorizar devolución?</AlertDialogTitle>
          <AlertDialogDescription>
            Confirmas la autorización de Dirección. La devolución pasará a
            estado <b>Autorizada</b> y el Almacenista podrá registrar la
            salida física.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={autorizar.isPending}>
            Cancelar
          </AlertDialogCancel>
          <AlertDialogAction
            disabled={autorizar.isPending}
            onClick={ejecutar}
          >
            {autorizar.isPending ? 'Autorizando…' : 'Autorizar'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

// ─── Rechazar (con motivo, Dirección) ───────────────────────────────────────

export function RechazarDevolucionDialog({
  devolucionId,
  open,
  onOpenChange,
}: {
  devolucionId: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const rechazar = useRechazarDevolucionProveedor();
  const form = useForm<RechazarDevolucionValues>({
    resolver: zodResolver(RechazarDevolucionSchema),
    defaultValues: { motivoRechazo: '' },
  });

  function onSubmit(values: RechazarDevolucionValues) {
    if (!devolucionId) return;
    rechazar.mutate(
      { devolucionId, motivoRechazo: values.motivoRechazo },
      {
        onSuccess: () => {
          toast.success('Devolución rechazada');
          onOpenChange(false);
          form.reset({ motivoRechazo: '' });
        },
        onError: (e) => manejarError(e),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Rechazar devolución</DialogTitle>
          <DialogDescription>
            Captura el motivo del rechazo. La devolución queda cerrada y el
            material no se devolverá al proveedor.
          </DialogDescription>
        </DialogHeader>
        <form
          id="rechazar-devolucion-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="space-y-3"
        >
          <Field
            label="Motivo del rechazo"
            required
            error={form.formState.errors.motivoRechazo?.message}
          >
            <Controller
              name="motivoRechazo"
              control={form.control}
              render={({ field }) => (
                <TextAreaField
                  value={field.value ?? null}
                  onChange={(v) => field.onChange(v ?? '')}
                  maxLength={500}
                  minRows={3}
                />
              )}
            />
          </Field>
        </form>
        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={rechazar.isPending}
          >
            Cancelar
          </Button>
          <Button
            type="submit"
            form="rechazar-devolucion-form"
            disabled={rechazar.isPending}
            variant="destructive"
          >
            {rechazar.isPending ? 'Rechazando…' : 'Rechazar'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ─── Registrar salida (Almacenista) ─────────────────────────────────────────

export function RegistrarSalidaDevolucionSheet({
  devolucionId,
  open,
  onOpenChange,
}: {
  devolucionId: string | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Registrar salida de devolución</SheetTitle>
          <SheetDescription>
            Confirma que el material físicamente salió del almacén hacia
            el proveedor. Esto genera el movimiento de salida y publica
            el evento al outbox.
          </SheetDescription>
        </SheetHeader>

        {open && devolucionId && (
          <RegistrarSalidaBody
            devolucionId={devolucionId}
            onSuccess={() => onOpenChange(false)}
            onCancel={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function RegistrarSalidaBody({
  devolucionId,
  onSuccess,
  onCancel,
}: {
  devolucionId: string;
  onSuccess: () => void;
  onCancel: () => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const registrar = useRegistrarSalidaDevolucionProveedor();
  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });
  const detalle = useDevolucionProveedor(devolucionId);
  const hoyIso = hoyLocalISO();

  // C7.2b: bin real por línea, elegido al registrar (por saldo).
  const [binPorLinea, setBinPorLinea] = useState<Record<string, string>>({});

  const form = useForm<RegistrarSalidaDevolucionValues>({
    resolver: zodResolver(RegistrarSalidaDevolucionSchema),
    defaultValues: { subAlmacenId: '', fechaMovimiento: hoyIso },
  });
  const subAlmacenId = useWatch({ control: form.control, name: 'subAlmacenId' });
  const lineas = detalle.data?.lineas ?? [];

  function onSubmit(values: RegistrarSalidaDevolucionValues) {
    const faltante = lineas.find((l) => !binPorLinea[l.id]);
    if (faltante) {
      toast.error('Elige la ubicación de salida de cada línea.');
      return;
    }
    registrar.mutate(
      {
        command: {
          devolucionId,
          subAlmacenId: values.subAlmacenId,
          fechaMovimiento: values.fechaMovimiento,
          bins: lineas.map((l) => ({
            lineaDevolucionId: l.id,
            ubicacionId: binPorLinea[l.id],
          })),
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Salida ${resp.folioMovimientoSalida} registrada`);
          onSuccess();
        },
        onError: (e) => manejarError(e),
      },
    );
  }

  return (
    <>
      <form
        id="registrar-salida-devolucion-form"
        onSubmit={form.handleSubmit(onSubmit)}
        noValidate
        className="flex-1 space-y-4 overflow-y-auto px-6"
      >
        <Field
          label="Sub-almacén origen"
          required
          error={form.formState.errors.subAlmacenId?.message}
        >
          <Controller
            name="subAlmacenId"
            control={form.control}
            render={({ field }) => (
              <Select
                value={field.value || ''}
                onValueChange={(v) => field.onChange(v)}
                disabled={subAlmacenesQuery.isLoading}
              >
                <SelectTrigger>
                  <SelectValue placeholder="Selecciona sub-almacén" />
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
          label="Fecha de movimiento"
          required
          error={form.formState.errors.fechaMovimiento?.message}
        >
          <Controller
            name="fechaMovimiento"
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

        {/* C7.2b: bin de salida por línea (elegido por saldo). */}
        <div className="space-y-2">
          <h3 className="text-sm font-semibold">Ubicación de salida por línea</h3>
          {detalle.isLoading ? (
            <p className="text-sm text-muted-foreground">Cargando líneas…</p>
          ) : lineas.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              La devolución no tiene líneas.
            </p>
          ) : (
            lineas.map((l) => (
              <div key={l.id} className="rounded-md border p-2">
                <p className="mb-1 text-xs text-muted-foreground">
                  {l.articuloClave ? (
                    <code className="font-mono">{l.articuloClave}</code>
                  ) : (
                    <code className="font-mono" title={l.articuloId}>
                      {l.articuloId.slice(0, 8)}…
                    </code>
                  )}
                  {' · '}
                  {l.cantidad} {l.unidadMedida}
                </p>
                <UbicacionBinSelector
                  modo="saldo"
                  articuloId={l.articuloId}
                  subAlmacenId={subAlmacenId}
                  value={binPorLinea[l.id] ?? null}
                  onChange={(id) =>
                    setBinPorLinea((prev) => {
                      const next = { ...prev };
                      if (id) next[l.id] = id;
                      else delete next[l.id];
                      return next;
                    })
                  }
                />
              </div>
            ))
          )}
        </div>
      </form>

      <SheetFooter>
        <Button
          type="button"
          variant="ghost"
          onClick={onCancel}
          disabled={registrar.isPending}
        >
          Cancelar
        </Button>
        <Button
          type="submit"
          form="registrar-salida-devolucion-form"
          disabled={registrar.isPending}
        >
          {registrar.isPending ? 'Registrando…' : 'Registrar salida'}
        </Button>
      </SheetFooter>
    </>
  );
}

// ─── Helpers ────────────────────────────────────────────────────────────────

function manejarError(error: unknown) {
  if (esApiError(error)) {
    toast.error(error.problem.title, {
      description: error.traceId ? `Código: ${error.traceId}` : undefined,
    });
    return;
  }
  toast.error('Error inesperado.');
}
