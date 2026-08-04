import { useMemo, useState, type ReactNode } from 'react';
import { useParams } from '@tanstack/react-router';
import { Pencil, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { DateTimeDisplay, EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { CanalVentaSelector } from '@/features/facturacion/components/selectors/CanalVentaSelector';
import {
  useActualizarCaja,
  useCaja,
  useReemplazarCanalesCaja,
  useReemplazarSucursalesCaja,
  useReemplazarUsuariosCaja,
} from '@/features/facturacion/api/useCajas';
import { useAjustesCaja } from '@/features/facturacion/api/useCajaSesiones';
import { useSucursales, useUsuarios } from '@/features/catalogos/api/hooks';
import { ChipEstatusCaja } from '@/features/facturacion/components/ListaCajasCompacta';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>Detalle de caja</c> (CAJAS-PR5, 12-cajas.md §4.1/§8): datos generales
 * (edición inline, patrón amber §6.3) + los tres alcances como replace-sets
 * (chips + selector; borde dashed primary cuando hay cambios sin guardar).
 * Semántica de comodines: sin sucursales = todas; sin canales = todos.
 */
export function DetalleCaja() {
  const { id } = useParams({ from: '/_app/facturacion/cajas/$id' });
  const query = useCaja(id);

  if (query.isLoading) {
    return (
      <div className="p-4">
        <TableSkeleton rows={6} columns={[{ width: 'w-full' }]} />
      </div>
    );
  }
  if (query.isError) {
    return (
      <ErrorState
        title="No se pudo cargar la caja"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch()}
      />
    );
  }
  const caja = query.data;
  if (caja == null) {
    return <EmptyState title="Caja no encontrada" description="Puede haber sido eliminada." />;
  }

  return (
    <div className="space-y-4">
      <TarjetaDatosGenerales
        key={`datos-${caja.id}-${caja.version}`}
        cajaId={caja.id}
        nombre={caja.nombre}
        descripcion={caja.descripcion}
        estatus={caja.estatus}
      />
      <TarjetaSucursales
        key={`suc-${caja.id}-${caja.version}`}
        cajaId={caja.id}
        inicial={caja.sucursales.map((s) => s.sucursalId)}
      />
      <TarjetaCanales
        key={`can-${caja.id}-${caja.version}`}
        cajaId={caja.id}
        inicial={caja.canales.map((c) => c.canalVentaId)}
        nombres={new Map(caja.canales.map((c) => [c.canalVentaId, c.nombre]))}
      />
      <TarjetaUsuarios
        key={`usr-${caja.id}-${caja.version}`}
        cajaId={caja.id}
        inicial={caja.usuarios.map((u) => u.usuarioId)}
      />
      <TarjetaAjustes cajaId={caja.id} />
    </div>
  );
}

// ─── Ajustes de la caja ([Decisión 12-C], CAJAS-PR7) ─────────────────

function TarjetaAjustes(props: { cajaId: string }) {
  const [incluirAplicados, setIncluirAplicados] = useState(false);
  const ajustes = useAjustesCaja(props.cajaId, incluirAplicados);
  const items = ajustes.data ?? [];

  return (
    <div className="rounded-md border bg-card p-4">
      <div className="mb-2 flex items-center justify-between gap-2">
        <div>
          <h3 className="font-medium">Ajustes de la caja</h3>
          <p className="text-xs text-muted-foreground">
            Cancelaciones sin sesión abierta; los pendientes se drenan en la próxima
            apertura ([Decisión 12-C]).
          </p>
        </div>
        <label className="flex shrink-0 items-center gap-2 text-xs text-muted-foreground">
          <input
            type="checkbox"
            checked={incluirAplicados}
            onChange={(e) => setIncluirAplicados(e.target.checked)}
          />
          Incluir aplicados
        </label>
      </div>

      {items.length === 0 ? (
        <p className="text-xs italic text-muted-foreground">
          {incluirAplicados ? 'Sin ajustes registrados.' : 'Sin ajustes pendientes.'}
        </p>
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Comprobante</th>
                <th className="px-3 py-2 text-right">Importe</th>
                <th className="px-3 py-2 text-left">Forma</th>
                <th className="px-3 py-2 text-left">Motivo</th>
                <th className="px-3 py-2 text-left">Creado</th>
                <th className="px-3 py-2 text-left">Estado</th>
              </tr>
            </thead>
            <tbody>
              {items.map((a) => (
                <tr key={a.id} className="border-t">
                  <td className="px-3 py-2 font-mono text-xs">
                    {a.comprobanteFolio ?? a.cobroMostradorId}
                  </td>
                  <td
                    className={cn(
                      'px-3 py-2 text-right font-mono tabular-nums',
                      a.importe < 0 && 'text-destructive',
                    )}
                  >
                    {new Intl.NumberFormat('es-MX', {
                      style: 'currency',
                      currency: 'MXN',
                    }).format(a.importe)}
                  </td>
                  <td className="px-3 py-2">{a.formaPago}</td>
                  <td className="px-3 py-2 text-muted-foreground">{a.motivo}</td>
                  <td className="px-3 py-2">
                    <DateTimeDisplay value={a.creadoEn} />
                  </td>
                  <td className="px-3 py-2">
                    {a.aplicadoEnSesionId == null ? (
                      <span className="inline-flex rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
                        Pendiente
                      </span>
                    ) : (
                      <span className="inline-flex rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800">
                        Aplicado
                      </span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

// ─── Datos generales ─────────────────────────────────────────────────

function TarjetaDatosGenerales(props: {
  cajaId: string;
  nombre: string;
  descripcion: string | null;
  estatus: string;
}) {
  const [editando, setEditando] = useState(false);
  const [nombre, setNombre] = useState(props.nombre);
  const [descripcion, setDescripcion] = useState(props.descripcion ?? '');
  const activa = props.estatus === 'Activo';
  const actualizar = useActualizarCaja();

  function guardar(nuevaActiva: boolean) {
    actualizar.mutate(
      {
        id: props.cajaId,
        body: {
          nombre: nombre.trim(),
          descripcion: descripcion.trim() === '' ? null : descripcion.trim(),
          activa: nuevaActiva,
        },
      },
      {
        onSuccess: () => {
          setEditando(false);
          toast.success('Caja actualizada.');
        },
        onError: (error) =>
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo actualizar la caja.',
          ),
      },
    );
  }

  return (
    <div
      className={cn(
        'rounded-md border bg-card p-4',
        editando && 'border-amber-400 bg-amber-50/40',
      )}
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        {editando ? (
          <div className="min-w-0 flex-1 space-y-2">
            <Input
              value={nombre}
              onChange={(e) => setNombre(e.target.value)}
              aria-label="Nombre de la caja"
              maxLength={100}
            />
            <Input
              value={descripcion}
              onChange={(e) => setDescripcion(e.target.value)}
              aria-label="Descripción"
              placeholder="Descripción (opcional)"
              maxLength={254}
            />
            <div className="flex gap-2">
              <Button
                size="sm"
                onClick={() => guardar(activa)}
                disabled={nombre.trim() === '' || actualizar.isPending}
              >
                Guardar
              </Button>
              <Button
                size="sm"
                variant="ghost"
                onClick={() => {
                  setNombre(props.nombre);
                  setDescripcion(props.descripcion ?? '');
                  setEditando(false);
                }}
              >
                Cancelar
              </Button>
            </div>
          </div>
        ) : (
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <h2 className="truncate text-xl font-semibold tracking-tight">{props.nombre}</h2>
              <ChipEstatusCaja estatus={props.estatus} />
            </div>
            <p className="text-sm text-muted-foreground">
              {props.descripcion ?? 'Sin descripción.'}
            </p>
          </div>
        )}
        {!editando && (
          <div className="flex shrink-0 gap-2">
            <Button size="sm" variant="outline" onClick={() => setEditando(true)}>
              <Pencil className="mr-1 h-3.5 w-3.5" />
              Editar
            </Button>
            <Button
              size="sm"
              variant={activa ? 'destructive' : 'default'}
              onClick={() => guardar(!activa)}
              disabled={actualizar.isPending}
            >
              {activa ? 'Desactivar' : 'Activar'}
            </Button>
          </div>
        )}
      </div>
    </div>
  );
}

// ─── Shell común de los editores de alcance ──────────────────────────

function TarjetaAlcance(props: {
  titulo: string;
  hint: string;
  dirty: boolean;
  guardando: boolean;
  onGuardar: () => void;
  onCancelar: () => void;
  children: ReactNode;
}) {
  return (
    <div
      className={cn(
        'rounded-md border bg-card p-4',
        props.dirty && 'border-dashed border-primary/40 bg-primary/5',
      )}
    >
      <div className="mb-2 flex items-center justify-between gap-2">
        <div>
          <h3 className="font-medium">{props.titulo}</h3>
          <p className="text-xs text-muted-foreground">{props.hint}</p>
        </div>
        {props.dirty && (
          <div className="flex shrink-0 gap-2">
            <Button size="sm" onClick={props.onGuardar} disabled={props.guardando}>
              Guardar
            </Button>
            <Button size="sm" variant="ghost" onClick={props.onCancelar}>
              Cancelar
            </Button>
          </div>
        )}
      </div>
      {props.children}
    </div>
  );
}

function Chip(props: { etiqueta: string; onQuitar: () => void }) {
  return (
    <span className="inline-flex items-center gap-1 rounded-full border bg-muted/40 px-2 py-0.5 text-xs">
      <span className="max-w-48 truncate">{props.etiqueta}</span>
      <button
        type="button"
        onClick={props.onQuitar}
        className="text-muted-foreground hover:text-destructive"
        aria-label={`Quitar ${props.etiqueta}`}
      >
        <X className="h-3 w-3" />
      </button>
    </span>
  );
}

function useSeleccion<T>(inicial: readonly T[]) {
  const [seleccion, setSeleccion] = useState<readonly T[]>(inicial);
  const dirty = useMemo(
    () =>
      seleccion.length !== inicial.length ||
      seleccion.some((v) => !inicial.includes(v)),
    [seleccion, inicial],
  );
  return { seleccion, setSeleccion, dirty, reset: () => setSeleccion(inicial) };
}

function toastResultado(nombre: string) {
  return {
    onSuccess: () => toast.success(`${nombre} actualizado.`),
    onError: (error: unknown) =>
      toast.error(
        esApiError(error) ? error.problem.title : `No se pudo actualizar ${nombre}.`,
      ),
  };
}

// ─── Sucursales ──────────────────────────────────────────────────────

function TarjetaSucursales(props: { cajaId: string; inicial: string[] }) {
  const { seleccion, setSeleccion, dirty, reset } = useSeleccion(props.inicial);
  const reemplazar = useReemplazarSucursalesCaja();
  const sucursales = useSucursales();
  const nombrePorId = useMemo(
    () => new Map((sucursales.data?.items ?? []).map((s) => [s.id, `${s.clave} — ${s.nombre}`])),
    [sucursales.data],
  );

  return (
    <TarjetaAlcance
      titulo="Sucursales del alcance"
      hint="Vacío = todas las sucursales (comodín, 12-cajas.md §4.1)."
      dirty={dirty}
      guardando={reemplazar.isPending}
      onGuardar={() =>
        reemplazar.mutate(
          { id: props.cajaId, body: { sucursalIds: [...seleccion] } },
          toastResultado('el alcance de sucursales'),
        )
      }
      onCancelar={reset}
    >
      <div className="flex flex-wrap items-center gap-2">
        {seleccion.length === 0 && (
          <span className="text-xs italic text-muted-foreground">Todas las sucursales</span>
        )}
        {seleccion.map((id) => (
          <Chip
            key={id}
            etiqueta={nombrePorId.get(id) ?? id}
            onQuitar={() => setSeleccion(seleccion.filter((s) => s !== id))}
          />
        ))}
        <div className="w-64">
          <SucursalSelector
            value={null}
            onChange={(id) => {
              if (id != null && !seleccion.includes(id)) setSeleccion([...seleccion, id]);
            }}
            placeholder="Agregar sucursal…"
          />
        </div>
      </div>
    </TarjetaAlcance>
  );
}

// ─── Canales de venta ────────────────────────────────────────────────

function TarjetaCanales(props: {
  cajaId: string;
  inicial: number[];
  nombres: Map<number, string | null>;
}) {
  const { seleccion, setSeleccion, dirty, reset } = useSeleccion(props.inicial);
  const reemplazar = useReemplazarCanalesCaja();

  return (
    <TarjetaAlcance
      titulo="Canales de venta del alcance"
      hint="Vacío = todos los canales (comodín). El alcance efectivo es sucursales × canales."
      dirty={dirty}
      guardando={reemplazar.isPending}
      onGuardar={() =>
        reemplazar.mutate(
          { id: props.cajaId, body: { canalVentaIds: [...seleccion] } },
          toastResultado('el alcance de canales'),
        )
      }
      onCancelar={reset}
    >
      <div className="flex flex-wrap items-center gap-2">
        {seleccion.length === 0 && (
          <span className="text-xs italic text-muted-foreground">Todos los canales</span>
        )}
        {seleccion.map((id) => (
          <Chip
            key={id}
            etiqueta={props.nombres.get(id) ?? `Canal ${id}`}
            onQuitar={() => setSeleccion(seleccion.filter((c) => c !== id))}
          />
        ))}
        <div className="w-64">
          <CanalVentaSelector
            value={null}
            onChange={(id) => {
              if (!seleccion.includes(id)) setSeleccion([...seleccion, id]);
            }}
          />
        </div>
      </div>
    </TarjetaAlcance>
  );
}

// ─── Cajeros relacionados ────────────────────────────────────────────

function TarjetaUsuarios(props: { cajaId: string; inicial: string[] }) {
  const { seleccion, setSeleccion, dirty, reset } = useSeleccion(props.inicial);
  const reemplazar = useReemplazarUsuariosCaja();
  const usuarios = useUsuarios();
  const nombrePorId = useMemo(
    () => new Map((usuarios.data?.items ?? []).map((u) => [u.id, u.nombre])),
    [usuarios.data],
  );

  return (
    <TarjetaAlcance
      titulo="Cajeros relacionados"
      hint="Pueden abrir sesión de esta caja sin autorización de supervisor (12-cajas.md §5.3)."
      dirty={dirty}
      guardando={reemplazar.isPending}
      onGuardar={() =>
        reemplazar.mutate(
          { id: props.cajaId, body: { usuarioIds: [...seleccion] } },
          toastResultado('los cajeros de la caja'),
        )
      }
      onCancelar={reset}
    >
      <div className="flex flex-wrap items-center gap-2">
        {seleccion.length === 0 && (
          <span className="text-xs italic text-muted-foreground">
            Sin cajeros relacionados (solo apertura con autorización).
          </span>
        )}
        {seleccion.map((id) => (
          <Chip
            key={id}
            etiqueta={nombrePorId.get(id) ?? id}
            onQuitar={() => setSeleccion(seleccion.filter((u) => u !== id))}
          />
        ))}
        <div className="w-64">
          <UsuarioSelector
            value={null}
            onChange={(id) => {
              if (id != null && !seleccion.includes(id)) setSeleccion([...seleccion, id]);
            }}
            placeholder="Agregar cajero…"
          />
        </div>
      </div>
    </TarjetaAlcance>
  );
}

// Nota: los editores usan `key` con la versión de la caja (ver DetalleCaja),
// así el estado local se re-inicializa tras cada guardado.
