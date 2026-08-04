import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { MoreVertical, Upload } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  EmptyState,
  ErrorState,
  SortableHeader,
  TableSkeleton,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { useCfdis } from '@/features/cxp/api/useCfdis';
import {
  CanalOrigenCfdiLabels,
  EstadoCfdiRecibido,
  TipoCfdi,
  TipoCfdiLabels,
  type CfdiListItem,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoCfdiBadge } from '@/features/cxp/components/EstadoCfdiBadge';
import { CapturarFacturaSheet } from '@/features/cxp/components/CapturarFacturaSheet';
import { CargarCfdiSheet } from '@/features/cxp/components/CargarCfdiSheet';
import { CfdiDetalleSheet } from '@/features/cxp/components/CfdiDetalleSheet';
import { DescartarCfdiSheet } from '@/features/cxp/components/DescartarCfdiSheet';
import { MarcarDuplicadoSheet } from '@/features/cxp/components/MarcarDuplicadoSheet';
import type { CfdisSearch } from '@/features/cxp/lib/cfdis-search-schema';

const FROM = '/_app/cxp/cfdis' as const;
const SENTINEL_ALL = '__all__';
const DIAS_VENCIDO_UMBRAL = 5;

type SortKey =
  | 'uuidCfdi'
  | 'rfcEmisor'
  | 'tipo'
  | 'folio'
  | 'fechaCfdi'
  | 'total'
  | 'fechaRecepcion'
  | 'estado';

/**
 * <c>P2 — Bandeja de CFDIs recibidos</c> (doc 07 §FE-F1-PR1).
 * Tabla con filtros server-side (estado, tipo, RFC emisor) + filtro
 * client-side de búsqueda libre por UUID/folio + filtro "Sólo vencidos
 * &gt; 5 días" (calculado client-side sobre <c>fechaRecepcion</c>).
 *
 * <para>Acciones por fila (dropdown): Marcar duplicado, Descartar.
 * Ambas sólo aplican a CFDIs en <c>PorProcesar</c>; las demás filas
 * tienen el menú deshabilitado (el backend bloquea de todas formas).</para>
 *
 * <para>Gateada por <c>cuentas_por_pagar.cfdis.leer</c> a nivel ruta.</para>
 *
 * <para>PLATFORM-TODO(&lt;CfdiBlobDownload&gt;): cuando el backend
 * exponga <c>GET /cfdis/{id}</c> con descarga de XML/PDF, agregar
 * link al detalle en la columna UUID, componente
 * <c>&lt;CfdiXmlViewer&gt;</c> y <c>&lt;CfdiPdfPreview&gt;</c>.</para>
 */
export function CfdisPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeDescartar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarCfdisDescartar,
  );
  const puedeCargarManual = useHasPermission(
    PermisosCanonicos.CuentasPorPagarCfdisCargarManual,
  );
  const puedeCapturarFactura = useHasPermission(
    PermisosCanonicos.CuentasPorPagarFacturasCapturar,
  );

  const [cargarOpen, setCargarOpen] = useState(false);
  const [detalleTarget, setDetalleTarget] = useState<CfdiListItem | null>(
    null,
  );
  const [capturarTarget, setCapturarTarget] = useState<CfdiListItem | null>(
    null,
  );
  const [descartarTarget, setDescartarTarget] = useState<CfdiListItem | null>(
    null,
  );
  const [duplicadoTarget, setDuplicadoTarget] = useState<CfdiListItem | null>(
    null,
  );

  const query = useCfdis({
    estado: search.estado,
    tipo: search.tipo,
    rfcEmisor: search.rfcEmisor,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<CfdisSearch>) {
    navigate({
      to: '/cxp/cfdis',
      search: { ...search, ...parcial },
    });
  }

  // Filtro client-side: búsqueda por UUID/folio + vencidos > N días.
  const itemsFiltrados = useMemo(() => {
    let items = query.data?.items ?? [];
    if (search.q) {
      const needle = search.q.toLowerCase();
      items = items.filter(
        (c) =>
          c.uuidCfdi.toLowerCase().includes(needle) ||
          (c.folio ?? '').toLowerCase().includes(needle),
      );
    }
    if (search.soloVencidos) {
      const ahora = new Date().getTime();
      items = items.filter((c) => {
        if (c.estado !== EstadoCfdiRecibido.PorProcesar) return false;
        const diasDesde =
          (ahora - Date.parse(c.fechaRecepcion)) / (1000 * 60 * 60 * 24);
        return diasDesde > DIAS_VENCIDO_UMBRAL;
      });
    }
    return items;
  }, [query.data, search.q, search.soloVencidos]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            CFDIs recibidos
          </h1>
          <p className="text-sm text-muted-foreground">
            Bandeja paginada del CfdiRecibido. Marca duplicados, descarta
            CFDIs no aplicables o abre el flujo de captura desde aquí.
          </p>
        </div>
        {puedeCargarManual && (
          <Button onClick={() => setCargarOpen(true)}>
            <Upload className="mr-2 h-4 w-4" />
            Cargar CFDI
          </Button>
        )}
      </div>

      <FiltrosToolbar search={search} onChange={actualizarSearch} />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los CFDIs"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={8}
          columns={[
            { width: 'w-48' },
            { width: 'w-32' },
            { width: 'w-24' },
            { width: 'w-24' },
            { width: 'w-32' },
            { width: 'w-28' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-8' },
          ]}
        />
      ) : itemsFiltrados.length === 0 ? (
        <EmptyState
          title="Sin CFDIs"
          description={
            search.q ||
            search.estado != null ||
            search.tipo != null ||
            search.rfcEmisor ||
            search.soloVencidos
              ? 'No hay CFDIs que coincidan con los filtros aplicados.'
              : 'Aún no se han recibido CFDIs. Los CFDIs entran por descarga SAT, mailbox o carga manual.'
          }
        />
      ) : (
        <TablaCfdis
          items={itemsFiltrados}
          puedeDescartar={puedeDescartar}
          puedeCapturarFactura={puedeCapturarFactura}
          onVerDetalle={setDetalleTarget}
          onDescartar={setDescartarTarget}
          onMarcarDuplicado={setDuplicadoTarget}
          onCapturarFactura={setCapturarTarget}
        />
      )}

      <CargarCfdiSheet open={cargarOpen} onOpenChange={setCargarOpen} />
      <CfdiDetalleSheet
        open={detalleTarget !== null}
        onOpenChange={(open) => !open && setDetalleTarget(null)}
        cfdi={detalleTarget}
      />
      <CapturarFacturaSheet
        open={capturarTarget !== null}
        onOpenChange={(open) => !open && setCapturarTarget(null)}
        cfdiPreseleccionado={capturarTarget}
      />
      <DescartarCfdiSheet
        open={descartarTarget !== null}
        onOpenChange={(open) => !open && setDescartarTarget(null)}
        cfdi={descartarTarget}
      />
      <MarcarDuplicadoSheet
        open={duplicadoTarget !== null}
        onOpenChange={(open) => !open && setDuplicadoTarget(null)}
        cfdi={duplicadoTarget}
      />
    </div>
  );
}

interface FiltrosToolbarProps {
  search: CfdisSearch;
  onChange: (parcial: Partial<CfdisSearch>) => void;
}

function FiltrosToolbar({ search, onChange }: FiltrosToolbarProps) {
  const algunFiltro =
    search.q ||
    search.estado != null ||
    search.tipo != null ||
    search.rfcEmisor ||
    search.soloVencidos;

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">UUID o folio</label>
        <Input
          aria-label="Buscar por UUID o folio"
          value={search.q ?? ''}
          onChange={(e) => onChange({ q: e.target.value || undefined })}
          placeholder="Buscar…"
          className="w-56"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estado</label>
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              estado:
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as EstadoCfdiRecibido),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por estado" className="w-48">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(EstadoCfdiRecibido.PorProcesar)}>
              Por procesar
            </SelectItem>
            <SelectItem
              value={String(EstadoCfdiRecibido.ConvertidoEnPasivo)}
            >
              Convertido en pasivo
            </SelectItem>
            <SelectItem value={String(EstadoCfdiRecibido.Duplicado)}>
              Duplicado
            </SelectItem>
            <SelectItem value={String(EstadoCfdiRecibido.Descartado)}>
              Descartado
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Tipo</label>
        <Select
          value={search.tipo != null ? String(search.tipo) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              tipo:
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as CfdisSearch['tipo']),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por tipo" className="w-40">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(TipoCfdi.Ingreso)}>Ingreso</SelectItem>
            <SelectItem value={String(TipoCfdi.Egreso)}>Egreso</SelectItem>
            <SelectItem value={String(TipoCfdi.Pago)}>Pago (REP)</SelectItem>
            <SelectItem value={String(TipoCfdi.Traslado)}>
              Traslado
            </SelectItem>
            <SelectItem value={String(TipoCfdi.Nomina)}>Nómina</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">RFC emisor</label>
        <Input
          aria-label="Filtrar por RFC emisor"
          value={search.rfcEmisor ?? ''}
          onChange={(e) =>
            onChange({ rfcEmisor: e.target.value.toUpperCase() || undefined })
          }
          placeholder="XAX010101000"
          className="w-40 uppercase"
          maxLength={13}
        />
      </div>
      <div className="flex items-center gap-2 pt-5">
        <Checkbox
          id="soloVencidos"
          checked={search.soloVencidos ?? false}
          onCheckedChange={(v) =>
            onChange({ soloVencidos: v === true ? true : undefined })
          }
        />
        <Label htmlFor="soloVencidos" className="text-sm">
          Solo por procesar &gt; {DIAS_VENCIDO_UMBRAL} días
        </Label>
      </div>
      {algunFiltro && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              q: undefined,
              estado: undefined,
              tipo: undefined,
              rfcEmisor: undefined,
              soloVencidos: undefined,
            })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

interface TablaCfdisProps {
  items: readonly CfdiListItem[];
  puedeDescartar: boolean;
  puedeCapturarFactura: boolean;
  onVerDetalle: (cfdi: CfdiListItem) => void;
  onDescartar: (cfdi: CfdiListItem) => void;
  onMarcarDuplicado: (cfdi: CfdiListItem) => void;
  onCapturarFactura: (cfdi: CfdiListItem) => void;
}

function TablaCfdis({
  items,
  puedeDescartar,
  puedeCapturarFactura,
  onVerDetalle,
  onDescartar,
  onMarcarDuplicado,
  onCapturarFactura,
}: TablaCfdisProps) {
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const ordenados = useMemo(() => {
    if (sort == null) return items;
    return [...items].sort((a, b) =>
      compareItemsBy(a, b, sort.key, sort.dir),
    );
  }, [items, sort]);

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="uuidCfdi"
                label="UUID"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="rfcEmisor"
                label="RFC emisor"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="tipo"
                label="Tipo"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="folio"
                label="Folio"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="fechaCfdi"
                label="Fecha CFDI"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-right">
              <SortableHeader
                columnKey="total"
                label="Total"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">Canal</th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="fechaRecepcion"
                label="Recepción"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="estado"
                label="Estado"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 w-8" aria-label="Acciones" />
          </tr>
        </thead>
        <tbody>
          {ordenados.map((c) => {
            const esPorProcesar =
              c.estado === EstadoCfdiRecibido.PorProcesar;
            const gestionHabilitada = esPorProcesar && puedeDescartar;
            // Solo CFDIs de ingreso pendientes se convierten en pasivo.
            const capturaHabilitada =
              esPorProcesar &&
              c.tipo === TipoCfdi.Ingreso &&
              puedeCapturarFactura;
            return (
              <tr
                key={c.id}
                className="border-t hover:bg-muted/30 focus-within:bg-muted/30"
              >
                <td className="px-3 py-2 font-mono text-xs">
                  {abreviar(c.uuidCfdi)}
                </td>
                <td className="px-3 py-2 font-mono text-xs">{c.rfcEmisor}</td>
                <td className="px-3 py-2">{TipoCfdiLabels[c.tipo]}</td>
                <td className="px-3 py-2 font-mono text-xs">
                  {c.serie ? `${c.serie}-${c.folio ?? ''}` : c.folio ?? '—'}
                </td>
                <td className="px-3 py-2 whitespace-nowrap">
                  {formatearFecha(c.fechaCfdi)}
                </td>
                <td className="px-3 py-2 text-right font-mono">
                  {formatearMonto(c.total, c.moneda)}
                </td>
                <td className="px-3 py-2 text-xs text-muted-foreground">
                  {CanalOrigenCfdiLabels[c.canalOrigen]}
                </td>
                <td className="px-3 py-2 whitespace-nowrap text-xs">
                  {formatearFecha(c.fechaRecepcion)}
                </td>
                <td className="px-3 py-2">
                  <EstadoCfdiBadge estado={c.estado} />
                </td>
                <td className="px-3 py-2">
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      {/* "Ver detalle" aplica a cualquier CFDI → el menú
                          queda siempre habilitado; cada item se deshabilita
                          según su propio permiso/estado. */}
                      <Button
                        variant="ghost"
                        size="icon"
                        aria-label={`Acciones para CFDI ${c.uuidCfdi}`}
                      >
                        <MoreVertical className="h-4 w-4" />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem onSelect={() => onVerDetalle(c)}>
                        Ver detalle
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        onSelect={() => onCapturarFactura(c)}
                        disabled={!capturaHabilitada}
                      >
                        Capturar factura
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        onSelect={() => onMarcarDuplicado(c)}
                        disabled={!gestionHabilitada}
                      >
                        Marcar duplicado
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        onSelect={() => onDescartar(c)}
                        disabled={!gestionHabilitada}
                        className="text-destructive focus:text-destructive"
                      >
                        Descartar
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function abreviar(uuid: string): string {
  return uuid.length > 12 ? `${uuid.slice(0, 8)}…${uuid.slice(-4)}` : uuid;
}

function formatearMonto(v: number, moneda: string): string {
  try {
    return new Intl.NumberFormat('es-MX', {
      style: 'currency',
      currency: moneda,
      minimumFractionDigits: 2,
    }).format(v);
  } catch {
    return `${v.toFixed(2)} ${moneda}`;
  }
}

function formatearFecha(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString('es-MX', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    });
  } catch {
    return iso;
  }
}
