import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useDepartamentos } from '@/features/catalogos/api/hooks';
import {
  EstatusCatalogo,
  MODO_NIVEL2_LABEL,
  ModoNivel2,
  type UmbralAprobacionResponse,
} from '@/features/compras/api/types';
import {
  useGuardarUmbral,
  useUmbralesAprobacion,
} from '@/features/compras/api/useUmbralesAprobacion';
import {
  UMBRAL_GENERAL_ID,
  UmbralAprobacionSchema,
  aUpsertUmbral,
  valoresIniciales,
  type UmbralFormValues,
} from '@/features/compras/schemas/umbral-aprobacion';

/**
 * <c>&lt;UmbralesAprobacionSection/&gt;</c> — tabla de umbrales N1 (menor
 * cuantía, opcional) y N2 por departamento. Cada fila guarda por separado
 * (un PUT por departamento, con su propio Idempotency-Key).
 *
 * <para>Estados: carga, error (lista o catálogo), vacío (sin departamentos
 * activos). Sin permiso de editar, los inputs y botones quedan
 * deshabilitados.</para>
 */
export interface UmbralesAprobacionSectionProps {
  puedeEditar: boolean;
}

export function UmbralesAprobacionSection({ puedeEditar }: UmbralesAprobacionSectionProps) {
  const departamentosQuery = useDepartamentos();
  const umbralesQuery = useUmbralesAprobacion();

  if (departamentosQuery.isPending || umbralesQuery.isPending) {
    return (
      <div className="rounded-lg bg-surface-card px-4 py-6 text-center text-sm text-ink-muted shadow-card-flat">
        Cargando umbrales de aprobación…
      </div>
    );
  }

  if (departamentosQuery.isError || umbralesQuery.isError) {
    return (
      <div role="alert" className="rounded-lg bg-danger-bg px-4 py-4 text-sm text-danger-fg">
        No fue posible cargar los umbrales de aprobación.
      </div>
    );
  }

  const departamentos = (departamentosQuery.data?.items ?? []).filter(
    (d) => d.estatus === EstatusCatalogo.Activo,
  );

  // La fila general siempre va primero: es el respaldo de la OC cuando el
  // departamento no tiene umbral propio (DepartamentoId = Guid.Empty).
  const entidades: EntidadUmbral[] = [
    { id: UMBRAL_GENERAL_ID, clave: null, nombre: 'General (empresa)' },
    ...departamentos,
  ];

  const umbralPorDepartamento = new Map(
    umbralesQuery.data.map((u) => [u.departamentoId, u]),
  );

  return (
    <section
      aria-labelledby="umbrales-titulo"
      className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat"
    >
      <div className="space-y-1 border-b border-line-divider px-4 py-3">
        <h2 id="umbrales-titulo" className="text-sm font-semibold text-ink-strong">
          Umbrales de aprobación
        </h2>
        <p className="text-xs text-ink-muted">
          N1 = menor cuantía (aprobación rápida); N2 = por encima de este monto la OC también
          requiere Dirección. El modo de nivel 2 decide si la requisición pide N2 por monto, por
          disponibilidad (si algo no está en almacén y su importe a comprar supera el mínimo) o
          por ambos. Sin umbral del departamento se usa la fila General; sin ninguna, la
          requisición pide N2. Montos en MXN.
        </p>
        {departamentos.length === 0 && (
          <p className="text-xs text-ink-muted">
            No hay departamentos activos. Da de alta un departamento en el catálogo para definir sus umbrales.
          </p>
        )}
        {!puedeEditar && (
          <p role="note" className="text-xs text-ink-strong">
            Solo lectura: tu rol puede consultar los umbrales, pero no modificarlos.
          </p>
        )}
      </div>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Departamento</TableHead>
            <TableHead className="text-right">N1 menor cuantía (MXN)</TableHead>
            <TableHead className="text-right">N2 monto (MXN)</TableHead>
            <TableHead>Modo de nivel 2</TableHead>
            <TableHead className="text-right">Mínimo por disponibilidad (MXN)</TableHead>
            <TableHead>Vigente desde</TableHead>
            <TableHead>
              <span className="sr-only">Acciones</span>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {entidades.map((entidad) => {
            const umbral = umbralPorDepartamento.get(entidad.id);
            return (
              <FilaUmbral
                // Remonta la fila cuando cambia el umbral del servidor, para
                // que el formulario tome los valores nuevos sin efectos.
                key={`${entidad.id}-${umbral?.vigenteDesde ?? ''}-${umbral?.umbralMonto ?? ''}-${umbral?.umbralMenorCuantia ?? ''}-${umbral?.modoNivel2 ?? ''}-${umbral?.montoMinimoDisponibilidad ?? ''}`}
                departamento={entidad}
                umbral={umbral}
                puedeEditar={puedeEditar}
              />
            );
          })}
        </TableBody>
      </Table>
    </section>
  );
}

/** Fila de la tabla: el umbral general (sin clave) o un departamento del catálogo. */
interface EntidadUmbral {
  id: string;
  clave: string | null;
  nombre: string;
}

interface FilaUmbralProps {
  departamento: EntidadUmbral;
  umbral: UmbralAprobacionResponse | undefined;
  puedeEditar: boolean;
}

function FilaUmbral({ departamento, umbral, puedeEditar }: FilaUmbralProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const guardar = useGuardarUmbral();

  const form = useForm<UmbralFormValues>({
    resolver: zodResolver(UmbralAprobacionSchema),
    defaultValues: valoresIniciales(umbral),
  });

  const errores = form.formState.errors;
  const bloqueado = !puedeEditar || guardar.isPending;
  const nombre = departamento.nombre;

  function onSubmit(values: UmbralFormValues) {
    guardar.mutate(
      { values: aUpsertUmbral(departamento.id, values), idempotencyKey },
      {
        onSuccess: () => toast.success(`Umbral guardado para ${nombre}.`),
        onError: (error) => {
          if (!esApiError(error)) {
            toast.error(`No fue posible guardar el umbral de ${nombre}.`);
          }
        },
      },
    );
  }

  const mensajeServidor =
    guardar.isError && esApiError(guardar.error)
      ? `${guardar.error.problem.detail ?? guardar.error.problem.title}${
          guardar.error.traceId ? ` (código ${guardar.error.traceId})` : ''
        }`
      : guardar.isError
        ? `No fue posible guardar el umbral de ${nombre}.`
        : null;

  return (
    <TableRow>
      <TableCell>
        <form id={`umbral-form-${departamento.id}`} onSubmit={form.handleSubmit(onSubmit)} noValidate>
          {departamento.clave && (
            <span className="block font-mono text-xs text-ink-muted">{departamento.clave}</span>
          )}
          <span className="block text-sm text-ink">{nombre}</span>
        </form>
      </TableCell>
      <TableCell className="text-right">
        <Input
          form={`umbral-form-${departamento.id}`}
          inputMode="decimal"
          aria-label={`Umbral N1 de ${nombre}`}
          aria-invalid={errores.umbralMenorCuantia ? true : undefined}
          aria-describedby={errores.umbralMenorCuantia ? `n1-err-${departamento.id}` : undefined}
          disabled={bloqueado}
          placeholder="Sin N1"
          className="ml-auto w-36 text-right tabular-nums"
          {...form.register('umbralMenorCuantia')}
        />
        {errores.umbralMenorCuantia && (
          <p id={`n1-err-${departamento.id}`} role="alert" className="mt-1 text-xs text-danger-fg">
            {errores.umbralMenorCuantia.message}
          </p>
        )}
      </TableCell>
      <TableCell className="text-right">
        <Input
          form={`umbral-form-${departamento.id}`}
          inputMode="decimal"
          aria-label={`Umbral N2 de ${nombre}`}
          aria-invalid={errores.umbralMonto ? true : undefined}
          aria-describedby={errores.umbralMonto ? `n2-err-${departamento.id}` : undefined}
          disabled={bloqueado}
          className="ml-auto w-36 text-right tabular-nums"
          {...form.register('umbralMonto')}
        />
        {errores.umbralMonto && (
          <p id={`n2-err-${departamento.id}`} role="alert" className="mt-1 text-xs text-danger-fg">
            {errores.umbralMonto.message}
          </p>
        )}
      </TableCell>
      <TableCell>
        <Controller
          name="modoNivel2"
          control={form.control}
          render={({ field }) => (
            <Select value={field.value} onValueChange={field.onChange} disabled={bloqueado}>
              <SelectTrigger aria-label={`Modo de nivel 2 de ${nombre}`} className="w-52">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {Object.values(ModoNivel2).map((m) => (
                  <SelectItem key={m} value={m}>
                    {MODO_NIVEL2_LABEL[m]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
      </TableCell>
      <TableCell className="text-right">
        <Input
          form={`umbral-form-${departamento.id}`}
          inputMode="decimal"
          aria-label={`Mínimo por disponibilidad de ${nombre}`}
          aria-invalid={errores.montoMinimoDisponibilidad ? true : undefined}
          aria-describedby={
            errores.montoMinimoDisponibilidad ? `min-err-${departamento.id}` : undefined
          }
          disabled={bloqueado}
          placeholder="Sin mínimo"
          className="ml-auto w-36 text-right tabular-nums"
          {...form.register('montoMinimoDisponibilidad')}
        />
        {errores.montoMinimoDisponibilidad && (
          <p id={`min-err-${departamento.id}`} role="alert" className="mt-1 text-xs text-danger-fg">
            {errores.montoMinimoDisponibilidad.message}
          </p>
        )}
      </TableCell>
      <TableCell className="text-ink-secondary">
        {umbral ? formatearFecha(umbral.vigenteDesde) : '—'}
      </TableCell>
      <TableCell className="text-right">
        <Button
          type="submit"
          form={`umbral-form-${departamento.id}`}
          size="sm"
          variant="outline"
          disabled={bloqueado}
          aria-label={`Guardar umbral de ${nombre}`}
        >
          {guardar.isPending ? 'Guardando…' : 'Guardar'}
        </Button>
        {mensajeServidor && (
          <p role="alert" className="mt-1 text-xs text-danger-fg">
            {mensajeServidor}
          </p>
        )}
      </TableCell>
    </TableRow>
  );
}

/** <c>DateOnly</c> ("2026-10-15") → fecha corta en español de México. */
function formatearFecha(iso: string): string {
  return new Date(`${iso}T00:00:00`).toLocaleDateString('es-MX', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}
