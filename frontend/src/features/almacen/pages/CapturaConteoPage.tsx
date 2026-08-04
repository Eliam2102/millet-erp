import { useMemo, useRef, useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft, ArrowRight, Check, EyeOff } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { ErrorState, TableSkeleton } from '@/components/erp';
import {
  EtiquetaArticulo,
  EtiquetaSubAlmacen,
} from '@/features/almacen/components/EtiquetasConteo';
import {
  useCapturarLinea,
  useConteo,
  useLineasParaCapturar,
} from '@/features/almacen/api/useConteos';
import {
  EstadoConteo,
  EstadoConteoLabels,
  type LineaConteoParaCapturarDto,
} from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

const FROM = '/_app/almacen/inventarios/$id/captura' as const;

/**
 * <c>P9 — Captura sin sesgo</c> (doc 07 §FE-F5-PR1, doc 00 §A6).
 * Pantalla dedicada full-width que NO muestra la cantidad teórica
 * — el contador captura sin sesgo.
 *
 * <para><b>Seguridad UX (A6)</b>:</para>
 * <list>
 *   <item>El endpoint <c>/lineas-para-capturar</c> NO incluye
 *     <c>cantidadTeorica</c> en el payload — no hay forma de leerla
 *     desde DevTools en este flujo.</item>
 *   <item>El header avisa: "Capturando sin ver cantidad teórica
 *     (política A6)". Es un banner visible.</item>
 *   <item>Tras capturar una línea, el foco salta automáticamente a la
 *     siguiente — minimiza la tentación de volver atrás.</item>
 *   <item>El cierre de la pantalla (volver al detalle) no expone
 *     teóricos.</item>
 * </list>
 */
export function CapturaConteoPage() {
  const { id } = useParams({ from: FROM });
  const conteoQuery = useConteo(id);
  const lineasQuery = useLineasParaCapturar(id);
  const capturar = useCapturarLinea();

  // El índice activo se calcula del lado del usuario: o bien la
  // selección manual más reciente (override) o, si no hay, la
  // primera línea no capturada. Evitamos el patrón useState +
  // useEffect (que dispara react-hooks/set-state-in-effect).
  const [overrideIndice, setOverrideIndice] = useState<number | null>(null);
  const inputsRef = useRef<Record<string, HTMLInputElement | null>>({});

  // Solo se permite capturar si el conteo está EnCurso.
  const noCapturable =
    conteoQuery.data != null &&
    conteoQuery.data.estado !== EstadoConteo.EnCurso;

  const indiceActivo = useMemo(() => {
    if (overrideIndice != null) return overrideIndice;
    const items = lineasQuery.data ?? [];
    if (items.length === 0) return 0;
    const idx = items.findIndex((l) => l.cantidadRealCapturada == null);
    return idx === -1 ? 0 : idx;
  }, [overrideIndice, lineasQuery.data]);

  async function guardarLinea(linea: LineaConteoParaCapturarDto, valor: number) {
    if (!id) return;
    try {
      await capturar.mutateAsync({
        conteoId: id,
        lineaId: linea.id,
        cantidadReal: valor,
      });
      toast.success('Línea capturada', { duration: 1500 });
      avanzarFoco();
    } catch (err) {
      if (esApiError(err)) {
        toast.error(err.problem.title, {
          description: err.traceId ? `Código: ${err.traceId}` : undefined,
        });
      } else {
        toast.error('Error inesperado al capturar.');
      }
    }
  }

  function avanzarFoco() {
    const items = lineasQuery.data ?? [];
    if (items.length === 0) return;
    // Buscar la siguiente línea no capturada después del índice actual.
    let next = indiceActivo + 1;
    while (next < items.length && items[next].cantidadRealCapturada != null) {
      next++;
    }
    if (next < items.length) {
      setOverrideIndice(next);
      const sigId = items[next].id;
      // Espera al próximo tick para que el render aplique el activo.
      setTimeout(() => {
        inputsRef.current[sigId]?.focus();
        inputsRef.current[sigId]?.select();
      }, 50);
    } else {
      // No quedan líneas pendientes.
      toast.success('Captura completa', {
        description: 'Vuelve al detalle para enviar a conciliación.',
      });
    }
  }

  const progreso = useMemo(() => {
    const items = lineasQuery.data ?? [];
    const total = items.length;
    const capturadas = items.filter(
      (l) => l.cantidadRealCapturada != null,
    ).length;
    return { capturadas, total };
  }, [lineasQuery.data]);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link
            to="/almacen/inventarios/$id"
            params={{ id: id ?? '' }}
          >
            <ArrowLeft className="mr-2 h-4 w-4" />
            Volver al detalle
          </Link>
        </Button>
        <div className="text-sm text-muted-foreground">
          Progreso:{' '}
          <span className="font-mono font-semibold">
            {progreso.capturadas} / {progreso.total}
          </span>
        </div>
      </div>

      {/* Banner sin-sesgo */}
      <div className="flex items-start gap-3 rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-sm">
        <EyeOff className="mt-0.5 h-5 w-5 shrink-0 text-amber-700" />
        <div>
          <p className="font-medium text-amber-900">
            Captura sin sesgo (política A6)
          </p>
          <p className="text-amber-800">
            Estás capturando la cantidad real <b>sin ver la cantidad
            teórica</b>. El aprobador comparará teórico vs real en una
            pantalla separada. Reporta lo que ves físicamente — no lo que
            crees que debería haber.
          </p>
        </div>
      </div>

      {conteoQuery.isError ? (
        <ErrorState
          title="No se pudo cargar el conteo"
          problem={
            esApiError(conteoQuery.error) ? conteoQuery.error.problem : undefined
          }
          onRetry={() => conteoQuery.refetch()}
        />
      ) : lineasQuery.isError ? (
        <ErrorState
          title="No se pudieron cargar las líneas"
          problem={
            esApiError(lineasQuery.error) ? lineasQuery.error.problem : undefined
          }
          onRetry={() => lineasQuery.refetch()}
        />
      ) : lineasQuery.isLoading || conteoQuery.isLoading ? (
        <TableSkeleton
          rows={10}
          columns={[
            { width: 'w-8' },
            { width: 'w-64' },
            { width: 'w-32' },
            { width: 'w-24' },
            { width: 'w-32' },
          ]}
        />
      ) : noCapturable ? (
        <div className="rounded-md border bg-muted/30 px-4 py-6 text-center text-sm text-muted-foreground">
          El conteo está en estado{' '}
          <b>{EstadoConteoLabels[conteoQuery.data!.estado]}</b> — la captura
          solo se permite mientras esté <b>En curso</b>.
        </div>
      ) : (lineasQuery.data ?? []).length === 0 ? (
        <div className="rounded-md border bg-muted/30 px-4 py-6 text-center text-sm text-muted-foreground">
          Sin líneas para capturar en este conteo.
        </div>
      ) : (
        <TablaCaptura
          lineas={lineasQuery.data!}
          indiceActivo={indiceActivo}
          onSetIndiceActivo={setOverrideIndice}
          inputsRef={inputsRef}
          onGuardar={guardarLinea}
          guardandoLineaId={
            capturar.isPending ? capturar.variables?.lineaId ?? null : null
          }
        />
      )}
    </div>
  );
}

function TablaCaptura({
  lineas,
  indiceActivo,
  onSetIndiceActivo,
  inputsRef,
  onGuardar,
  guardandoLineaId,
}: {
  lineas: readonly LineaConteoParaCapturarDto[];
  indiceActivo: number;
  onSetIndiceActivo: (i: number) => void;
  inputsRef: React.MutableRefObject<Record<string, HTMLInputElement | null>>;
  onGuardar: (linea: LineaConteoParaCapturarDto, valor: number) => void;
  guardandoLineaId: string | null;
}) {
  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">#</th>
            <th className="px-3 py-2 text-left">Artículo</th>
            <th className="px-3 py-2 text-left">Sub-almacén</th>
            <th className="px-3 py-2 text-left">Rack</th>
            <th className="px-3 py-2 text-right">Cantidad real</th>
            <th className="px-3 py-2 text-left">Estado</th>
          </tr>
        </thead>
        <tbody>
          {lineas.map((linea, index) => (
            <FilaCaptura
              key={linea.id}
              index={index}
              linea={linea}
              activo={index === indiceActivo}
              onActivar={() => onSetIndiceActivo(index)}
              inputRef={(el) => {
                inputsRef.current[linea.id] = el;
              }}
              onGuardar={(valor) => onGuardar(linea, valor)}
              guardando={guardandoLineaId === linea.id}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}

function FilaCaptura({
  index,
  linea,
  activo,
  onActivar,
  inputRef,
  onGuardar,
  guardando,
}: {
  index: number;
  linea: LineaConteoParaCapturarDto;
  activo: boolean;
  onActivar: () => void;
  inputRef: (el: HTMLInputElement | null) => void;
  onGuardar: (valor: number) => void;
  guardando: boolean;
}) {
  const [valor, setValor] = useState<string>(
    linea.cantidadRealCapturada != null
      ? String(linea.cantidadRealCapturada)
      : '',
  );

  function intentarGuardar() {
    const numero = Number(valor);
    if (Number.isNaN(numero) || numero < 0) {
      toast.error('Cantidad inválida');
      return;
    }
    onGuardar(numero);
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Enter') {
      e.preventDefault();
      intentarGuardar();
    }
  }

  const capturada = linea.cantidadRealCapturada != null;

  return (
    <tr
      className={cn(
        'border-t',
        activo && 'bg-primary/5',
        capturada && !activo && 'opacity-60',
      )}
      onClick={onActivar}
    >
      <td className="px-3 py-2 font-mono text-xs">{index + 1}</td>
      <td className="px-3 py-2">
        <EtiquetaArticulo
          articuloId={linea.articuloId}
          clave={linea.articuloClave}
          descripcion={linea.articuloDescripcion}
        />
      </td>
      <td className="px-3 py-2">
        <EtiquetaSubAlmacen
          subAlmacenId={linea.subAlmacenId}
          clave={linea.subAlmacenClave}
        />
      </td>
      <td className="px-3 py-2 font-mono text-xs">{linea.ubicacionClave}</td>
      <td className="px-3 py-2 text-right">
        <div className="flex items-center justify-end gap-2">
          <Input
            ref={inputRef}
            type="number"
            inputMode="decimal"
            step="0.0001"
            min="0"
            value={valor}
            onChange={(e) => setValor(e.target.value)}
            onFocus={onActivar}
            onKeyDown={onKeyDown}
            className="w-32 text-right font-mono"
            disabled={guardando}
            aria-label={`Cantidad real línea ${index + 1}`}
          />
          <Button
            type="button"
            size="sm"
            variant={capturada ? 'outline' : 'default'}
            onClick={intentarGuardar}
            disabled={guardando || valor === ''}
            aria-label={`Guardar línea ${index + 1}`}
          >
            {guardando ? (
              <ArrowRight className="h-4 w-4 animate-pulse" />
            ) : (
              <Check className="h-4 w-4" />
            )}
          </Button>
        </div>
      </td>
      <td className="px-3 py-2">
        {capturada ? (
          <span className="inline-flex items-center gap-1 text-xs text-emerald-700">
            <Check className="h-3 w-3" />
            Capturada
          </span>
        ) : linea.requiereRecuento ? (
          <span className="inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
            Recuento
          </span>
        ) : (
          <span className="text-xs text-muted-foreground">Pendiente</span>
        )}
      </td>
    </tr>
  );
}
