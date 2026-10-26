import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormGrid } from '@/components/erp/forms/FormGrid';
import { useId, useState } from 'react';
import { Pencil, Save, X, Info } from 'lucide-react';
import { toast } from 'sonner';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useQueryClient, type QueryClient } from '@tanstack/react-query';
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
import { IncotermSelector } from '@/components/erp';
import {
  applyServerErrors,
  codigoSoporte,
  esApiError,
  useFormIdempotencyKey,
  type ApiError,
  type FormConSetError,
} from '@/lib/api';
import {
  accionEditarInfoImportacion,
  accionEditarNumeroPedimentoImportacion,
} from '@/features/compras/ordenes/lib/acciones-disponibles';
import {
  TipoOperacionComercioExterior,
  TipoTransporte,
  type OrdenCompraDetalleResponse,
} from '@/features/compras/ordenes/api/types';
import {
  useActualizarInformacionImportacion,
  type ActualizarInformacionImportacionCommand,
} from '@/features/compras/ordenes/api/useActualizarInformacionImportacion';
import { useActualizarNumeroPedimento } from '@/features/compras/ordenes/api/useActualizarNumeroPedimento';
import {
  NumeroPedimentoSchema,
  type NumeroPedimentoValues,
} from '@/features/compras/ordenes/schemas/comercio-exterior';
import {
  ETIQUETA_TIPO_OPERACION,
  ETIQUETA_TIPO_TRANSPORTE,
} from '@/features/compras/ordenes/lib/comercio-exterior';
import { useAuthStore } from '@/lib/auth/auth-store';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { handleOcMutationError } from '@/features/compras/ordenes/lib/handle-conflict';
import { useIncoterms } from '@/features/catalogos/api';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;InformacionImportacionForm/&gt;</c> — sub-tab "Importación"
 * del Tab "Información" del detalle de OC (comercio exterior). Solo se
 * renderiza si <c>oc.esImportacion === true</c>.
 *
 * <para>Dos bloques:</para>
 * <list>
 *   <item><b>Información de importación/exportación</b>: tipo de
 *   operación, incoterm, país, contenedor, ruta, semana, tipo de
 *   transporte, BL/AWB, agente aduanal/naviera, puerto/aduana de entrada,
 *   ETA y certificado de origen. Editable en <c>Borrador</c>/<c>Rechazada</c>
 *   (<c>PATCH /informacion-importacion</c>).</item>
 *   <item><b>Pedimento y embarque</b>: pedimento, BL/AWB, ETA y agente.
 *   Editable post-autorización sin re-auth (<c>PATCH /numero-pedimento</c>,
 *   permiso <c>compras.ordenes.logistica</c>). El pedimento NO se envía en
 *   el form principal: el backend lo ignora ahí.</item>
 * </list>
 *
 * <para>// PLATFORM-TODO(&lt;F1-COM-06&gt;): supuesto Vilo, pendiente lista
 * oficial de Millet.</para>
 */
export interface InformacionImportacionFormProps {
  oc: OrdenCompraDetalleResponse;
}

/** Valores del form principal. Selects en string ('' = sin definir). */
interface ImportacionValues {
  incotermId: string;
  paisOrigen: string;
  numeroContenedor: string;
  codigoRuta: string;
  semanaEmbarque: string;
  tipoOperacion: string;
  tipoTransporte: string;
  numeroBl: string;
  agenteAduanalNaviera: string;
  puertoAduanaEntrada: string;
  fechaEstimadaArribo: string;
  certificadoOrigen: boolean;
}

export function InformacionImportacionForm({ oc }: InformacionImportacionFormProps) {
  const permisos = useAuthStore((s) => s.permisos);
  const accion = accionEditarInfoImportacion(oc, permisos);
  const incotermsQuery = useIncoterms();
  const [editing, setEditing] = useState(false);

  if (!oc.esImportacion) {
    return null;
  }

  const incotermResolved = oc.infoImportIncotermId
    ? incotermsQuery.data?.find((i) => i.id === oc.infoImportIncotermId)
    : undefined;

  if (!editing) {
    return (
      <div className="space-y-3">
        <ReadView
          oc={oc}
          incotermResolved={incotermResolved}
          canEdit={accion.visible && accion.habilitada}
          onEditar={() => setEditing(true)}
        />
        <PedimentoImportacionControl oc={oc} />
        {(oc.recepcionesImportacion ?? []).map((recepcion, index) => (
          <section key={recepcion.recepcionId} className="rounded-lg bg-surface-card p-4 shadow-card-flat">
            <h3 className="mb-3 text-sm font-semibold">Documentos de recepción {index + 1}</h3>
            <dl className="grid gap-3 text-sm md:grid-cols-2">
              <Field label="Número de pedimento">{recepcion.numeroPedimento}</Field>
              <Field label="BL o guía aérea">{recepcion.numeroBl}</Field>
              <Field label="Número de contenedor">{recepcion.numeroContenedor}</Field>
              <Field label="Aduana">{recepcion.aduana}</Field>
              <Field label="Clave de pedimento">{recepcion.clavePedimento}</Field>
              {recepcion.partidas.map((partida, posicion) => <Field key={partida.lineaOcId} label={`Fracción arancelaria · partida ${posicion + 1}`}>{partida.fraccionArancelaria}</Field>)}
            </dl>
          </section>
        ))}
      </div>
    );
  }

  return <EditView oc={oc} onCancel={() => setEditing(false)} onSaved={() => setEditing(false)} />;
}

// ─── Read mode ────────────────────────────────────────────────────

function ReadView({
  oc,
  incotermResolved,
  canEdit,
  onEditar,
}: {
  oc: OrdenCompraDetalleResponse;
  incotermResolved: { codigo: string; nombre: string } | undefined;
  canEdit: boolean;
  onEditar: () => void;
}) {
  const incotermLabel = incotermResolved
    ? `${incotermResolved.codigo} · ${incotermResolved.nombre}`
    : null;

  return (
    <div
      className="rounded-md border bg-surface-card p-4 space-y-3"
      data-component="info-importacion-read"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold tracking-tight">Información de importación</h3>
        {canEdit && (
          <Button
            size="sm"
            variant="outline"
            onClick={onEditar}
            data-action="editar-info-importacion"
          >
            <Pencil className="mr-1 h-3.5 w-3.5" />
            Editar
          </Button>
        )}
      </div>

      <dl className="grid gap-3 text-sm md:grid-cols-2">
        <Field label="Tipo de operación">
          {oc.infoImportTipoOperacion != null ? (
            ETIQUETA_TIPO_OPERACION[oc.infoImportTipoOperacion]
          ) : (
            <Vacio />
          )}
        </Field>
        <Field label="Incoterm">{incotermLabel ?? <Vacio />}</Field>
        <Field label="País de origen">{oc.infoImportPaisOrigen ?? <Vacio />}</Field>
        <Field label="Número de contenedor">{oc.infoImportNumeroContenedor ?? <Vacio />}</Field>
        <Field label="Código de ruta">{oc.infoImportCodigoRuta ?? <Vacio />}</Field>
        <Field label="Semana de embarque">{oc.infoImportSemanaEmbarque ?? <Vacio />}</Field>
        <Field label="Tipo de transporte">
          {oc.infoImportTipoTransporte != null ? (
            ETIQUETA_TIPO_TRANSPORTE[oc.infoImportTipoTransporte]
          ) : (
            <Vacio />
          )}
        </Field>
        <Field label="Puerto / aduana de entrada">
          {oc.infoImportPuertoAduanaEntrada ?? <Vacio />}
        </Field>
        <Field label="Certificado de origen">{oc.infoImportCertificadoOrigen ? 'Sí' : 'No'}</Field>
      </dl>
    </div>
  );
}

// ─── Pedimento y embarque (post-autorización) ─────────────────────

/**
 * Tarjeta "Pedimento y embarque": muestra pedimento, BL/AWB, ETA y agente
 * y, si la matriz lo permite, abre el mini-form que llama a
 * <c>PATCH /numero-pedimento</c>. Es el único control para estos campos
 * después de autorizar.
 */
function PedimentoImportacionControl({ oc }: { oc: OrdenCompraDetalleResponse }) {
  const permisos = useAuthStore((s) => s.permisos);
  const accion = accionEditarNumeroPedimentoImportacion(oc, permisos);
  const [editing, setEditing] = useState(false);

  if (editing) {
    return (
      <PedimentoEditView
        oc={oc}
        onCancel={() => setEditing(false)}
        onSaved={() => setEditing(false)}
      />
    );
  }

  const puedeEditar = accion.visible && accion.habilitada;

  return (
    <div
      className="rounded-md border bg-surface-card p-4 space-y-3"
      data-component="pedimento-importacion-read"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold tracking-tight">Pedimento y embarque</h3>
        {puedeEditar && (
          <Button
            size="sm"
            variant="outline"
            onClick={() => setEditing(true)}
            data-action="editar-numero-pedimento"
          >
            <Pencil className="mr-1 h-3.5 w-3.5" />
            Editar
          </Button>
        )}
      </div>

      <dl className="grid gap-3 text-sm md:grid-cols-2">
        <Field label="Número de pedimento">{oc.infoImportNumeroPedimento ?? <Vacio />}</Field>
        <Field label="BL / AWB">{oc.infoImportNumeroBl ?? <Vacio />}</Field>
        <Field label="Fecha estimada de arribo (ETA)">
          {oc.infoImportFechaEstimadaArribo ?? <Vacio />}
        </Field>
        <Field label="Agente aduanal / naviera">
          {oc.infoImportAgenteAduanalNaviera ?? <Vacio />}
        </Field>
      </dl>
    </div>
  );
}

function PedimentoEditView({
  oc,
  onCancel,
  onSaved,
}: {
  oc: OrdenCompraDetalleResponse;
  onCancel: () => void;
  onSaved: () => void;
}) {
  const uid = useId();
  const idempotencyKey = useFormIdempotencyKey();
  const mutation = useActualizarNumeroPedimento();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();

  const form = useForm<NumeroPedimentoValues>({
    resolver: zodResolver(NumeroPedimentoSchema),
    defaultValues: {
      numeroPedimento: oc.infoImportNumeroPedimento ?? '',
      numeroBl: oc.infoImportNumeroBl ?? '',
      fechaEstimadaArribo: oc.infoImportFechaEstimadaArribo ?? '',
      agenteAduanalNaviera: oc.infoImportAgenteAduanalNaviera ?? '',
    },
  });

  function onSubmit(values: NumeroPedimentoValues) {
    // El endpoint reemplaza los cuatro campos: se envían siempre los cuatro.
    mutation.mutate(
      {
        ordenCompraId: oc.id,
        command: {
          numeroPedimento: nullIfEmpty(values.numeroPedimento),
          numeroBl: nullIfEmpty(values.numeroBl),
          fechaEstimadaArribo: nullIfEmpty(values.fechaEstimadaArribo),
          agenteAduanalNaviera: nullIfEmpty(values.agenteAduanalNaviera),
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Pedimento y embarque actualizados.');
          onSaved();
        },
        onError: (err) =>
          manejarErrorGuardado(err, {
            ordenCompraId: oc.id,
            conflictDialog,
            queryClient,
            form: form as unknown as FormConSetError,
            onCancel,
            mensajeInesperado: 'Error inesperado al guardar el pedimento.',
          }),
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      onKeyDown={(e) => {
        if (e.key === 'Escape' && !mutation.isPending) {
          e.preventDefault();
          onCancel();
        }
      }}
      className={cn('space-y-3 rounded-md border p-3', 'border-warning bg-warning-bg/40')}
      aria-label="Editar pedimento y embarque"
      data-component="pedimento-importacion-edit"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold tracking-tight">Pedimento y embarque</h3>
        <div className="flex items-center gap-2">
          <Button
            type="button"
            size="sm"
            variant="ghost"
            onClick={onCancel}
            disabled={mutation.isPending}
          >
            <X className="mr-1 h-4 w-4" />
            Cancelar
          </Button>
          <Button
            type="submit"
            size="sm"
            disabled={mutation.isPending}
            data-action="submit-numero-pedimento"
          >
            <Save className="mr-1 h-4 w-4" />
            {mutation.isPending ? 'Guardando…' : 'Guardar cambios'}
          </Button>
        </div>
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <FieldGroup label="Número de pedimento" htmlFor={`${uid}-pedimento`}>
          <Input
            id={`${uid}-pedimento`}
            type="text"
            maxLength={40}
            {...form.register('numeroPedimento')}
          />
        </FieldGroup>
        <FieldGroup
          label="BL / AWB"
          htmlFor={`${uid}-bl`}
          ayuda="Conocimiento de embarque (B/L) o guía aérea (AWB)."
        >
          <Input id={`${uid}-bl`} type="text" maxLength={50} {...form.register('numeroBl')} />
        </FieldGroup>
        <FieldGroup
          label="Fecha estimada de arribo (ETA)"
          htmlFor={`${uid}-eta`}
          error={form.formState.errors.fechaEstimadaArribo?.message}
        >
          <Input id={`${uid}-eta`} type="date" {...form.register('fechaEstimadaArribo')} />
        </FieldGroup>
        <FieldGroup
          label="Agente aduanal / naviera"
          htmlFor={`${uid}-agente`}
          error={form.formState.errors.agenteAduanalNaviera?.message}
        >
          <Input
            id={`${uid}-agente`}
            type="text"
            maxLength={120}
            {...form.register('agenteAduanalNaviera')}
          />
        </FieldGroup>
      </div>
    </form>
  );
}

// ─── Edit mode ────────────────────────────────────────────────────

function EditView({
  oc,
  onCancel,
  onSaved,
}: {
  oc: OrdenCompraDetalleResponse;
  onCancel: () => void;
  onSaved: () => void;
}) {
  const uid = useId();
  const idempotencyKey = useFormIdempotencyKey();
  const mutation = useActualizarInformacionImportacion();
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();

  const form = useForm<ImportacionValues>({
    defaultValues: {
      incotermId: oc.infoImportIncotermId ?? '',
      paisOrigen: oc.infoImportPaisOrigen ?? '',
      numeroContenedor: oc.infoImportNumeroContenedor ?? '',
      codigoRuta: oc.infoImportCodigoRuta ?? '',
      semanaEmbarque: oc.infoImportSemanaEmbarque ?? '',
      tipoOperacion: oc.infoImportTipoOperacion != null ? String(oc.infoImportTipoOperacion) : '',
      tipoTransporte:
        oc.infoImportTipoTransporte != null ? String(oc.infoImportTipoTransporte) : '',
      numeroBl: oc.infoImportNumeroBl ?? '',
      agenteAduanalNaviera: oc.infoImportAgenteAduanalNaviera ?? '',
      puertoAduanaEntrada: oc.infoImportPuertoAduanaEntrada ?? '',
      fechaEstimadaArribo: oc.infoImportFechaEstimadaArribo ?? '',
      certificadoOrigen: oc.infoImportCertificadoOrigen,
    },
  });

  function onSubmit(values: ImportacionValues) {
    const command: ActualizarInformacionImportacionCommand = {
      incotermId: nullIfEmpty(values.incotermId),
      paisOrigen: nullIfEmpty(values.paisOrigen),
      numeroContenedor: nullIfEmpty(values.numeroContenedor),
      codigoRuta: nullIfEmpty(values.codigoRuta),
      semanaEmbarque: nullIfEmpty(values.semanaEmbarque),
      tipoOperacion: enumDesdeForm<TipoOperacionComercioExterior>(values.tipoOperacion),
      tipoTransporte: enumDesdeForm<TipoTransporte>(values.tipoTransporte),
      numeroBl: nullIfEmpty(values.numeroBl),
      agenteAduanalNaviera: nullIfEmpty(values.agenteAduanalNaviera),
      puertoAduanaEntrada: nullIfEmpty(values.puertoAduanaEntrada),
      fechaEstimadaArribo: nullIfEmpty(values.fechaEstimadaArribo),
      certificadoOrigen: values.certificadoOrigen,
    };
    mutation.mutate(
      { ordenCompraId: oc.id, command, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Información de importación actualizada.');
          onSaved();
        },
        onError: (err) =>
          manejarErrorGuardado(err, {
            ordenCompraId: oc.id,
            conflictDialog,
            queryClient,
            form: form as unknown as FormConSetError,
            onCancel,
            mensajeInesperado: 'Error inesperado al guardar la importación.',
          }),
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      onKeyDown={(e) => {
        if (e.key === 'Escape' && !mutation.isPending) {
          e.preventDefault();
          onCancel();
        }
      }}
      className={cn('space-y-3 rounded-md border p-3', 'border-warning bg-warning-bg/40')}
      aria-label="Editar información de importación"
      data-component="info-importacion-edit"
    >
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold tracking-tight">Información de importación</h3>
        <div className="flex items-center gap-2">
          <Button
            type="button"
            size="sm"
            variant="ghost"
            onClick={onCancel}
            disabled={mutation.isPending}
          >
            <X className="mr-1 h-4 w-4" />
            Cancelar
          </Button>
          <Button
            type="submit"
            size="sm"
            disabled={mutation.isPending}
            data-action="submit-info-importacion"
          >
            <Save className="mr-1 h-4 w-4" />
            {mutation.isPending ? 'Guardando…' : 'Guardar cambios'}
          </Button>
        </div>
      </div>

      <FormGrid className="">
        <FormCampo span={6}>
          <FieldGroup label="Tipo de operación" htmlFor={`${uid}-tipo-operacion`}>
            <Controller
              name="tipoOperacion"
              control={form.control}
              render={({ field }) => (
                <Select
                  value={field.value || '__none__'}
                  onValueChange={(v) => field.onChange(v === '__none__' ? '' : v)}
                >
                  <SelectTrigger id={`${uid}-tipo-operacion`}>
                    <SelectValue placeholder="Sin definir" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__none__">Sin definir</SelectItem>
                    <SelectItem value={String(TipoOperacionComercioExterior.Importacion)}>
                      {ETIQUETA_TIPO_OPERACION[TipoOperacionComercioExterior.Importacion]}
                    </SelectItem>
                    <SelectItem value={String(TipoOperacionComercioExterior.Exportacion)}>
                      {ETIQUETA_TIPO_OPERACION[TipoOperacionComercioExterior.Exportacion]}
                    </SelectItem>
                  </SelectContent>
                </Select>
              )}
            />
          </FieldGroup>
        </FormCampo>
        <FormCampo span={6}>
          <FieldGroup label="Incoterm">
            <Controller
              name="incotermId"
              control={form.control}
              render={({ field }) => (
                <IncotermSelector
                  value={field.value || null}
                  onChange={(v) => field.onChange(v ?? '')}
                />
              )}
            />
          </FieldGroup>
        </FormCampo>
        <FormCampo span={6}>
          <FieldGroup label="País de origen" htmlFor={`${uid}-pais`}>
            <Input
              id={`${uid}-pais`}
              type="text"
              maxLength={80}
              placeholder="MX, US, CN…"
              {...form.register('paisOrigen')}
            />
          </FieldGroup>
        </FormCampo>
        <FormCampo span={6}>
          <FieldGroup label="Número de contenedor" htmlFor={`${uid}-contenedor`}>
            <Input
              id={`${uid}-contenedor`}
              type="text"
              maxLength={20}
              placeholder="ABCU1234567"
              {...form.register('numeroContenedor')}
            />
          </FieldGroup>
        </FormCampo>
        <FormCampo span={6}>
          <FieldGroup label="Código de ruta" htmlFor={`${uid}-ruta`}>
            <Input id={`${uid}-ruta`} type="text" maxLength={40} {...form.register('codigoRuta')} />
          </FieldGroup>
        </FormCampo>
        <FormCampo span={6}>
          <FieldGroup label="Semana de embarque" htmlFor={`${uid}-semana`}>
            <Input
              id={`${uid}-semana`}
              type="text"
              maxLength={20}
              placeholder="2026-W12"
              {...form.register('semanaEmbarque')}
            />
          </FieldGroup>
        </FormCampo>
        <FormCampo span={6}>
          <FieldGroup label="Tipo de transporte" htmlFor={`${uid}-transporte`}>
            <Controller
              name="tipoTransporte"
              control={form.control}
              render={({ field }) => (
                <Select
                  value={field.value || '__none__'}
                  onValueChange={(v) => field.onChange(v === '__none__' ? '' : v)}
                >
                  <SelectTrigger id={`${uid}-transporte`}>
                    <SelectValue placeholder="Sin definir" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__none__">Sin definir</SelectItem>
                    <SelectItem value={String(TipoTransporte.Maritimo)}>
                      {ETIQUETA_TIPO_TRANSPORTE[TipoTransporte.Maritimo]}
                    </SelectItem>
                    <SelectItem value={String(TipoTransporte.Aereo)}>
                      {ETIQUETA_TIPO_TRANSPORTE[TipoTransporte.Aereo]}
                    </SelectItem>
                    <SelectItem value={String(TipoTransporte.Terrestre)}>
                      {ETIQUETA_TIPO_TRANSPORTE[TipoTransporte.Terrestre]}
                    </SelectItem>
                  </SelectContent>
                </Select>
              )}
            />
          </FieldGroup>
        </FormCampo>
        <FormCampo span={6}>
          <FieldGroup
            label="BL / AWB"
            htmlFor={`${uid}-bl`}
            ayuda="Conocimiento de embarque (B/L) o guía aérea (AWB)."
          >
            <Input id={`${uid}-bl`} type="text" maxLength={50} {...form.register('numeroBl')} />
          </FieldGroup>
        </FormCampo>
        <FormCampo span={6}>
          <FieldGroup label="Agente aduanal / naviera" htmlFor={`${uid}-agente`}>
            <Input
              id={`${uid}-agente`}
              type="text"
              maxLength={120}
              {...form.register('agenteAduanalNaviera')}
            />
          </FieldGroup>
        </FormCampo>
        <FormCampo span={6}>
          <FieldGroup label="Puerto / aduana de entrada" htmlFor={`${uid}-puerto`}>
            <Input
              id={`${uid}-puerto`}
              type="text"
              maxLength={120}
              {...form.register('puertoAduanaEntrada')}
            />
          </FieldGroup>
        </FormCampo>
        <FormCampo span={6}>
          <FieldGroup label="Fecha estimada de arribo (ETA)" htmlFor={`${uid}-eta`}>
            <Input id={`${uid}-eta`} type="date" {...form.register('fechaEstimadaArribo')} />
          </FieldGroup>
        </FormCampo>
        <FormCampo span={12}>
          <FieldGroup
            label="Certificado de origen"
            htmlFor={`${uid}-certificado`}
            ayuda="Marca si el proveedor entrega certificado de origen para preferencias arancelarias."
          >
            <Controller
              name="certificadoOrigen"
              control={form.control}
              render={({ field }) => (
                <div className="flex h-9 items-center">
                  <Checkbox
                    id={`${uid}-certificado`}
                    checked={field.value}
                    onCheckedChange={(v) => field.onChange(v === true)}
                  />
                </div>
              )}
            />
          </FieldGroup>
        </FormCampo>
      </FormGrid>

      <p className="flex items-start gap-2 text-xs text-ink-muted">
        <Info className="h-3.5 w-3.5 mt-0.5 shrink-0" />
        <span>
          El pedimento se captura en la tarjeta «Pedimento y embarque», que sigue editable después
          de autorizar sin re-firmar.
        </span>
      </p>
    </form>
  );
}

// ─── Helpers ──────────────────────────────────────────────────────

function nullIfEmpty(v: string): string | null {
  const trimmed = v.trim();
  return trimmed === '' ? null : trimmed;
}

/** '' = sin definir (null = no tocar en el PATCH); si no, el valor numérico del enum. */
function enumDesdeForm<T extends number>(v: string): T | null {
  return v === '' ? null : (Number(v) as T);
}

function descripcionError(err: ApiError): string | undefined {
  const partes = [err.problem.detail, codigoSoporte(err)].filter((p): p is string => Boolean(p));
  return partes.length > 0 ? partes.join(' · ') : undefined;
}

/**
 * Manejo común de error al guardar: 409 → ConflictDialog (cierra el form);
 * error de API → errores por campo si los hay + toast con el título, el
 * detalle y el traceId; cualquier otro → toast genérico.
 */
function manejarErrorGuardado(
  err: unknown,
  ctx: {
    ordenCompraId: string;
    conflictDialog: ReturnType<typeof useConflictDialog>;
    queryClient: QueryClient;
    form: FormConSetError;
    onCancel: () => void;
    mensajeInesperado: string;
  },
) {
  if (
    handleOcMutationError(err, {
      ordenCompraId: ctx.ordenCompraId,
      conflictDialog: ctx.conflictDialog,
      queryClient: ctx.queryClient,
    })
  ) {
    ctx.onCancel();
    return;
  }
  if (esApiError(err)) {
    applyServerErrors(ctx.form, err);
    toast.error(err.problem.title, { description: descripcionError(err) });
    return;
  }
  toast.error(ctx.mensajeInesperado);
}

function Vacio() {
  return <span className="text-ink-muted">—</span>;
}

function Field({
  label,
  className,
  children,
}: {
  label: string;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <div className={cn('space-y-0.5', className)}>
      <dt className="text-xs font-medium text-ink-muted">{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}

function FieldGroup({
  label,
  htmlFor,
  ayuda,
  error,
  children,
}: {
  label: string;
  htmlFor?: string;
  ayuda?: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <label htmlFor={htmlFor} className="text-xs font-medium text-ink-muted">
        {label}
      </label>
      {children}
      {ayuda && <p className="text-xs text-ink-muted">{ayuda}</p>}
      {error != null && (
        <p role="alert" className="text-xs text-danger-fg">
          {error}
        </p>
      )}
    </div>
  );
}
