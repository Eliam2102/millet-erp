import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Loader2, Plus, ShieldAlert, ShieldCheck, Trash2, Upload } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';
import { SubirFielDialog } from '@/features/integraciones-fiscal/components/SubirFielDialog';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useActualizarRfcReceptor,
  useAgregarRfcReceptor,
  useEliminarRfcReceptor,
  useRfcsReceptores,
} from '@/features/integraciones-fiscal/api/useIntegracionesFiscal';
import {
  AgregarRfcReceptorSchema,
  type AgregarRfcReceptorValues,
} from '@/features/integraciones-fiscal/schemas/configuracion-pac';

/**
 * <c>&lt;RfcReceptorList/&gt;</c> — tabla inline (sin modal) de RFCs
 * receptores de una empresa. Sigue la convención del proyecto:
 * agregar/editar inline, no abrir modales para items.
 *
 * <para>Cada fila tiene 2 switches (descarga / refresh) + botón
 * eliminar (soft-delete). El form de "agregar" es inline al final con
 * border dashed.</para>
 */
export interface RfcReceptorListProps {
  empresaId: string;
}

export function RfcReceptorList({ empresaId }: RfcReceptorListProps) {
  const canAdministrar = useHasPermission(PermisosCanonicos.IntegracionesFiscalAdministrar);
  const list = useRfcsReceptores(empresaId);

  return (
    <div className="space-y-3 p-4">
      <h3 className="text-sm font-semibold">RFCs receptores</h3>

      {list.isLoading && (
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="h-3 w-3 animate-spin" />
          Cargando…
        </p>
      )}

      {list.data && list.data.length === 0 && (
        <p className="text-sm text-muted-foreground">
          Sin RFCs receptores configurados. Agrega al menos uno para que el worker
          de descarga consulte el SAT.
        </p>
      )}

      <ul className="space-y-2">
        {list.data?.map((rfc) => (
          <RfcRow key={rfc.id} rfc={rfc} canEdit={canAdministrar} />
        ))}
      </ul>

      {canAdministrar && <AgregarRfcRow empresaId={empresaId} />}
    </div>
  );
}

interface RfcRowProps {
  rfc: {
    id: string;
    empresaId: string;
    rfc: string;
    descargaHabilitada: boolean;
    refreshHabilitada: boolean;
    checkpointDescargaAt: string | null;
    tieneFiel: boolean;
    fielValidFrom: string | null;
    fielValidTo: string | null;
    fielSubidaAt: string | null;
  };
  canEdit: boolean;
}

function RfcRow({ rfc, canEdit }: RfcRowProps) {
  const actualizar = useActualizarRfcReceptor();
  const eliminar = useEliminarRfcReceptor();
  const idempotencyKey = useFormIdempotencyKey();
  const [fielDialogOpen, setFielDialogOpen] = useState(false);

  function toggle(field: 'descargaHabilitada' | 'refreshHabilitada', value: boolean) {
    actualizar.mutate(
      {
        id: rfc.id,
        empresaId: rfc.empresaId,
        payload: {
          descargaHabilitada:
            field === 'descargaHabilitada' ? value : rfc.descargaHabilitada,
          refreshHabilitada:
            field === 'refreshHabilitada' ? value : rfc.refreshHabilitada,
        },
        idempotencyKey,
      },
      {
        onError: () => toast.error('Error al actualizar RFC.'),
      },
    );
  }

  function handleDelete() {
    if (!confirm(`¿Eliminar RFC ${rfc.rfc}?`)) return;
    eliminar.mutate(
      { id: rfc.id, empresaId: rfc.empresaId, idempotencyKey },
      {
        onSuccess: () => toast.success(`RFC ${rfc.rfc} eliminado.`),
        onError: () => toast.error('Error al eliminar RFC.'),
      },
    );
  }

  return (
    <li className="flex flex-wrap items-center gap-3 rounded-md border p-3">
      <span className="font-mono text-sm">{rfc.rfc}</span>

      {rfc.tieneFiel ? (
        <Badge variant="secondary" className="gap-1">
          <ShieldCheck className="h-3 w-3" />
          FIEL hasta {rfc.fielValidTo ? new Date(rfc.fielValidTo).toLocaleDateString() : '—'}
        </Badge>
      ) : (
        <Badge variant="outline" className="gap-1 border-amber-400 text-amber-700">
          <ShieldAlert className="h-3 w-3" />
          Sin FIEL
        </Badge>
      )}

      <div className="ml-auto flex items-center gap-3">
        <div className="flex items-center gap-2">
          <Label htmlFor={`desc-${rfc.id}`} className="text-xs">Descarga</Label>
          <Checkbox
            id={`desc-${rfc.id}`}
            checked={rfc.descargaHabilitada}
            disabled={!canEdit || actualizar.isPending}
            onCheckedChange={(v) => toggle('descargaHabilitada', v === true)}
          />
        </div>

        <div className="flex items-center gap-2">
          <Label htmlFor={`ref-${rfc.id}`} className="text-xs">Refresh</Label>
          <Checkbox
            id={`ref-${rfc.id}`}
            checked={rfc.refreshHabilitada}
            disabled={!canEdit || actualizar.isPending}
            onCheckedChange={(v) => toggle('refreshHabilitada', v === true)}
          />
        </div>

        {rfc.checkpointDescargaAt && (
          <span className="text-xs text-muted-foreground">
            Checkpoint: {new Date(rfc.checkpointDescargaAt).toLocaleString()}
          </span>
        )}

        {canEdit && (
          <>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setFielDialogOpen(true)}
              className="gap-1"
            >
              <Upload className="h-3 w-3" />
              {rfc.tieneFiel ? 'Renovar FIEL' : 'Subir FIEL'}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              onClick={handleDelete}
              disabled={eliminar.isPending}
              aria-label={`Eliminar RFC ${rfc.rfc}`}
            >
              <Trash2 className="h-4 w-4 text-destructive" />
            </Button>
          </>
        )}
      </div>

      {canEdit && (
        <SubirFielDialog
          open={fielDialogOpen}
          onOpenChange={setFielDialogOpen}
          rfcReceptorId={rfc.id}
          empresaId={rfc.empresaId}
          rfc={rfc.rfc}
        />
      )}
    </li>
  );
}

function AgregarRfcRow({ empresaId }: { empresaId: string }) {
  const agregar = useAgregarRfcReceptor();
  const idempotencyKey = useFormIdempotencyKey();
  const [open, setOpen] = useState(false);

  const form = useForm<AgregarRfcReceptorValues>({
    resolver: zodResolver(AgregarRfcReceptorSchema),
    defaultValues: { rfc: '' },
  });

  function onSubmit(values: AgregarRfcReceptorValues) {
    agregar.mutate(
      {
        command: { empresaId, rfc: values.rfc.toUpperCase() },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success(`RFC ${values.rfc.toUpperCase()} agregado.`);
          form.reset({ rfc: '' });
          setOpen(false);
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
            toast.error(error.problem.title);
            return;
          }
          toast.error('Error inesperado al agregar RFC.');
        },
      },
    );
  }

  if (!open) {
    return (
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={() => setOpen(true)}
        className="border-dashed"
      >
        <Plus className="mr-1 h-3 w-3" />
        Agregar RFC
      </Button>
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      className="flex items-end gap-2 rounded-md border border-dashed border-primary p-3"
    >
      <div className="flex-1 space-y-1">
        <Label htmlFor="new-rfc">RFC</Label>
        <Input
          id="new-rfc"
          placeholder="MIL010101AAA"
          autoFocus
          {...form.register('rfc')}
        />
        {form.formState.errors.rfc && (
          <p className="text-xs text-destructive">
            {form.formState.errors.rfc.message}
          </p>
        )}
      </div>
      <Button type="submit" disabled={agregar.isPending} size="sm">
        {agregar.isPending && <Loader2 className="mr-1 h-3 w-3 animate-spin" />}
        Agregar
      </Button>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        onClick={() => {
          form.reset({ rfc: '' });
          setOpen(false);
        }}
      >
        Cancelar
      </Button>
    </form>
  );
}
