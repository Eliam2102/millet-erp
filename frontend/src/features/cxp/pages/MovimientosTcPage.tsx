import { useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { AlertTriangle, MoreVertical, Plus, Undo2, Zap } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
  UsuarioSelector,
} from '@/components/erp';
import { TarjetaSelector } from '@/features/cxp/components/TarjetaSelector';
import { useMovimientosTc } from '@/features/cxp/api/useTarjetasCredito';
import {
  EstadoMovimientoTc,
  TipoMovimientoTc,
  TipoMovimientoTcLabels,
  type MovimientoTc,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoMovimientoTcChip } from '@/features/cxp/components/EstadoChips';
import { NuevoMovimientoTcSheet } from '@/features/cxp/components/NuevoMovimientoTcSheet';
import { RegistrarRefundTcSheet } from '@/features/cxp/components/RegistrarRefundTcSheet';
import { RegistrarMovimientoEspecialSheet } from '@/features/cxp/components/RegistrarMovimientoEspecialSheet';
import { DisputarMovimientoTcSheet } from '@/features/cxp/components/DisputarMovimientoTcSheet';
import type { MovimientosTcSearch } from '@/features/cxp/lib/tc-search-schema';

const FROM = '/_app/cxp/tc/movimientos' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P1 — Bandeja de Movimientos TC</c> (doc 07 §FE-F6-PR1).
 * Auxiliar ve todos; titular puede filtrar por <c>usuarioQueUsoId</c>
 * con su propio UUID.
 *
 * <para>PLATFORM-TODO(&lt;MovimientoTcCard&gt;): el spec menciona
 * &lt;MovimientoTcCard&gt; (vista cards alternativa a tabla); hoy
 * priorizamos la tabla por consistencia con resto del módulo. Cards
 * pueden entrar en hardening (FE-F8).</para>
 */
export function MovimientosTcPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeRegistrar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento,
  );
  const puedeDisputar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarTcDisputar,
  );
  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [refundTarget, setRefundTarget] = useState<MovimientoTc | null>(null);
  const [disputarTarget, setDisputarTarget] =
    useState<MovimientoTc | null>(null);
  const [especialAbierto, setEspecialAbierto] = useState(false);

  const query = useMovimientosTc({
    tarjetaId: search.tarjetaId,
    usuarioQueUsoId: search.usuarioQueUsoId,
    estado: search.estado,
    tipo: search.tipo,
    fechaDesde: search.fechaDesde,
    fechaHasta: search.fechaHasta,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<MovimientosTcSearch>) {
    navigate({ to: '/cxp/tc/movimientos', search: { ...search, ...parcial } });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Movimientos TC
          </h1>
          <p className="text-sm text-muted-foreground">
            Cargos a tarjetas. Flujo A genera factura automática; Flujo B
            solo registra el movimiento.
          </p>
        </div>
        {puedeRegistrar && (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button>
                <Plus className="mr-2 h-4 w-4" />
                Nuevo movimiento
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onSelect={() => setSheetAbierto(true)}>
                Compra (Flujo A/B)
              </DropdownMenuItem>
              <DropdownMenuItem onSelect={() => setEspecialAbierto(true)}>
                <Zap className="mr-2 h-3 w-3" />
                Especial (intereses/anualidad/comisión)
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>

      <Filtros search={search} onChange={actualizarSearch} />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los movimientos"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-24' },
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin movimientos"
          description="No hay movimientos con los filtros aplicados."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Fecha</th>
                <th className="px-3 py-2 text-left">Merchant</th>
                <th className="px-3 py-2 text-left">Concepto</th>
                <th className="px-3 py-2 text-right">Monto MXN</th>
                <th className="px-3 py-2 text-left">Tipo</th>
                <th className="px-3 py-2 text-left">Estado</th>
                <th className="px-3 py-2 text-left">Factura</th>
                <th className="px-3 py-2 w-8" aria-label="Acciones" />
              </tr>
            </thead>
            <tbody>
              {items.map((m) => {
                const puedeRefund =
                  puedeRegistrar &&
                  (m.tipo === TipoMovimientoTc.CompraConCfdi ||
                    m.tipo === TipoMovimientoTc.CompraSinCfdi) &&
                  m.estado !== EstadoMovimientoTc.EnDisputa &&
                  m.estado !== EstadoMovimientoTc.Reversado;
                const puedeDisputarRow =
                  puedeDisputar &&
                  m.estado === EstadoMovimientoTc.Registrado;
                const tieneAcciones = puedeRefund || puedeDisputarRow;
                return (
                  <tr key={m.id} className="border-t hover:bg-muted/30">
                    <td className="px-3 py-2 whitespace-nowrap text-xs">
                      {m.fechaMovimiento}
                    </td>
                    <td className="px-3 py-2">{m.merchantNormalizado}</td>
                    <td className="px-3 py-2 text-xs text-muted-foreground">
                      {m.conceptoContable}
                    </td>
                    <td className="px-3 py-2 text-right font-mono">
                      {formatearMonto(m.montoMxn, 'MXN')}
                    </td>
                    <td className="px-3 py-2 text-xs">
                      {TipoMovimientoTcLabels[m.tipo]}
                    </td>
                    <td className="px-3 py-2">
                      <EstadoMovimientoTcChip estado={m.estado} />
                    </td>
                    <td className="px-3 py-2 font-mono text-xs text-muted-foreground">
                      {m.facturaProveedorId
                        ? abreviar(m.facturaProveedorId)
                        : '—'}
                    </td>
                    <td className="px-3 py-2">
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button
                            variant="ghost"
                            size="icon"
                            aria-label={`Acciones para movimiento ${m.id}`}
                            disabled={!tieneAcciones}
                          >
                            <MoreVertical className="h-4 w-4" />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          {puedeRefund && (
                            <DropdownMenuItem
                              onSelect={() => setRefundTarget(m)}
                            >
                              <Undo2 className="mr-2 h-3 w-3" />
                              Registrar refund
                            </DropdownMenuItem>
                          )}
                          {puedeDisputarRow && (
                            <DropdownMenuItem
                              onSelect={() => setDisputarTarget(m)}
                              className="text-destructive focus:text-destructive"
                            >
                              <AlertTriangle className="mr-2 h-3 w-3" />
                              Marcar en disputa
                            </DropdownMenuItem>
                          )}
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      <NuevoMovimientoTcSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
      />
      <RegistrarMovimientoEspecialSheet
        open={especialAbierto}
        onOpenChange={setEspecialAbierto}
      />
      <RegistrarRefundTcSheet
        open={refundTarget !== null}
        onOpenChange={(open) => !open && setRefundTarget(null)}
        movimientoOriginal={refundTarget}
      />
      <DisputarMovimientoTcSheet
        open={disputarTarget !== null}
        onOpenChange={(open) => !open && setDisputarTarget(null)}
        movimiento={disputarTarget}
      />
    </div>
  );
}

function Filtros({
  search,
  onChange,
}: {
  search: MovimientosTcSearch;
  onChange: (parcial: Partial<MovimientosTcSearch>) => void;
}) {
  const algun =
    search.tarjetaId ||
    search.usuarioQueUsoId ||
    search.estado != null ||
    search.tipo != null ||
    search.fechaDesde ||
    search.fechaHasta;
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Tarjeta</label>
        <TarjetaSelector
          value={search.tarjetaId ?? null}
          onChange={(id) => onChange({ tarjetaId: id ?? undefined })}
          placeholder="Todas las tarjetas"
          className="w-56"
          soloActivas={false}
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Usuario</label>
        <UsuarioSelector
          value={search.usuarioQueUsoId ?? null}
          onChange={(id) => onChange({ usuarioQueUsoId: id ?? undefined })}
          placeholder="Todos los usuarios"
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
                  : (Number(v) as EstadoMovimientoTc),
            })
          }
        >
          <SelectTrigger
            aria-label="Filtrar por estado movimiento"
            className="w-44"
          >
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(EstadoMovimientoTc.Registrado)}>
              Registrado
            </SelectItem>
            <SelectItem
              value={String(EstadoMovimientoTc.ConciliadoConEstadoCuenta)}
            >
              Conciliado
            </SelectItem>
            <SelectItem value={String(EstadoMovimientoTc.EnDisputa)}>
              En disputa
            </SelectItem>
            <SelectItem value={String(EstadoMovimientoTc.Reversado)}>
              Reversado
            </SelectItem>
            <SelectItem value={String(EstadoMovimientoTc.PagadoAlBanco)}>
              Pagado al banco
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
                  : (Number(v) as TipoMovimientoTc),
            })
          }
        >
          <SelectTrigger
            aria-label="Filtrar por tipo movimiento"
            className="w-44"
          >
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(TipoMovimientoTc.CompraConCfdi)}>
              Compra con CFDI
            </SelectItem>
            <SelectItem value={String(TipoMovimientoTc.CompraSinCfdi)}>
              Compra sin CFDI
            </SelectItem>
            <SelectItem value={String(TipoMovimientoTc.Refund)}>Refund</SelectItem>
            <SelectItem value={String(TipoMovimientoTc.GastoFinanciero)}>
              Gasto financiero
            </SelectItem>
            <SelectItem value={String(TipoMovimientoTc.Anualidad)}>
              Anualidad
            </SelectItem>
            <SelectItem value={String(TipoMovimientoTc.ComisionDivisa)}>
              Comisión divisa
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Desde</label>
        <Input
          type="date"
          value={search.fechaDesde ?? ''}
          onChange={(e) =>
            onChange({ fechaDesde: e.target.value || undefined })
          }
          className="w-40"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Hasta</label>
        <Input
          type="date"
          value={search.fechaHasta ?? ''}
          onChange={(e) =>
            onChange({ fechaHasta: e.target.value || undefined })
          }
          className="w-40"
        />
      </div>
      {algun && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              tarjetaId: undefined,
              usuarioQueUsoId: undefined,
              estado: undefined,
              tipo: undefined,
              fechaDesde: undefined,
              fechaHasta: undefined,
            })
          }
        >
          Limpiar
        </Button>
      )}
    </div>
  );
}

function abreviar(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…` : id;
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
