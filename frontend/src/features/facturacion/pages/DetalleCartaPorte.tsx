import { hoyLocalISO } from '@/lib/datetime';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Link, useNavigate, useParams, useSearch } from '@tanstack/react-router';
import { Route as RouteIcon, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ErrorState, SucursalSelector } from '@/components/erp';
import {
  useCartaPorte,
  useSiguienteTramo,
} from '@/features/facturacion/api/useCartaPorte';
import {
  SiguienteTramoSchema,
  type SiguienteTramoValues,
} from '@/features/facturacion/schemas/carta-porte';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import type { CartaPorteDetalleResponse } from '@/features/facturacion/api/types';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import { TimbradoFallidoBanner } from '@/features/facturacion/components/TimbradoFallidoBanner';
import { IntentosTimbradoPanel } from '@/features/facturacion/components/IntentosTimbradoPanel';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import { VehiculoPicker } from '@/features/facturacion/components/VehiculoPicker';
import { OperadorPicker } from '@/features/facturacion/components/OperadorPicker';
import type { CartaPorteSearch } from '@/features/facturacion/lib/carta-porte-search-schema';

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
    <div className="space-y-4">
      {/* Sub-topbar del detalle (§6.5): cerrar vuelve a la bandeja
          preservando los filtros activos. */}
      <div
        className="sticky top-0 z-10 flex items-center justify-end gap-2 border-b bg-background/95 pb-2 backdrop-blur"
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
          <div className="h-8 w-64 animate-pulse rounded bg-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-muted" />
        </div>
      ) : (
        <Contenido c={query.data} />
      )}
    </div>
  );
}

function Contenido({
  c,
}: {
  c: NonNullable<ReturnType<typeof useCartaPorte>['data']>;
}) {
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionCartaPorteEmitir);
  const [siguienteAbierto, setSiguienteAbierto] = useState(false);
  // Solo desde un tramo timbrado tiene sentido crear el siguiente.
  const puedeContinuar = puedeEmitir && c.estado === 'Timbrado';

  return (
    <div className="space-y-6">
      <TimbradoFallidoBanner
        comprobanteId={c.id}
        estado={c.estado}
        errorCodigo={c.timbradoErrorCodigo}
        errorMensaje={c.timbradoErrorMensaje}
      />

      <IntentosTimbradoPanel comprobanteId={c.id} />

      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="font-mono text-2xl font-semibold">{c.folio}</h1>
          <ChipTimbrado estado={c.estado} />
          <span className="rounded-full bg-muted px-2 py-0.5 text-xs">
            Tipo {c.tipo}
          </span>
        </div>
        {c.uuid && (
          <p className="font-mono text-xs text-muted-foreground">UUID {c.uuid}</p>
        )}
        {c.cartaPortePreviaId && (
          <p className="text-xs">
            Continuación de{' '}
            <Link
              to="/facturacion/carta-porte/$id"
              params={{ id: c.cartaPortePreviaId }}
              className="text-primary hover:underline"
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
      </header>

      {siguienteAbierto && (
        <SiguienteTramoForm previa={c} onDone={() => setSiguienteAbierto(false)} />
      )}

      <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <Dato label="Origen">{c.origen}</Dato>
        <Dato label="Destino">{c.destino}</Dato>
        <Dato label="Distancia">{c.distanciaKm.toFixed(1)} km</Dato>
        <Dato label="Salida">
          {new Date(c.fechaSalida).toLocaleString('es-MX')}
        </Dato>
        <Dato label="Llegada estimada">
          {new Date(c.fechaLlegadaEstimada).toLocaleString('es-MX')}
        </Dato>
        {c.total > 0 && <Dato label="Total">{c.total.toFixed(2)}</Dato>}
      </section>

      <section className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div className="rounded-md border px-3 py-2">
          <h2 className="text-xs font-medium text-muted-foreground">Vehículo</h2>
          {c.vehiculo ? (
            <p className="text-sm">
              {c.vehiculo.placa} · {c.vehiculo.configVehicular} ·{' '}
              {c.vehiculo.anioModelo}
            </p>
          ) : (
            <p className="text-sm text-muted-foreground">—</p>
          )}
        </div>
        <div className="rounded-md border px-3 py-2">
          <h2 className="text-xs font-medium text-muted-foreground">Operador</h2>
          {c.operador ? (
            <p className="text-sm">
              {c.operador.nombre} · {c.operador.rfc} · Lic. {c.operador.numLicencia}
            </p>
          ) : (
            <p className="text-sm text-muted-foreground">—</p>
          )}
        </div>
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Mercancías ({c.mercancias.length})</h2>
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Descripción</th>
                <th className="px-3 py-2 text-left">Bienes transp.</th>
                <th className="px-3 py-2 text-left">Unidad</th>
                <th className="px-3 py-2 text-right">Cantidad</th>
                <th className="px-3 py-2 text-right">Peso (kg)</th>
                <th className="px-3 py-2 text-center">Peligroso</th>
              </tr>
            </thead>
            <tbody>
              {c.mercancias.map((m, i) => (
                <tr key={`${m.bienesTransp}-${i}`} className="border-t">
                  <td className="px-3 py-2">{m.descripcion}</td>
                  <td className="px-3 py-2 font-mono text-xs">{m.bienesTransp}</td>
                  <td className="px-3 py-2 font-mono text-xs">{m.claveUnidad}</td>
                  <td className="px-3 py-2 text-right font-mono">
                    {m.cantidad.toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {m.pesoEnKg.toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-center">
                    {m.materialPeligroso ? 'Sí' : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {/* ── Trazabilidad documento-céntrica (ANT-PR3, doc 13) ────── */}
      <TrazabilidadFacturacion raiz="comprobante" id={c.id} />
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
      className="space-y-3 rounded-md border border-dashed border-primary/40 bg-primary/5 p-3"
    >
      <h3 className="text-sm font-medium">
        Crear siguiente tramo (continúa desde {previa.destino})
      </h3>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
        <CampoCp label="Tipo CFDI">
          <select
            className="h-9 w-full rounded-md border border-input bg-transparent px-3 text-sm shadow-sm"
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
              <SucursalSelector value={field.value || null} onChange={(id) => field.onChange(id ?? '')} />
            )}
          />
        </CampoCp>
        <CampoCp label="Origen" error={form.formState.errors.origen?.message}>
          <Input {...form.register('origen')} />
        </CampoCp>
        <CampoCp label="CP origen" error={form.formState.errors.origenCodigoPostal?.message}>
          <Input className="font-mono" maxLength={5} inputMode="numeric" {...form.register('origenCodigoPostal')} />
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
          <Input className="font-mono" maxLength={5} inputMode="numeric" {...form.register('destinoCodigoPostal')} />
        </CampoCp>
        <CampoCp
          label="Estado destino (clave SAT)"
          error={form.formState.errors.destinoEstado?.message}
        >
          <Input className="uppercase" maxLength={3} {...form.register('destinoEstado')} />
        </CampoCp>
        <CampoCp label="Distancia (km)" error={form.formState.errors.distanciaKm?.message}>
          <Input type="number" step="0.1" min="0" {...form.register('distanciaKm', { valueAsNumber: true })} />
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
          <Input type="number" step="0.01" min="0" {...form.register('montoServicio', { valueAsNumber: true })} />
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
      {error != null && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}

function Dato({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-0.5">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  );
}
