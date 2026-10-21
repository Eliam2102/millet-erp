import { ErrorState, SucursalSelector } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { CartaPorteDetalleResponse } from '@/features/facturacion/api/types';
import { useCartaPorte, useSiguienteTramo } from '@/features/facturacion/api/useCartaPorte';
import { ErrorTimbradoAviso } from '@/features/facturacion/components/ErrorTimbradoAviso';
import { IntentosTimbradoPanel } from '@/features/facturacion/components/IntentosTimbradoPanel';
import { OperadorPicker } from '@/features/facturacion/components/OperadorPicker';
import { TimbradoFallidoBanner } from '@/features/facturacion/components/TimbradoFallidoBanner';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import { VehiculoPicker } from '@/features/facturacion/components/VehiculoPicker';
import type { CartaPorteSearch } from '@/features/facturacion/lib/carta-porte-search-schema';
import {
  SiguienteTramoSchema,
  type SiguienteTramoValues,
} from '@/features/facturacion/schemas/carta-porte';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { hoyLocalISO } from '@/lib/datetime';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { Route as RouteIcon, X } from 'lucide-react';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { EstadoFacturacionBadge } from '../components/EstadoFacturacionBadge';

/**
 * <c>Detalle de una Carta Porte</c> (FE-F8). Tramo + vehículo + operador +
 * mercancías. Enlace al tramo previo si la Carta Porte es continuación.
 * La acción "Crear siguiente tramo" llega en FE-F8-PR2.
 */
export function DetalleCartaPorte() {
  const { id } = useParams({ from: '/_app/facturacion/carta-porte/$id' });
  const search = useSearch({ strict: false }) as CartaPorteSearch;
  const query = useCartaPorte(id);

  return (
    <div className="space-y-5">
      {/* Sub-topbar del detalle (§6.5): cerrar vuelve a la bandeja
          preservando los filtros activos. */}
      <div
        className="sticky top-0 z-10 flex items-center justify-end gap-2 border-b bg-surface-page/95 pb-2 backdrop-blur border-line"
        data-print="hidden"
      >
        <Button variant="ghost" size="sm" asChild aria-label="Cerrar detalle">
          <Link to="/facturacion/carta-porte" search={search}>
            <X className="h-4 w-4" />
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la Carta Porte"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-surface-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-surface-muted" />
        </div>
      ) : (
        <Contenido c={query.data} />
      )}
    </div>
  );
}

function Contenido({ c }: { c: NonNullable<ReturnType<typeof useCartaPorte>['data']> }) {
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionCartaPorteEmitir);
  const [siguienteAbierto, setSiguienteAbierto] = useState(false);
  // Solo desde un tramo timbrado tiene sentido crear el siguiente.
  const puedeContinuar = puedeEmitir && c.estado === 'Timbrado';

  return (
    <div className="space-y-5">
      <TimbradoFallidoBanner
        comprobanteId={c.id}
        estado={c.estado}
        errorCodigo={c.timbradoErrorCodigo}
        errorMensaje={c.timbradoErrorMensaje}
      />

      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-3">
            <PageHeader
              mono
              titulo={<>{c.folio}</>}
              descripcion={<>Consulta el traslado y sus mercancías.</>}
              estado={<EstadoFacturacionBadge entidad="cartaPorte" estado={c.estado} />}
            />
            <Badge variant="neutral">Tipo {c.tipo}</Badge>
          </div>
          {c.uuid && <p className="font-mono text-xs text-ink-muted">UUID {c.uuid}</p>}
          {c.cartaPortePreviaId && (
            <p className="text-xs">
              Continuación de{' '}
              <Link
                to="/facturacion/carta-porte/$id"
                params={{ id: c.cartaPortePreviaId }}
                className="text-brand hover:underline"
              >
                tramo previo
              </Link>
            </p>
          )}
        </div>
        {puedeContinuar && (
          <Button
            variant="outline"
            data-print="hidden"
            onClick={() => setSiguienteAbierto((v) => !v)}
          >
            <RouteIcon className="mr-2 h-4 w-4" />
            Crear siguiente tramo
          </Button>
        )}
      </div>

      {siguienteAbierto && (
        <SiguienteTramoForm previa={c} onDone={() => setSiguienteAbierto(false)} />
      )}

      <dl className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat sm:grid-cols-4">
        <Dato label="Origen">{c.origen}</Dato>
        <Dato label="Destino">{c.destino}</Dato>
        <Dato label="Distancia">{c.distanciaKm.toFixed(1)} km</Dato>
        <Dato label="Salida">{new Date(c.fechaSalida).toLocaleString('es-MX')}</Dato>
        <Dato label="Llegada estimada">
          {new Date(c.fechaLlegadaEstimada).toLocaleString('es-MX')}
        </Dato>
        {c.total > 0 && <Dato label="Total">{c.total.toFixed(2)}</Dato>}
      </dl>

      <dl className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat sm:grid-cols-4">
        <div className="rounded-md border px-3 py-2 border-line">
          <h2 className="text-xs font-medium text-ink-muted">Vehículo</h2>
          {c.vehiculo ? (
            <p className="text-sm">
              {c.vehiculo.placa} · {c.vehiculo.configVehicular} · {c.vehiculo.anioModelo}
            </p>
          ) : (
            <p className="text-sm text-ink-muted">—</p>
          )}
        </div>
        <div className="rounded-md border px-3 py-2 border-line">
          <h2 className="text-xs font-medium text-ink-muted">Operador</h2>
          {c.operador ? (
            <p className="text-sm">
              {c.operador.nombre} · {c.operador.rfc} · Lic. {c.operador.numLicencia}
            </p>
          ) : (
            <p className="text-sm text-ink-muted">—</p>
          )}
        </div>
      </dl>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Mercancías ({c.mercancias.length})</h2>
        <div className="overflow-x-auto rounded-md border border-line">
          <Table className="w-full text-sm">
            <TableHeader className="bg-surface-muted/50">
              <TableRow>
                <TableHead className="px-3 py-2 text-left">Descripción</TableHead>
                <TableHead className="px-3 py-2 text-left">Bienes transp.</TableHead>
                <TableHead className="px-3 py-2 text-left">Unidad</TableHead>
                <TableHead className="px-3 py-2 text-right tabular-nums">Cantidad</TableHead>
                <TableHead className="px-3 py-2 text-right tabular-nums">Peso (kg)</TableHead>
                <TableHead className="px-3 py-2 text-center">Peligroso</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {c.mercancias.map((m, i) => (
                <TableRow key={`${m.bienesTransp}-${i}`} className="border-t border-line">
                  <TableCell className="px-3 py-2">{m.descripcion}</TableCell>
                  <TableCell className="px-3 py-2 font-mono text-xs">{m.bienesTransp}</TableCell>
                  <TableCell className="px-3 py-2 font-mono text-xs">{m.claveUnidad}</TableCell>
                  <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                    {m.cantidad.toFixed(2)}
                  </TableCell>
                  <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                    {m.pesoEnKg.toFixed(2)}
                  </TableCell>
                  <TableCell className="px-3 py-2 text-center">
                    {m.materialPeligroso ? 'Sí' : '—'}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </section>

      {/* ── Trazabilidad documento-céntrica (ANT-PR3, doc 13) ────── */}
      <TrazabilidadFacturacion raiz="comprobante" id={c.id} />
      <IntentosTimbradoPanel comprobanteId={c.id} />
    </div>
  );
}

function SiguienteTramoForm({
  previa,
  onDone,
}: {
  previa: CartaPorteDetalleResponse;
  onDone: () => void;
}) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useSiguienteTramo();

  const form = useForm<SiguienteTramoValues>({
    resolver: zodResolver(SiguienteTramoSchema),
    defaultValues: {
      tipoCfdi: previa.tipo === 'I' ? 'I' : 'T',
      sucursalId: '',
      origen: previa.destino,
      destino: '',
      origenCodigoPostal: '',
      origenEstado: '',
      destinoCodigoPostal: '',
      destinoEstado: '',
      distanciaKm: 0,
      vehiculoId: previa.vehiculo?.id ?? '',
      operadorId: previa.operador?.id ?? '',
      fechaSalida: hoyLocalISO(),
      fechaLlegadaEstimada: hoyLocalISO(),
      montoServicio: 0,
      tasaIvaServicio: 0.16,
    },
  });

  function onSubmit(values: SiguienteTramoValues) {
    crear.mutate(
      {
        previaId: previa.id,
        command: {
          tipoCfdi: values.tipoCfdi,
          sucursalId: values.sucursalId,
          origen: values.origen,
          destino: values.destino,
          origenCodigoPostal: values.origenCodigoPostal,
          origenEstado: values.origenEstado.toUpperCase(),
          destinoCodigoPostal: values.destinoCodigoPostal,
          destinoEstado: values.destinoEstado.toUpperCase(),
          distanciaKm: values.distanciaKm,
          vehiculoId: values.vehiculoId,
          operadorId: values.operadorId,
          fechaSalida: `${values.fechaSalida}T08:00:00Z`,
          fechaLlegadaEstimada: `${values.fechaLlegadaEstimada}T18:00:00Z`,
          montoServicio: values.montoServicio,
          tasaIvaServicio: values.tasaIvaServicio,
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(`Siguiente tramo creado (${res.folio}).`);
          onDone();
          navigate({
            to: '/facturacion/carta-porte/$id',
            params: { id: res.id },
          });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
            ) {
              return;
            }
            // El aviso en el formulario (ErrorTimbradoAviso) muestra el motivo; sin toast técnico duplicado.
            return;
          }
          toast.error('No se pudo crear el siguiente tramo.');
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      data-print="hidden"
      className="space-y-3 rounded-md border border-dashed border-brand/40 bg-brand/5 p-3"
    >
      <ErrorTimbradoAviso error={crear.error} />
      <h3 className="text-sm font-medium">
        Crear siguiente tramo (continúa desde {previa.destino})
      </h3>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
        <CampoCp label="Tipo CFDI">
          <select
            className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-sm"
            {...form.register('tipoCfdi')}
          >
            <option value="T">T — Traslado</option>
            <option value="I">I — Ingreso</option>
          </select>
        </CampoCp>
        <CampoCp label="Sucursal" error={form.formState.errors.sucursalId?.message}>
          <Controller
            name="sucursalId"
            control={form.control}
            render={({ field }) => (
              <SucursalSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
              />
            )}
          />
        </CampoCp>
        <CampoCp label="Origen" error={form.formState.errors.origen?.message}>
          <Input {...form.register('origen')} />
        </CampoCp>
        <CampoCp label="CP origen" error={form.formState.errors.origenCodigoPostal?.message}>
          <Input
            className="font-mono"
            maxLength={5}
            inputMode="numeric"
            {...form.register('origenCodigoPostal')}
          />
        </CampoCp>
        <CampoCp
          label="Estado origen (clave SAT)"
          error={form.formState.errors.origenEstado?.message}
        >
          <Input className="uppercase" maxLength={3} {...form.register('origenEstado')} />
        </CampoCp>
        <CampoCp label="Destino" error={form.formState.errors.destino?.message}>
          <Input {...form.register('destino')} />
        </CampoCp>
        <CampoCp label="CP destino" error={form.formState.errors.destinoCodigoPostal?.message}>
          <Input
            className="font-mono"
            maxLength={5}
            inputMode="numeric"
            {...form.register('destinoCodigoPostal')}
          />
        </CampoCp>
        <CampoCp
          label="Estado destino (clave SAT)"
          error={form.formState.errors.destinoEstado?.message}
        >
          <Input className="uppercase" maxLength={3} {...form.register('destinoEstado')} />
        </CampoCp>
        <CampoCp label="Distancia (km)" error={form.formState.errors.distanciaKm?.message}>
          <Input
            type="number"
            step="0.1"
            min="0"
            {...form.register('distanciaKm', { valueAsNumber: true })}
          />
        </CampoCp>
        <CampoCp label="Vehículo" error={form.formState.errors.vehiculoId?.message}>
          <Controller
            name="vehiculoId"
            control={form.control}
            render={({ field }) => (
              <VehiculoPicker
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                initialLabel={
                  previa.vehiculo != null
                    ? `${previa.vehiculo.placa} · ${previa.vehiculo.configVehicular}`
                    : null
                }
              />
            )}
          />
        </CampoCp>
        <CampoCp label="Operador" error={form.formState.errors.operadorId?.message}>
          <Controller
            name="operadorId"
            control={form.control}
            render={({ field }) => (
              <OperadorPicker
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                initialLabel={
                  previa.operador != null
                    ? `${previa.operador.nombre} · ${previa.operador.rfc}`
                    : null
                }
              />
            )}
          />
        </CampoCp>
        <CampoCp label="Fecha salida">
          <Input type="date" {...form.register('fechaSalida')} />
        </CampoCp>
        <CampoCp label="Fecha llegada">
          <Input type="date" {...form.register('fechaLlegadaEstimada')} />
        </CampoCp>
        <CampoCp label="Monto servicio">
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register('montoServicio', { valueAsNumber: true })}
          />
        </CampoCp>
      </div>
      <div className="flex justify-end gap-2">
        <Button type="button" variant="ghost" size="sm" onClick={onDone} disabled={crear.isPending}>
          Cancelar
        </Button>
        <Button type="submit" size="sm" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear tramo'}
        </Button>
      </div>
    </form>
  );
}

function CampoCp({
  label,
  error,
  children,
}: {
  label: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <Label className="text-xs">{label}</Label>
      {children}
      {error != null && <p className="text-xs text-danger-fg">{error}</p>}
    </div>
  );
}

function Dato({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-0.5">
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  );
}
