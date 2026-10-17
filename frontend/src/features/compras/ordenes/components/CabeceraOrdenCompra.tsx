import { useState } from 'react';
import { useForm, Controller } from 'react-hook-form';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { Badge as BadgeUi } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  AvisoEnLinea,
  DateTimeDisplay,
  Field,
} from '@/components/erp';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { CondicionesPagoSelector } from '@/components/erp/selectors/CondicionesPagoSelector';
import { ProveedorSelector } from '@/components/erp/selectors/ProveedorSelector';
import { UsoPrincipalSelector } from '@/components/erp/selectors/UsoPrincipalSelector';
import type { OrdenCompraDetalleResponse } from '@/features/compras/ordenes/api/types';
import {
  useActualizarCabeceraOc,
  type ActualizarCabeceraOcCommand,
} from '@/features/compras/ordenes/api/useActualizarCabeceraOc';
import { accionEditarCabecera } from '@/features/compras/ordenes/lib/acciones-disponibles';
import { handleOcMutationError } from '@/features/compras/ordenes/lib/handle-conflict';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';
import { nombreUsuarioOGuion } from '@/lib/nombre-usuario';

/**
 * <c>&lt;CabeceraOrdenCompra/&gt;</c> — bloque de información de la
 * cabecera de la OC en el tab "Información" del detalle P3. Mismo
 * patrón que <c>&lt;CabeceraRequisicion/&gt;</c>: <c>dl</c> de pares
 * label/valor agrupados, banner para campos especiales (motivo
 * sin RQ, motivo cancelación, motivo rechazo).
 *
 * <para>UX-F0-01: los campos que siguen en TBD (OC generada desde RQ) se
 * muestran como «Por capturar» con un aviso, y proveedor, condiciones de
 * pago y uso principal se editan aquí (Borrador/Rechazada con permiso de
 * crear OC) vía <c>PATCH /api/v1/compras/ordenes/{id}</c>.</para>
 */
export interface CabeceraOrdenCompraProps {
  oc: OrdenCompraDetalleResponse;
  /** Mapa de id → nombre para resolver proveedor, sucursal, almacén,
   * comprador, condiciones de pago, etc. Si el id no se resuelve,
   * fallback al id raw para no esconder data. */
  resolverNombre: (id: string | null | undefined) => string;
}

export function CabeceraOrdenCompra({
  oc,
  resolverNombre,
}: CabeceraOrdenCompraProps) {
  const permisos = useAuthStore((s) => s.permisos);
  const puedeEditar = accionEditarCabecera(oc, permisos).habilitada;
  const [editando, setEditando] = useState(false);
  const pendientes = oc.camposPorCapturar ?? [];
  const porCapturar = (c: 'proveedor' | 'condicionesPago' | 'usoPrincipal') =>
    pendientes.includes(c);
  const valor = (c: 'proveedor' | 'condicionesPago' | 'usoPrincipal', v: React.ReactNode) =>
    porCapturar(c) ? <BadgeUi variant="warning">Por capturar</BadgeUi> : v;

  return (
    <section
      className="space-y-4 rounded-md border bg-card p-4"
      data-component="cabecera-orden-compra"
    >
      {puedeEditar && !editando && (
        <div className="flex justify-end">
          <Button variant="secondary" onClick={() => setEditando(true)}>
            Editar cabecera
          </Button>
        </div>
      )}

      {pendientes.length > 0 && (
        <AvisoEnLinea tono="advertencia">
          {`Falta ${listarFaltantes(pendientes)}. Captúralos para poder enviar la OC a autorización.`}
          {!puedeEditar &&
            ' Pide a un comprador con permiso de crear OC que los capture.'}
        </AvisoEnLinea>
      )}

      {editando && (
        <FormularioCabecera oc={oc} onClose={() => setEditando(false)} />
      )}

      <dl className="grid grid-cols-1 gap-x-6 gap-y-2 text-sm @lg:grid-cols-2 @3xl:grid-cols-3">
        <Item label="Emitida">
          <DateTimeDisplay value={oc.fechaDocumento} variant="long" />
        </Item>
        <Item label="Proveedor">
          {valor(
            'proveedor',
            oc.proveedorRazonSocial ??
              oc.proveedorClave ??
              resolverNombre(oc.proveedorId),
          )}
        </Item>
        <Item label="Comprador titular">
          {nombreUsuarioOGuion(
            oc.compradorNombre ?? resolverNombre(oc.compradorTitularId),
          )}
        </Item>
        <Item label="Encargado de compras">
          {nombreUsuarioOGuion(resolverNombre(oc.encargadoComprasId))}
        </Item>
        <Item label="Sucursal destino">
          {resolverNombre(oc.sucursalDestinoId)}
        </Item>
        <Item label="Condiciones de pago">
          {valor('condicionesPago', resolverNombre(oc.condicionesPagoId))}
        </Item>
        <Item label="Uso principal">
          {valor('usoPrincipal', resolverNombre(oc.usoPrincipalId))}
        </Item>
        <Item label="Moneda" mono>
          {oc.moneda}
          {oc.tipoCambio != null ? ` @ ${oc.tipoCambio}` : ''}
        </Item>
        <Item label="Fecha contabilización">
          <DateTimeDisplay value={oc.fechaContabilizacion} />
        </Item>
        <Item label="Fecha entrega esperada">
          <DateTimeDisplay value={oc.fechaEntregaEsperada} />
        </Item>
        <Item label="Versión" mono>
          v{oc.version}
        </Item>
      </dl>

      {/* Banners de naturalezas especiales — visibles solo cuando aplican. */}
      <div className="flex flex-wrap gap-1.5 text-xs">
        {oc.esImportacion && (
          <Badge tono="azul" data-flag="es-importacion">
            Importación
          </Badge>
        )}
        {oc.sinRequisicionPrevia && (
          <Badge tono="ambar" data-flag="sin-rq-previa">
            Sin RQ previa
          </Badge>
        )}

        {oc.ocOrigenId != null && (
          <Badge tono="violeta" data-flag="duplicada">
            Duplicada de OC origen
          </Badge>
        )}
      </div>

      {oc.obra && <p className="mt-2 text-sm text-ink">Obra: {oc.obra}</p>}
      {oc.observaciones && (
        <div className="rounded-md border bg-muted/30 p-3 text-sm">
          <p className="mb-1 text-xs font-medium uppercase tracking-wide text-muted-foreground">
            Observaciones
          </p>
          <p className="whitespace-pre-wrap">{oc.observaciones}</p>
        </div>
      )}

      {oc.motivoSinRequisicion && (
        <div className="rounded-md border border-warning bg-warning-bg p-3 text-sm">
          <p className="mb-1 text-xs font-medium uppercase tracking-wide text-warning-fg">
            Motivo sin requisición previa
          </p>
          <p className="whitespace-pre-wrap text-warning-fg">
            {oc.motivoSinRequisicion}
          </p>
        </div>
      )}

      {(oc.motivoRechazoTexto != null || oc.motivoRechazoId != null) && (
        <div className="rounded-md border border-danger bg-danger-bg p-3 text-sm">
          <p className="mb-1 text-xs font-medium uppercase tracking-wide text-danger-fg">
            Motivo de rechazo
          </p>
          <p className="whitespace-pre-wrap text-danger-fg">
            {oc.motivoRechazoTexto ?? `(motivo id: ${oc.motivoRechazoId})`}
          </p>
        </div>
      )}

      {oc.motivoCancelacion && (
        <div className="rounded-md border border-danger bg-danger-bg p-3 text-sm">
          <p className="mb-1 text-xs font-medium uppercase tracking-wide text-danger-fg">
            Motivo de cancelación
          </p>
          <p className="whitespace-pre-wrap text-danger-fg">
            {oc.motivoCancelacion}
          </p>
        </div>
      )}
    </section>
  );
}

const ETIQUETA_CAMPO = {
  proveedor: 'proveedor',
  condicionesPago: 'condiciones de pago',
  usoPrincipal: 'uso principal',
} as const;

function listarFaltantes(campos: readonly string[]): string {
  const nombres = campos.map(
    (c) => ETIQUETA_CAMPO[c as keyof typeof ETIQUETA_CAMPO] ?? c,
  );
  return nombres.length > 1
    ? `${nombres.slice(0, -1).join(', ')} y ${nombres[nombres.length - 1]}`
    : nombres[0];
}

interface CabeceraValues {
  proveedorId: string;
  condicionesPagoId: string;
  usoPrincipalId: string;
}

/** Formulario de edición de proveedor, condiciones de pago y uso principal. */
function FormularioCabecera({
  oc,
  onClose,
}: {
  oc: OrdenCompraDetalleResponse;
  onClose: () => void;
}) {
  const pendientes = oc.camposPorCapturar ?? [];
  // Un campo en TBD arranca vacío: el id sentinel no es un valor elegible.
  const inicial: CabeceraValues = {
    proveedorId: pendientes.includes('proveedor') ? '' : oc.proveedorId,
    condicionesPagoId: pendientes.includes('condicionesPago') ? '' : oc.condicionesPagoId,
    usoPrincipalId: pendientes.includes('usoPrincipal') ? '' : oc.usoPrincipalId,
  };
  const form = useForm<CabeceraValues>({ defaultValues: inicial });
  const idempotencyKey = useFormIdempotencyKey();
  const mutation = useActualizarCabeceraOc();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();

  function onSubmit(values: CabeceraValues) {
    const command: ActualizarCabeceraOcCommand = {};
    if (values.proveedorId && values.proveedorId !== inicial.proveedorId)
      command.proveedorId = values.proveedorId;
    if (values.condicionesPagoId && values.condicionesPagoId !== inicial.condicionesPagoId)
      command.condicionesPagoId = values.condicionesPagoId;
    if (values.usoPrincipalId && values.usoPrincipalId !== inicial.usoPrincipalId)
      command.usoPrincipalId = values.usoPrincipalId;
    if (Object.keys(command).length === 0) {
      onClose();
      return;
    }
    mutation.mutate(
      { ordenCompraId: oc.id, command, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Cabecera actualizada.');
          onClose();
        },
        onError: (err) => {
          if (
            handleOcMutationError(err, {
              ordenCompraId: oc.id,
              conflictDialog,
              queryClient,
            })
          ) {
            onClose();
            return;
          }
          if (
            esApiError(err) &&
            applyServerErrors(
              form as unknown as Parameters<typeof applyServerErrors>[0],
              err,
              {
                codigos: {
                  PROVEEDOR_NO_ENCONTRADO: { campo: 'proveedorId' },
                  PROVEEDOR_NO_UTILIZABLE: { campo: 'proveedorId' },
                },
              },
            ) === true
          ) {
            return;
          }
          toast.error(
            esApiError(err)
              ? err.problem.title
              : 'Error inesperado al guardar la cabecera.',
          );
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      aria-label="Editar cabecera"
      className="space-y-3 rounded-md border bg-muted/30 p-3"
    >
      <div className="grid grid-cols-1 gap-3 @lg:grid-cols-3">
        <Controller
          control={form.control}
          name="proveedorId"
          render={({ field, fieldState }) => (
            <Field label="Proveedor" error={fieldState.error?.message}>
              {(c) => (
                <div role="group" aria-label="Proveedor" id={c.id}>
                  <ProveedorSelector
                    value={field.value || null}
                    onChange={(v) => field.onChange(v ?? '')}
                    initialLabel={oc.proveedorRazonSocial ?? undefined}
                  />
                </div>
              )}
            </Field>
          )}
        />
        <Controller
          control={form.control}
          name="condicionesPagoId"
          render={({ field, fieldState }) => (
            <Field label="Condiciones de pago" error={fieldState.error?.message}>
              {(c) => (
                <div role="group" aria-label="Condiciones de pago" id={c.id}>
                  <CondicionesPagoSelector
                    value={field.value || null}
                    onChange={(v) => field.onChange(v ?? '')}
                  />
                </div>
              )}
            </Field>
          )}
        />
        <Controller
          control={form.control}
          name="usoPrincipalId"
          render={({ field, fieldState }) => (
            <Field label="Uso principal" error={fieldState.error?.message}>
              {(c) => (
                <div role="group" aria-label="Uso principal" id={c.id}>
                  <UsoPrincipalSelector
                    value={field.value || null}
                    onChange={(v) => field.onChange(v ?? '')}
                  />
                </div>
              )}
            </Field>
          )}
        />
      </div>
      <div className="flex justify-end gap-2">
        <Button
          type="button"
          variant="ghost"
          onClick={onClose}
          disabled={mutation.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={mutation.isPending}>
          {mutation.isPending ? 'Guardando…' : 'Guardar cabecera'}
        </Button>
      </div>
    </form>
  );
}

function Item({
  label,
  children,
  mono,
}: {
  label: string;
  children: React.ReactNode;
  mono?: boolean;
}) {
  return (
    <div className="grid min-w-0 grid-cols-[max-content_1fr] gap-x-2">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className={mono ? 'min-w-0 break-words font-mono' : 'min-w-0 break-words'}>
        {children}
      </dd>
    </div>
  );
}

function Badge({
  tono,
  children,
  ...rest
}: {
  tono: 'azul' | 'ambar' | 'violeta';
  children: React.ReactNode;
} & React.HTMLAttributes<HTMLSpanElement>) {
  const cls =
    tono === 'azul'
      ? 'bg-info-bg text-info-fg ring-info'
      : tono === 'ambar'
        ? 'bg-warning-bg text-warning-fg ring-warning'
        : 'bg-neutral-bg text-neutral-fg ring-line';
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 font-medium ring-1 ring-inset ${cls}`}
      {...rest}
    >
      {children}
    </span>
  );
}
