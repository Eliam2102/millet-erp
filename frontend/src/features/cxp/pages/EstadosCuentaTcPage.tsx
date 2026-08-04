import { useRef, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import {
  Calculator,
  CheckCircle2,
  FileUp,
  Lock,
  Plus,
  Wallet,
} from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { TarjetaSelector } from '@/features/cxp/components/TarjetaSelector';
import {
  useCerrarEstadoCuentaTc,
  useConciliarAutomaticoEstadoCuentaTc,
  useEstadosCuentaTc,
  useMarcarEstadoCuentaConciliado,
  useMarcarEstadoCuentaPagadoBanco,
  useSubirArchivoEstadoCuentaTc,
} from '@/features/cxp/api/useEstadosCuentaTc';
import {
  EstadoCuentaTcStatus,
  type EstadoCuentaTc,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EstadoCuentaTcChip } from '@/features/cxp/components/EstadoChips';
import { NuevoEstadoCuentaTcSheet } from '@/features/cxp/components/NuevoEstadoCuentaTcSheet';
import type { EstadosCuentaTcSearch } from '@/features/cxp/lib/tc-search-schema';

const FROM = '/_app/cxp/tc/estados-cuenta' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P1 — Bandeja de Estados de Cuenta TC</c> (doc 07 §FE-F6-PR2).
 * Ciclo: EnConciliacion → (subir archivo + conciliar) → Conciliado →
 * Cerrado → PagadoBanco.
 *
 * <para>PLATFORM-TODO(&lt;ConciliacionTcDual&gt;): el spec menciona
 * una pantalla full-screen con vista dual líneas del banco vs
 * movimientos, drag-and-drop opcional, confirmación inline de
 * sugerencias 60-89, y captura retroactiva inline. En este MVP,
 * priorizamos las transiciones del ciclo (subir archivo + conciliar
 * automático + cerrar) que son lo crítico para CxP; la conciliación
 * manual fina entra en FE-F8 hardening cuando llegue volumen real.</para>
 */
export function EstadosCuentaTcPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeRegistrar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento,
  );
  const [sheetAbierto, setSheetAbierto] = useState(false);

  const query = useEstadosCuentaTc({
    tarjetaId: search.tarjetaId,
    estado: search.estado,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<EstadosCuentaTcSearch>) {
    navigate({
      to: '/cxp/tc/estados-cuenta',
      search: { ...search, ...parcial },
    });
  }

  const items = query.data?.items ?? [];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Estados de cuenta TC
          </h1>
          <p className="text-sm text-muted-foreground">
            Conciliación periódica con el archivo del banco. Al cerrar se
            genera una factura agregada contra el proveedor banco.
          </p>
        </div>
        {puedeRegistrar && (
          <Button onClick={() => setSheetAbierto(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Nuevo estado de cuenta
          </Button>
        )}
      </div>

      <Filtros search={search} onChange={actualizarSearch} />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los estados de cuenta"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={4}
          columns={[
            { width: 'w-32' },
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin estados de cuenta"
          description="Crea el primer estado de cuenta para empezar la conciliación."
        />
      ) : (
        <Tabla items={items} />
      )}

      <NuevoEstadoCuentaTcSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
      />
    </div>
  );
}

function Filtros({
  search,
  onChange,
}: {
  search: EstadosCuentaTcSearch;
  onChange: (parcial: Partial<EstadosCuentaTcSearch>) => void;
}) {
  const algun = search.estado != null || search.tarjetaId;
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estado</label>
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              estado:
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as EstadoCuentaTcStatus),
            })
          }
        >
          <SelectTrigger
            aria-label="Filtrar por estado"
            className="w-52"
          >
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(EstadoCuentaTcStatus.EnConciliacion)}>
              En conciliación
            </SelectItem>
            <SelectItem value={String(EstadoCuentaTcStatus.Conciliado)}>
              Conciliado
            </SelectItem>
            <SelectItem value={String(EstadoCuentaTcStatus.Cerrado)}>
              Cerrado
            </SelectItem>
            <SelectItem value={String(EstadoCuentaTcStatus.PagadoBanco)}>
              Pagado al banco
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">
          Tarjeta
        </label>
        <TarjetaSelector
          value={search.tarjetaId ?? null}
          onChange={(id) => onChange({ tarjetaId: id ?? undefined })}
          placeholder="Todas las tarjetas"
          className="w-56"
          soloActivas={false}
        />
      </div>
      {algun && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({ tarjetaId: undefined, estado: undefined })
          }
        >
          Limpiar
        </Button>
      )}
    </div>
  );
}

function Tabla({ items }: { items: readonly EstadoCuentaTc[] }) {
  const subir = useSubirArchivoEstadoCuentaTc();
  const conciliar = useConciliarAutomaticoEstadoCuentaTc();
  const marcarConciliado = useMarcarEstadoCuentaConciliado();
  const cerrar = useCerrarEstadoCuentaTc();
  const marcarPagado = useMarcarEstadoCuentaPagadoBanco();

  const puedeCerrar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarTcCerrarEstadoCuenta,
  );
  const puedeRegistrar = useHasPermission(
    PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento,
  );

  function dispatch(label: string, p: Promise<unknown>) {
    p.then(
      () => toast.success(label),
      (error) => {
        if (esApiError(error)) {
          toast.error(error.problem.title);
          return;
        }
        toast.error('Error al ejecutar acción.');
      },
    );
  }

  function handleConciliar(ec: EstadoCuentaTc) {
    conciliar
      .mutateAsync({
        id: ec.id,
        versionEsperada: ec.version,
        idempotencyKey: crypto.randomUUID(),
      })
      .then(
        (res) => {
          toast.success(
            `Conciliado: ${res.matchesAuto} auto, ${res.sugerencias} sugerencias, ${res.sinMatch} sin match. Diferencia ${res.diferenciaMxn.toFixed(2)} MXN.`,
          );
        },
        (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title);
            return;
          }
          toast.error('Error al conciliar.');
        },
      );
  }

  function handleMarcarConciliado(ec: EstadoCuentaTc) {
    dispatch(
      'Marcado como conciliado',
      marcarConciliado.mutateAsync({
        id: ec.id,
        versionEsperada: ec.version,
        idempotencyKey: crypto.randomUUID(),
      }),
    );
  }

  function handleCerrar(ec: EstadoCuentaTc) {
    if (
      !window.confirm(
        'Cerrar el estado de cuenta generará una factura agregada contra el banco. ¿Continuar?',
      )
    )
      return;
    cerrar
      .mutateAsync({
        id: ec.id,
        versionEsperada: ec.version,
        idempotencyKey: crypto.randomUUID(),
      })
      .then(
        (res) => {
          toast.success(
            `Cerrado. Factura proveedor: ${res.facturaProveedorId.slice(0, 8)}… (${res.totalBancoMxn.toFixed(2)} MXN)`,
          );
        },
        (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title);
            return;
          }
          toast.error('Error al cerrar.');
        },
      );
  }

  function handleMarcarPagado(ec: EstadoCuentaTc) {
    dispatch(
      'Pagado al banco',
      marcarPagado.mutateAsync({
        id: ec.id,
        versionEsperada: ec.version,
        idempotencyKey: crypto.randomUUID(),
      }),
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">Periodo</th>
            <th className="px-3 py-2 text-left">Tarjeta</th>
            <th className="px-3 py-2 text-right">Líneas</th>
            <th className="px-3 py-2 text-right">Total banco</th>
            <th className="px-3 py-2 text-right">Conciliado</th>
            <th className="px-3 py-2 text-right">Diferencia</th>
            <th className="px-3 py-2 text-left">Estado</th>
            <th className="px-3 py-2 text-right" aria-label="Acciones" />
          </tr>
        </thead>
        <tbody>
          {items.map((ec) => (
            <tr key={ec.id} className="border-t hover:bg-muted/30">
              <td className="px-3 py-2 whitespace-nowrap text-xs">
                {ec.periodoDesde} → {ec.periodoHasta}
              </td>
              <td className="px-3 py-2 font-mono text-xs text-muted-foreground">
                {abreviar(ec.tarjetaId)}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {ec.lineasCount}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {fmt(ec.totalBancoMxn)}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {fmt(ec.totalConciliadoMxn)}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {fmt(ec.diferenciaMxn)}
              </td>
              <td className="px-3 py-2">
                <EstadoCuentaTcChip estado={ec.estado} />
              </td>
              <td className="px-3 py-2 text-right">
                <Acciones
                  ec={ec}
                  puedeRegistrar={puedeRegistrar}
                  puedeCerrar={puedeCerrar}
                  isPending={
                    subir.isPending ||
                    conciliar.isPending ||
                    marcarConciliado.isPending ||
                    cerrar.isPending ||
                    marcarPagado.isPending
                  }
                  onSubir={(file) =>
                    dispatch(
                      'Archivo procesado',
                      subir.mutateAsync({
                        id: ec.id,
                        versionEsperada: ec.version,
                        archivo: file,
                        idempotencyKey: crypto.randomUUID(),
                      }),
                    )
                  }
                  onConciliar={() => handleConciliar(ec)}
                  onMarcarConciliado={() => handleMarcarConciliado(ec)}
                  onCerrar={() => handleCerrar(ec)}
                  onMarcarPagado={() => handleMarcarPagado(ec)}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

interface AccionesProps {
  ec: EstadoCuentaTc;
  puedeRegistrar: boolean;
  puedeCerrar: boolean;
  isPending: boolean;
  onSubir: (file: File) => void;
  onConciliar: () => void;
  onMarcarConciliado: () => void;
  onCerrar: () => void;
  onMarcarPagado: () => void;
}

function Acciones({
  ec,
  puedeRegistrar,
  puedeCerrar,
  isPending,
  onSubir,
  onConciliar,
  onMarcarConciliado,
  onCerrar,
  onMarcarPagado,
}: AccionesProps) {
  const fileRef = useRef<HTMLInputElement>(null);
  const enConciliacion = ec.estado === EstadoCuentaTcStatus.EnConciliacion;
  const conciliado = ec.estado === EstadoCuentaTcStatus.Conciliado;
  const cerrado = ec.estado === EstadoCuentaTcStatus.Cerrado;
  return (
    <div className="flex flex-wrap items-center justify-end gap-1">
      {enConciliacion && puedeRegistrar && (
        <>
          <input
            ref={fileRef}
            type="file"
            accept=".xlsx,.xls"
            className="hidden"
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) onSubir(f);
              e.target.value = '';
            }}
          />
          <Button
            variant="outline"
            size="sm"
            onClick={() => fileRef.current?.click()}
            disabled={isPending}
          >
            <FileUp className="mr-1 h-3 w-3" />
            Subir archivo
          </Button>
          {ec.perfilParserUsado && (
            <Button
              variant="outline"
              size="sm"
              onClick={onConciliar}
              disabled={isPending}
            >
              <Calculator className="mr-1 h-3 w-3" />
              Conciliar auto
            </Button>
          )}
          {ec.diferenciaMxn === 0 && (
            <Button
              variant="outline"
              size="sm"
              onClick={onMarcarConciliado}
              disabled={isPending}
            >
              <CheckCircle2 className="mr-1 h-3 w-3" />
              Marcar conciliado
            </Button>
          )}
        </>
      )}
      {conciliado && puedeCerrar && (
        <Button
          variant="outline"
          size="sm"
          onClick={onCerrar}
          disabled={isPending}
        >
          <Lock className="mr-1 h-3 w-3" />
          Cerrar
        </Button>
      )}
      {cerrado && puedeCerrar && (
        <Button
          variant="outline"
          size="sm"
          onClick={onMarcarPagado}
          disabled={isPending}
        >
          <Wallet className="mr-1 h-3 w-3" />
          Marcar pagado
        </Button>
      )}
    </div>
  );
}

function abreviar(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…` : id;
}

function fmt(v: number | null): string {
  if (v == null) return '—';
  try {
    return new Intl.NumberFormat('es-MX', {
      style: 'currency',
      currency: 'MXN',
      minimumFractionDigits: 2,
    }).format(v);
  } catch {
    return v.toFixed(2);
  }
}
