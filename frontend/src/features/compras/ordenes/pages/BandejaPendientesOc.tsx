import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Inbox, Clock } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
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
import { PageHeader } from '@/components/erp/PageHeader';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';

/**
 * <c>P2 Bandeja de pendientes de autorización OC</c> (UF4-PR1).
 *
 * <para>Filtrado server-side por nivel — el endpoint
 * <c>/pendientes-autorizacion</c> acepta <c>?nivel=Nivel1|Nivel2</c>
 * (mapea a estados <c>EnAutorizacionJefeCompras</c> /
 * <c>EnAutorizacionDireccion</c>). Las más antiguas aparecen primero.</para>
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
  const [nivelActivo, setNivelActivo] = useState<NivelAutorizacionFiltro>(nivelDefault);

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
  const [seleccion, setSeleccion] = useState<ReadonlySet<string>>(() => new Set());
  const [resultados, setResultados] = useState<Record<string, AutorizacionMasivaOcItemResultado>>(
    {},
  );
  const [errorAprobacion, setErrorAprobacion] = useState<string | null>(null);
  const [resumen, setResumen] = useState<readonly ResultadoAprobacionItem[]>([]);
  const [porConfirmar, setPorConfirmar] = useState<readonly OrdenPendienteAutorizacion[]>([]);
  const totalesPorMoneda = new Map<string, number>();
  for (const oc of porConfirmar) {
    totalesPorMoneda.set(oc.moneda, (totalesPorMoneda.get(oc.moneda) ?? 0) + oc.totalAPagar);
  }

  // Captura "ahora" una vez al montar — Date.now() es impuro y no puede
  // llamarse durante el render. El valor es solo informativo para
  // "días esperando" (proxy FIFO); no necesita re-renderearse en vivo.
  const [ahoraMs] = useState<number>(() => Date.now());

  const proveedoresMap = useMemo(
    () => mapById(proveedoresQuery.data?.items),
    [proveedoresQuery.data],
  );
  const usuariosMap = useMemo(() => mapById(usuariosQuery.data?.items), [usuariosQuery.data]);

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
      const porId = Object.fromEntries(respuesta.resultados.map((x) => [x.id, x]));
      setResultados((prev) => ({ ...prev, ...porId }));
      setResumen(resultadosConFolio(respuesta.resultados, folioPorId));
      const okIds = new Set(respuesta.resultados.filter((x) => x.ok).map((x) => x.id));
      setSeleccion((prev) => new Set([...prev].filter((id) => !okIds.has(id))));
    } catch {
      setErrorAprobacion(
        'No se pudo completar la aprobación. Revisa la bandeja e intenta de nuevo.',
      );
    }
  }

  return (
    <Tabs
      value={nivelActivo}
      onValueChange={(v) => cambiarNivel(v as NivelAutorizacionFiltro)}
      className="space-y-4"
      data-component="bandeja-pendientes-oc"
    >
      <PageHeader
        titulo="OC por autorizar"
        descripcion={`Órdenes esperando firma ${nivelActivo === 'Nivel1' ? 'Nivel 1 (Jefe Compras)' : 'Nivel 2 (Dirección)'}, las más viejas primero.`}
        acciones={
          tieneAmbos && (
            <TabsList aria-label="Nivel de autorización" className="flex gap-1 border-b">
              <TabsTrigger value="Nivel1">Nivel 1 (Jefe Compras)</TabsTrigger>
              <TabsTrigger value="Nivel2">Nivel 2 (Dirección)</TabsTrigger>
            </TabsList>
          )
        }
      />

      <TabsContent value={nivelActivo} className="mt-0 space-y-4">
        <ListadoModulo
          pie={
            <PaginacionTabla
              total={query.data?.totalCount ?? 0}
              offset={(page - 1) * pageSize}
              limit={pageSize}
              onChange={(offset, limit) => setPagina(Math.floor(offset / limit) + 1, limit)}
            />
          }
        >
          {query.isLoading ? (
            <TableSkeleton rows={8} />
          ) : query.error != null ? (
            <ErrorState
              title="No se pudieron cargar las OCs pendientes"
              problem={esApiError(query.error) ? query.error.problem : undefined}
              onRetry={() => {
                void query.refetch();
              }}
            />
          ) : (
            <>
              <ResumenAprobacionMasiva resultados={resumen} onCerrar={() => setResumen([])} />

              <ConfirmarAccionDialog
                open={porConfirmar.length > 0}
                onOpenChange={(v) => {
                  if (!v) setPorConfirmar([]);
                }}
                titulo={`Aprobar ${porConfirmar.length} OC`}
                resumen={
                  <p>
                    Vas a firmar {nivelActivo === 'Nivel1' ? 'el Nivel 1' : 'el Nivel 2'} de{' '}
                    {porConfirmar.length} {porConfirmar.length === 1 ? 'orden' : 'órdenes'} por un
                    total de{' '}
                    {[...totalesPorMoneda].map(([moneda, total], i) => (
                      <span key={moneda}>
                        {i > 0 && ' + '}
                        <MoneyDisplay amount={total} currency={moneda} />
                      </span>
                    ))}
                    . Tu firma queda registrada con tu usuario y no se puede deshacer.
                  </p>
                }
                verbo={`Aprobar ${porConfirmar.length} OC`}
                tono="primario"
                pendiente={autorizarMasivo.isPending}
                onConfirmar={() => {
                  const lote = porConfirmar;
                  setPorConfirmar([]);
                  void aprobar(lote);
                }}
              />

              {(seleccionadas.length > 0 || errorAprobacion) && (
                <div className="flex flex-wrap items-center gap-2">
                  {seleccionadas.length > 0 && (
                    <Button
                      type="button"
                      size="sm"
                      disabled={autorizarMasivo.isPending}
                      onClick={() => setPorConfirmar(seleccionadas)}
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
                <div className="overflow-x-auto rounded-md border bg-surface-card">
                  <Table className="w-full min-w-full text-sm">
                    <TableHeader className="bg-surface-muted/50 text-xs uppercase tracking-wide text-ink-muted">
                      <TableRow>
                        <TableHead className="w-10 px-3 py-2 text-left">
                          <Checkbox
                            checked={
                              todasSeleccionadas
                                ? true
                                : algunaSeleccionada
                                  ? 'indeterminate'
                                  : false
                            }
                            onCheckedChange={(v) => alternarTodas(v === true)}
                            disabled={autorizables.length === 0}
                            aria-label="Seleccionar todas las OC autorizables"
                          />
                        </TableHead>
                        <TableHead className="px-3 py-2 text-left font-medium">Folio</TableHead>
                        <TableHead className="px-3 py-2 text-left font-medium">Proveedor</TableHead>
                        <TableHead className="px-3 py-2 text-right font-medium">Cantidad</TableHead>
                        <TableHead className="px-3 py-2 text-right font-medium">
                          Monto total
                        </TableHead>
                        <TableHead className="px-3 py-2 text-left font-medium">Comprador</TableHead>
                        <TableHead className="px-3 py-2 text-left font-medium">
                          Fecha doc.
                        </TableHead>
                        <TableHead className="px-3 py-2 text-left font-medium">
                          Días esperando
                        </TableHead>
                        <TableHead className="px-3 py-2 text-left font-medium">
                          Indicadores
                        </TableHead>
                        <TableHead className="px-3 py-2 text-left font-medium">Estado</TableHead>
                        <TableHead className="px-3 py-2 text-right font-medium">Acción</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody className="[&_tr]:h-12">
                      {items.map((oc, i) => (
                        <FilaOc
                          key={oc.id}
                          oc={oc}
                          bgClass={i % 2 === 1 ? 'bg-surface-muted/20' : undefined}
                          proveedor={resolverProveedor(oc)}
                          comprador={resolverUsuario(oc.compradorTitularId)}
                          ahoraMs={ahoraMs}
                          seleccionada={seleccion.has(oc.id)}
                          puedeAutorizar={puedeAutorizar}
                          bloqueado={autorizarMasivo.isPending}
                          resultado={resultados[oc.id]}
                          onSeleccion={(on) => alternarSeleccion(oc.id, on)}
                          onAprobarRapido={() => setPorConfirmar([oc])}
                        />
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
            </>
          )}
        </ListadoModulo>
      </TabsContent>
    </Tabs>
  );
}

// ─── Subcomponentes ───────────────────────────────────────────────

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
  const diasEsperando = Math.max(0, Math.floor((ahoraMs - fechaDoc) / (1000 * 60 * 60 * 24)));
  const aprobableRapido = puedeAutorizar && (oc.esMenorCuantia || oc.proveedorConocido);

  return (
    <TableRow className={cn(bgClass)} data-oc={oc.id}>
      <TableCell className="px-3 py-2">
        <Checkbox
          checked={seleccionada}
          onCheckedChange={(v) => onSeleccion(v === true)}
          disabled={!puedeAutorizar}
          aria-label={`Seleccionar ${oc.folio}`}
        />
      </TableCell>
      <TableCell className="px-3 py-2 font-mono text-xs">{oc.folio}</TableCell>
      <TableCell className="px-3 py-2">{proveedor}</TableCell>
      <TableCell className="px-3 py-2 text-right tabular-nums">
        {oc.cantidadTotal.toLocaleString('es-MX')}
        <span className="block text-xs text-ink-muted">
          {oc.numeroLineas} {oc.numeroLineas === 1 ? 'partida' : 'partidas'}
        </span>
      </TableCell>
      <TableCell className="px-3 py-2 text-right">
        <MoneyDisplay amount={oc.totalAPagar} currency={oc.moneda} />
      </TableCell>
      <TableCell className="px-3 py-2 text-ink-muted">{comprador}</TableCell>
      <TableCell className="px-3 py-2">
        <DateTimeDisplay value={oc.fechaDocumento} />
      </TableCell>
      <TableCell
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
      </TableCell>
      <TableCell className="px-3 py-2">
        <IndicadoresOc oc={oc} />
      </TableCell>
      <TableCell className="px-3 py-2">
        <EstadoBadge tipo="orden-compra" estado={oc.estado} />
      </TableCell>
      <TableCell className="px-3 py-2 text-right">
        <div className="flex flex-wrap items-center justify-end gap-2">
          {resultado && <ResultadoFila resultado={resultado} />}
          {aprobableRapido && (
            <Button
              type="button"
              variant="secondary"
              size="sm"
              disabled={bloqueado}
              aria-label={`Aprobar ${oc.folio}`}
              onClick={onAprobarRapido}
            >
              Aprobar
            </Button>
          )}
          <Button asChild size="sm" variant="ghost">
            <Link to="/compras/ordenes/$id" params={{ id: oc.id }} data-action="ver-detalle">
              {oc.estado === EstadoOrdenCompra.CancelacionSolicitada
                ? 'Confirmar / Rechazar cancelación'
                : 'Ver detalle'}
            </Link>
          </Button>
        </div>
      </TableCell>
    </TableRow>
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
export function GateBandejaPendientesOc({ children }: { children: React.ReactNode }) {
  const tienePermiso = useHasAnyPermission([
    PermisosCanonicos.ComprasOrdenesAutorizarNivel1,
    PermisosCanonicos.ComprasOrdenesAutorizarNivel2,
  ]);
  if (!tienePermiso) return null;
  return <>{children}</>;
}
