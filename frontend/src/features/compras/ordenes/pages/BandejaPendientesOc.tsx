import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Inbox, Clock } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
  EstadoBadge,
  DateTimeDisplay,
  MoneyDisplay,
  PaginacionTabla,
} from '@/components/erp';
import {
  usePendientesAutorizacionOc,
  type NivelAutorizacionFiltro,
  type OrdenPendienteAutorizacion,
} from '@/features/compras/ordenes/api/usePendientesAutorizacionOc';
import {
  useAutorizarMasivoOc,
  type AutorizacionMasivaOcItemResultado,
} from '@/features/compras/ordenes/api/useAutorizarMasivoOc';
import { IndicadoresOc } from '@/features/compras/ordenes/components/IndicadoresOc';
import { ResumenAprobacionMasiva } from '@/features/compras/components/ResumenAprobacionMasiva';
import {
  resultadosConFolio,
  type ResultadoAprobacionItem,
} from '@/features/compras/lib/resultados-masivos';
import { NivelAutorizacion } from '@/features/compras/ordenes/schemas/autorizar';
import { useProveedores, useUsuarios, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { parseDateOnlyLocal } from '@/lib/datetime';
import { useHasPermission, useHasAnyPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EstadoOrdenCompra } from '@/features/compras/ordenes/api/types';
import { cn } from '@/lib/utils';
import { nombreUsuarioOGuion } from '@/lib/nombre-usuario';

/**
 * <c>P2 Bandeja de pendientes de autorización OC</c> (UF4-PR1).
 *
 * <para>Filtrado server-side por nivel — el endpoint
 * <c>/pendientes-autorizacion</c> acepta <c>?nivel=Nivel1|Nivel2</c>
 * (mapea a estados <c>EnAutorizacionJefeCompras</c> /
 * <c>EnAutorizacionDireccion</c>). Orden FIFO (más viejas arriba).</para>
 *
 * <para>Cada fila muestra a quién se compra, cantidad, monto e indicadores
 * (proveedor conocido, recurrente, menor cuantía, falta N2) para decidir sin
 * abrir el detalle. Selección múltiple + «Aprobar seleccionadas» y un botón
 * «Aprobar» rápido por fila para menor cuantía o proveedor conocido. Ambos
 * usan <c>autorizaciones-masivas</c>; el resultado se muestra por fila. Cada
 * pestaña es un solo nivel, así que el lote siempre va en un único POST.</para>
 *
 * <para>Las acciones solo aparecen si el usuario tiene el permiso del nivel
 * de la pestaña activa (<c>autorizar-nivel1</c> / <c>autorizar-nivel2</c>).
 * El detalle (<c>Ver detalle</c>) sigue siendo el camino para rechazar o
 * para cancelaciones.</para>
 *
 * <para>Tab switcher: si el usuario tiene ambos permisos ve un switcher para
 * alternar entre las dos bandejas; si solo tiene uno, se fija al que aplica.</para>
 */
export function BandejaPendientesOc() {
  const canN1 = useHasPermission(PermisosCanonicos.ComprasOrdenesAutorizarNivel1);
  const canN2 = useHasPermission(PermisosCanonicos.ComprasOrdenesAutorizarNivel2);

  // Default: si solo tiene N1, fija a N1; solo N2, a N2; ambos, a N1.
  const nivelDefault: NivelAutorizacionFiltro = canN1 ? 'Nivel1' : 'Nivel2';
  const [nivelActivo, setNivelActivo] =
    useState<NivelAutorizacionFiltro>(nivelDefault);

  const tieneAmbos = canN1 && canN2;
  const puedeAutorizar = nivelActivo === 'Nivel1' ? canN1 : canN2;
  const nivelNumerico =
    nivelActivo === 'Nivel1' ? NivelAutorizacion.Nivel1 : NivelAutorizacion.Nivel2;

  // strict:false: la ruta valida page/pageSize; defensa si se monta sin search.
  const rawSearch = useSearch({ strict: false }) as { page?: number; pageSize?: number };
  const page = rawSearch.page ?? 1;
  const pageSize = rawSearch.pageSize ?? 25;
  const navigate = useNavigate();
  function setPagina(nextPage: number, nextPageSize: number) {
    void navigate({
      to: '/compras/ordenes/pendientes-autorizacion',
      search: { page: nextPage, pageSize: nextPageSize },
    });
  }

  function cambiarNivel(nivel: NivelAutorizacionFiltro) {
    setNivelActivo(nivel);
    if (page > 1) setPagina(1, pageSize);
  }

  const query = usePendientesAutorizacionOc({ nivel: nivelActivo, page, pageSize });
  // limit alto + includeInactivas para no perder proveedores
  // históricos en la columna proveedor de la bandeja (respaldo si el
  // backend no resolvió el nombre).
  const proveedoresQuery = useProveedores({
    limit: 1000,
    includeInactivas: true,
  });
  const usuariosQuery = useUsuarios();

  const autorizarMasivo = useAutorizarMasivoOc();
  const [seleccion, setSeleccion] = useState<ReadonlySet<string>>(
    () => new Set(),
  );
  const [resultados, setResultados] = useState<
    Record<string, AutorizacionMasivaOcItemResultado>
  >({});
  const [errorAprobacion, setErrorAprobacion] = useState<string | null>(null);
  const [resumen, setResumen] = useState<readonly ResultadoAprobacionItem[]>(
    [],
  );

  // Captura "ahora" una vez al montar — Date.now() es impuro y no puede
  // llamarse durante el render. El valor es solo informativo para
  // "días esperando" (proxy FIFO); no necesita re-renderearse en vivo.
  const [ahoraMs] = useState<number>(() => Date.now());

  const proveedoresMap = useMemo(
    () => mapById(proveedoresQuery.data?.items),
    [proveedoresQuery.data],
  );
  const usuariosMap = useMemo(
    () => mapById(usuariosQuery.data?.items),
    [usuariosQuery.data],
  );

  function resolverProveedor(oc: OrdenPendienteAutorizacion): string {
    if (oc.proveedorNombre) return oc.proveedorNombre;
    const p = proveedoresMap.get(oc.proveedorId);
    if (p == null) return oc.proveedorId.slice(0, 8) + '…';
    return p.nombreComercial ?? p.razonSocial;
  }

  function resolverUsuario(id: string): string {
    return nombreUsuarioOGuion(usuariosMap.get(id)?.nombre);
  }

  const items = query.data?.items ?? [];
  const autorizables = puedeAutorizar ? items : [];
  const seleccionadas = autorizables.filter((oc) => seleccion.has(oc.id));
  const todasSeleccionadas =
    autorizables.length > 0 && seleccionadas.length === autorizables.length;
  const algunaSeleccionada = seleccionadas.length > 0 && !todasSeleccionadas;

  function alternarSeleccion(id: string, on: boolean) {
    setResumen([]);
    setSeleccion((prev) => {
      const next = new Set(prev);
      if (on) next.add(id);
      else next.delete(id);
      return next;
    });
  }

  function alternarTodas(on: boolean) {
    setResumen([]);
    setSeleccion(on ? new Set(autorizables.map((oc) => oc.id)) : new Set());
  }

  /**
   * Aprueba un lote con el nivel de la pestaña. Cada id devuelve su propio
   * resultado; se muestra por fila, en el resumen persistente y las aprobadas
   * salen de la selección.
   */
  async function aprobar(ocs: readonly OrdenPendienteAutorizacion[]) {
    setErrorAprobacion(null);
    setResumen([]);
    const folioPorId = new Map(ocs.map((oc) => [oc.id, oc.folio]));
    try {
      const respuesta = await autorizarMasivo.mutateAsync({
        ids: ocs.map((oc) => oc.id),
        nivel: nivelNumerico,
        idempotencyKey: crypto.randomUUID(),
      });
      const porId = Object.fromEntries(
        respuesta.resultados.map((x) => [x.id, x]),
      );
      setResultados((prev) => ({ ...prev, ...porId }));
      setResumen(resultadosConFolio(respuesta.resultados, folioPorId));
      const okIds = new Set(
        respuesta.resultados.filter((x) => x.ok).map((x) => x.id),
      );
      setSeleccion((prev) => new Set([...prev].filter((id) => !okIds.has(id))));
    } catch {
      setErrorAprobacion(
        'No se pudo completar la aprobación. Revisa la bandeja e intenta de nuevo.',
      );
    }
  }

  if (query.isLoading) {
    return (
      <div className="space-y-4 p-6">
        <header>
          <h1 className="text-2xl font-semibold tracking-tight">
            Pendientes de autorización
          </h1>
        </header>
        <TableSkeleton rows={8} />
      </div>
    );
  }

  if (query.error != null) {
    return (
      <div className="p-6">
        <ErrorState
          title="No se pudieron cargar las OCs pendientes"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => {
            void query.refetch();
          }}
        />
      </div>
    );
  }

  return (
    <div className="space-y-4 p-6" data-component="bandeja-pendientes-oc">
      <header className="flex flex-wrap items-baseline justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Pendientes de autorización
          </h1>
          <p className="text-sm text-muted-foreground">
            Órdenes esperando firma{' '}
            {nivelActivo === 'Nivel1' ? 'Nivel 1 (Jefe Compras)' : 'Nivel 2 (Dirección)'}.
            Orden FIFO — las más viejas aparecen arriba.
          </p>
        </div>
        {tieneAmbos && (
          <nav
            role="tablist"
            aria-label="Nivel de autorización"
            className="flex gap-1 border-b"
          >
            <NivelTab
              activo={nivelActivo === 'Nivel1'}
              onClick={() => cambiarNivel('Nivel1')}
              label="Nivel 1 (Jefe Compras)"
            />
            <NivelTab
              activo={nivelActivo === 'Nivel2'}
              onClick={() => cambiarNivel('Nivel2')}
              label="Nivel 2 (Dirección)"
            />
          </nav>
        )}
      </header>

      <ResumenAprobacionMasiva
        resultados={resumen}
        onCerrar={() => setResumen([])}
      />

      {(seleccionadas.length > 0 || errorAprobacion) && (
        <div className="flex flex-wrap items-center gap-2">
          {seleccionadas.length > 0 && (
            <Button
              type="button"
              size="sm"
              disabled={autorizarMasivo.isPending}
              onClick={() => aprobar(seleccionadas)}
            >
              Aprobar seleccionadas ({seleccionadas.length})
            </Button>
          )}
          {errorAprobacion && (
            <p role="alert" className="text-sm text-danger-fg">
              {errorAprobacion}
            </p>
          )}
        </div>
      )}

      {items.length === 0 ? (
        <EmptyState
          icon={<Inbox className="h-10 w-10" />}
          title={`Sin OCs pendientes en ${nivelActivo === 'Nivel1' ? 'Nivel 1' : 'Nivel 2'}.`}
          description="Cuando lleguen, las verás aquí ordenadas por antigüedad."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border bg-card">
          <table className="w-full min-w-[1100px] text-sm">
            <thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="w-10 px-3 py-2 text-left">
                  <Checkbox
                    checked={
                      todasSeleccionadas ? true : algunaSeleccionada ? 'indeterminate' : false
                    }
                    onCheckedChange={(v) => alternarTodas(v === true)}
                    disabled={autorizables.length === 0}
                    aria-label="Seleccionar todas las OC autorizables"
                  />
                </th>
                <th className="px-3 py-2 text-left font-medium">Folio</th>
                <th className="px-3 py-2 text-left font-medium">Proveedor</th>
                <th className="px-3 py-2 text-right font-medium">Cantidad</th>
                <th className="px-3 py-2 text-right font-medium">Monto total</th>
                <th className="px-3 py-2 text-left font-medium">Comprador</th>
                <th className="px-3 py-2 text-left font-medium">Fecha doc.</th>
                <th className="px-3 py-2 text-left font-medium">Días esperando</th>
                <th className="px-3 py-2 text-left font-medium">Indicadores</th>
                <th className="px-3 py-2 text-left font-medium">Estado</th>
                <th className="px-3 py-2 text-right font-medium">Acción</th>
              </tr>
            </thead>
            <tbody>
              {items.map((oc, i) => (
                <FilaOc
                  key={oc.id}
                  oc={oc}
                  bgClass={i % 2 === 1 ? 'bg-muted/20' : undefined}
                  proveedor={resolverProveedor(oc)}
                  comprador={resolverUsuario(oc.compradorTitularId)}
                  ahoraMs={ahoraMs}
                  seleccionada={seleccion.has(oc.id)}
                  puedeAutorizar={puedeAutorizar}
                  bloqueado={autorizarMasivo.isPending}
                  resultado={resultados[oc.id]}
                  onSeleccion={(on) => alternarSeleccion(oc.id, on)}
                  onAprobarRapido={() => aprobar([oc])}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}

      {((query.data?.totalCount ?? 0) > 0 || page > 1) && (
        <PaginacionTabla
          total={query.data?.totalCount ?? 0}
          offset={(page - 1) * pageSize}
          limit={pageSize}
          onChange={(offset, limit) => setPagina(Math.floor(offset / limit) + 1, limit)}
        />
      )}
    </div>
  );
}

// ─── Subcomponentes ───────────────────────────────────────────────

function NivelTab({
  activo,
  onClick,
  label,
}: {
  activo: boolean;
  onClick: () => void;
  label: string;
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={activo}
      onClick={onClick}
      className={cn(
        '-mb-px border-b-2 px-3 py-1.5 text-sm font-medium transition-colors',
        activo
          ? 'border-primary text-primary'
          : 'border-transparent text-muted-foreground hover:text-foreground',
      )}
    >
      {label}
    </button>
  );
}

function FilaOc({
  oc,
  bgClass,
  proveedor,
  comprador,
  ahoraMs,
  seleccionada,
  puedeAutorizar,
  bloqueado,
  resultado,
  onSeleccion,
  onAprobarRapido,
}: {
  oc: OrdenPendienteAutorizacion;
  bgClass?: string;
  proveedor: string;
  comprador: string;
  ahoraMs: number;
  seleccionada: boolean;
  puedeAutorizar: boolean;
  bloqueado: boolean;
  resultado: AutorizacionMasivaOcItemResultado | undefined;
  onSeleccion: (on: boolean) => void;
  onAprobarRapido: () => void;
}) {
  // Días esperando = days since fechaDocumento (proxy razonable hasta
  // que F7-PR3 exponga el timestamp de la última transición a
  // EnAutorización; suficiente para FIFO visual).
  // fechaDocumento es date-only (ADR-0040); parsear como medianoche local
  // para no correr un día con new Date('YYYY-MM-DD') (medianoche UTC).
  const fechaDoc = parseDateOnlyLocal(oc.fechaDocumento).getTime();
  const diasEsperando = Math.max(
    0,
    Math.floor((ahoraMs - fechaDoc) / (1000 * 60 * 60 * 24)),
  );
  const aprobableRapido = puedeAutorizar && (oc.esMenorCuantia || oc.proveedorConocido);

  return (
    <tr className={cn(bgClass)} data-oc={oc.id}>
      <td className="px-3 py-2">
        <Checkbox
          checked={seleccionada}
          onCheckedChange={(v) => onSeleccion(v === true)}
          disabled={!puedeAutorizar}
          aria-label={`Seleccionar ${oc.folio}`}
        />
      </td>
      <td className="px-3 py-2 font-mono text-xs">{oc.folio}</td>
      <td className="px-3 py-2">{proveedor}</td>
      <td className="px-3 py-2 text-right tabular-nums">
        {oc.cantidadTotal.toLocaleString('es-MX')}
        <span className="block text-xs text-muted-foreground">
          {oc.numeroLineas} {oc.numeroLineas === 1 ? 'línea' : 'líneas'}
        </span>
      </td>
      <td className="px-3 py-2 text-right">
        <MoneyDisplay amount={oc.totalAPagar} currency={oc.moneda} />
      </td>
      <td className="px-3 py-2 text-muted-foreground">{comprador}</td>
      <td className="px-3 py-2">
        <DateTimeDisplay value={oc.fechaDocumento} />
      </td>
      <td
        className={cn(
          'px-3 py-2 tabular-nums',
          diasEsperando >= 7 && 'text-warning-fg font-medium',
          diasEsperando >= 14 && 'text-danger-fg font-semibold',
        )}
      >
        <span className="inline-flex items-center gap-1">
          <Clock className="h-3 w-3" aria-hidden="true" />
          {diasEsperando} {diasEsperando === 1 ? 'día' : 'días'}
        </span>
      </td>
      <td className="px-3 py-2">
        <IndicadoresOc oc={oc} />
      </td>
      <td className="px-3 py-2">
        <EstadoBadge tipo="orden-compra" estado={oc.estado} />
      </td>
      <td className="px-3 py-2 text-right">
        <div className="flex flex-wrap items-center justify-end gap-2">
          {resultado && <ResultadoFila resultado={resultado} />}
          {aprobableRapido && (
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={bloqueado}
              aria-label={`Aprobar ${oc.folio}`}
              onClick={onAprobarRapido}
            >
              Aprobar
            </Button>
          )}
          <Button asChild size="sm" variant="ghost">
            <Link
              to="/compras/ordenes/$id"
              params={{ id: oc.id }}
              data-action="ver-detalle"
            >
              {oc.estado === EstadoOrdenCompra.CancelacionSolicitada ? 'Confirmar / Rechazar cancelación' : 'Ver detalle'}
            </Link>
          </Button>
        </div>
      </td>
    </tr>
  );
}

/** Resultado por fila de la aprobación masiva (accesible: role="status"). */
function ResultadoFila({ resultado }: { resultado: AutorizacionMasivaOcItemResultado }) {
  return (
    <span
      role="status"
      className={resultado.ok ? 'text-xs text-success-fg' : 'text-xs text-danger-fg'}
    >
      {resultado.ok ? 'Aprobada' : (resultado.mensaje ?? 'Error al aprobar')}
    </span>
  );
}

/**
 * Gate para el sub-item del sidebar: si no tiene ningún permiso de
 * autorización, no debería ver la entrada. El componente acepta
 * children y devuelve null sin permiso (o lo renderea sino).
 */
export function GateBandejaPendientesOc({
  children,
}: {
  children: React.ReactNode;
}) {
  const tienePermiso = useHasAnyPermission([
    PermisosCanonicos.ComprasOrdenesAutorizarNivel1,
    PermisosCanonicos.ComprasOrdenesAutorizarNivel2,
  ]);
  if (!tienePermiso) return null;
  return <>{children}</>;
}
