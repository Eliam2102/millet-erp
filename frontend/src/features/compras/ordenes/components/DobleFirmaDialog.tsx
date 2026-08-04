import { useState } from 'react';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { TextAreaField } from '@/components/erp';
import {
  MotivoRechazoSelector,
  MotivoRechazoAplicaA,
} from '@/features/compras/components/MotivoRechazoSelector';
import type { MotivoRechazoResponse } from '@/features/compras/api/types';
import {
  CancelarOcConRecepcionesSchema,
  type CancelarOcConRecepcionesValues,
} from '@/features/compras/ordenes/schemas/cancelar-doble-firma';

/**
 * <c>&lt;DobleFirmaDialog/&gt;</c> — modal de cancelación con doble
 * firma para OCs <c>Autorizada</c> con recepciones parciales/completas
 * (UF5-PR1, F5-PR4).
 *
 * <para><b>Política actual del backend</b>: la "doble firma" se
 * implementa como <i>un solo usuario que tiene los 3 permisos</i>
 * (<c>cancelar-doble</c> + <c>autorizar-nivel1</c> + <c>autorizar-nivel2</c>).
 * El backend NO recibe IDs de usuario separados. Por eso este dialog
 * NO presenta selectores de usuario — presenta dos checkboxes de
 * confirmación explícita ("certifico firma N1", "certifico firma N2")
 * para que el usuario reconozca conscientemente que está firmando
 * dos veces antes de habilitar el botón de cancelación definitiva.</para>
 *
 * <para><b>Promoción futura</b>: si el backend evoluciona a aceptar 2
 * IDs separados con verificación cruzada, reemplazar los checkboxes
 * por <c>&lt;UsuarioSelector/&gt;</c> con validación
 * <c>n1Id !== n2Id</c>. La API de props del dialog no cambia.</para>
 *
 * <para><b>UX</b>: layout vertical con motivo arriba, sección de
 * confirmaciones N1+N2 al medio (banner amber), submit destructive
 * abajo. Sin "stepper" formal — la doble firma es <i>una sola decisión
 * con dos confirmaciones explícitas</i>, no un proceso de varios pasos.</para>
 */
export interface DobleFirmaDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  ocFolio: string;
  /** Estado de loading del mutation. */
  isPending?: boolean;
  /** Submit con los valores del schema. El caller dispara el mutation. */
  onSubmit: (values: CancelarOcConRecepcionesValues) => Promise<void>;
}

export function DobleFirmaDialog({
  open,
  onOpenChange,
  ocFolio,
  isPending,
  onSubmit,
}: DobleFirmaDialogProps) {
  const [motivoSeleccionado, setMotivoSeleccionado] =
    useState<MotivoRechazoResponse | null>(null);
  const [confirmoN1, setConfirmoN1] = useState(false);
  const [confirmoN2, setConfirmoN2] = useState(false);

  const form = useForm<CancelarOcConRecepcionesValues>({
    resolver: zodResolver(CancelarOcConRecepcionesSchema),
    defaultValues: {
      motivoCancelacionId: '',
      motivoCancelacionTexto: null,
    },
  });

  const permiteTextoLibre = motivoSeleccionado?.permiteTextoLibre ?? false;
  const ambasFirmas = confirmoN1 && confirmoN2;

  async function handleSubmit(values: CancelarOcConRecepcionesValues) {
    if (!ambasFirmas) return;
    if (
      permiteTextoLibre &&
      (!values.motivoCancelacionTexto ||
        values.motivoCancelacionTexto.trim().length < 3)
    ) {
      form.setError('motivoCancelacionTexto', {
        type: 'manual',
        message: 'El motivo requiere un texto descriptivo (≥3 caracteres).',
      });
      return;
    }
    await onSubmit(values);
    if (!form.formState.isSubmitting) {
      form.reset();
      setMotivoSeleccionado(null);
      setConfirmoN1(false);
      setConfirmoN2(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(o) => {
        if (!isPending) onOpenChange(o);
      }}
    >
      <DialogContent
        className="max-w-lg"
        data-component="doble-firma-dialog"
      >
        <DialogHeader>
          <DialogTitle>Cancelar OC {ocFolio} (doble firma)</DialogTitle>
          <DialogDescription>
            Esta OC tiene recepciones registradas. Las cantidades ya
            recibidas permanecen en las líneas (trazabilidad contable);
            las cantidades no recibidas se liberan al pool de la RQ
            origen.
          </DialogDescription>
        </DialogHeader>

        <form
          onSubmit={form.handleSubmit(handleSubmit)}
          noValidate
          className="space-y-4"
        >
          {/* Motivo */}
          <FieldGroup
            label="Motivo de cancelación"
            required
            error={form.formState.errors.motivoCancelacionId?.message}
          >
            <Controller
              name="motivoCancelacionId"
              control={form.control}
              render={({ field }) => (
                <MotivoRechazoSelector
                  aplicaA={
                    MotivoRechazoAplicaA.Cancelacion as MotivoRechazoAplicaA
                  }
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                  onMotivoChange={setMotivoSeleccionado}
                  disabled={isPending}
                />
              )}
            />
          </FieldGroup>

          {permiteTextoLibre && (
            <FieldGroup
              label="Detalle del motivo"
              required
              error={form.formState.errors.motivoCancelacionTexto?.message}
            >
              <Controller
                name="motivoCancelacionTexto"
                control={form.control}
                render={({ field }) => (
                  <TextAreaField
                    value={field.value || null}
                    onChange={(v) => field.onChange(v || null)}
                    maxLength={500}
                    minRows={2}
                    disabled={isPending}
                  />
                )}
              />
            </FieldGroup>
          )}

          {/* Confirmación de doble firma */}
          <div
            className="rounded-md border border-amber-300 bg-amber-50 p-3 space-y-2"
            data-component="doble-firma-confirmaciones"
          >
            <div className="flex items-start gap-2 text-sm font-medium text-amber-900">
              <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" />
              <span>
                Esta acción requiere doble firma. Por política
                (<code>cancelar-doble</code> + <code>autorizar-nivel1</code> +
                {' '}
                <code>autorizar-nivel2</code>), la firmas tú mismo en
                ambos niveles. Confirma explícitamente:
              </span>
            </div>
            <label className="flex items-start gap-2 text-sm cursor-pointer">
              <input
                type="checkbox"
                checked={confirmoN1}
                onChange={(e) => setConfirmoN1(e.target.checked)}
                disabled={isPending}
                aria-label="Confirmo firma N1"
                className="mt-0.5 h-4 w-4 cursor-pointer accent-amber-700"
              />
              <span>
                <strong>Certifico firma Nivel 1</strong> — entiendo que
                actúo en representación del Jefe de Compras para esta
                cancelación.
              </span>
            </label>
            <label className="flex items-start gap-2 text-sm cursor-pointer">
              <input
                type="checkbox"
                checked={confirmoN2}
                onChange={(e) => setConfirmoN2(e.target.checked)}
                disabled={isPending}
                aria-label="Confirmo firma N2"
                className="mt-0.5 h-4 w-4 cursor-pointer accent-amber-700"
              />
              <span>
                <strong>Certifico firma Nivel 2</strong> — entiendo que
                actúo en representación de la Dirección para esta
                cancelación.
              </span>
            </label>
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
              disabled={isPending}
            >
              Cancelar
            </Button>
            <Button
              type="submit"
              variant="destructive"
              disabled={isPending || !ambasFirmas}
              data-action="confirmar-doble-firma"
            >
              {isPending ? 'Procesando…' : 'Cancelar OC definitivamente'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

function FieldGroup({
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
    <div className="space-y-1">
      <label className="flex items-center gap-1 text-xs font-medium text-foreground">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {error && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
