import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Inbox, Clock } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
  EstadoBadge,
  DateTimeDisplay,
} from '@/components/erp';
import {
  usePendientesAutorizacionOc,
  type NivelAutorizacionFiltro,
} from '@/features/compras/ordenes/api/usePendientesAutorizacionOc';
import { useProveedores, useUsuarios, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { parseDateOnlyLocal } from '@/lib/datetime';
import { useHasPermission, useHasAnyPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { OrdenCompraResumen } from '@/features/compras/ordenes/api/types';
import { cn } from '@/lib/utils';

/**
 * <c>P2 Bandeja de pendientes de autorización OC</c> (UF4-PR1).
 *
 * <para>Filtrado server-side por nivel — el endpoint
 * <c>/pendientes-autorizacion</c> acepta <c>?nivel=Nivel1|Nivel2</c>
 * (mapea a estados <c>EnAutorizacionJefeCompras</c> /
 * <c>EnAutorizacionDireccion</c>). Orden FIFO (más viejas arriba).</para>
 *
 * <para><b>FOC16 — Sin acciones inline</b>: la única acción de la fila
 * es "Ver" → navega a P3 detalle donde están los botones contextuales
 * (transmitir / aprobar N1 / aprobar N2 / rechazar). Mantiene UI
 * escaneable y elimina el riesgo de clicks accidentales en una bandeja
 * de aprobación masiva.</para>
 *
 * <para><b>Tab switcher</b>: si el usuario tiene ambos permisos
 * (<c>autorizar.nivel1</c> + <c>autorizar.nivel2</c>) ve un switcher
 * para alternar entre las dos bandejas; si solo tiene uno, se fija al
 * que aplica.</para>
 */
export function BandejaPendientesOc() {
  const canN1 = useHasPermission(PermisosCanonicos.ComprasOrdenesAutorizarNivel1);
  const canN2 = useHasPermission(PermisosCanonicos.ComprasOrdenesAutorizarNivel2);

  // Default: si solo tiene N1, fija a N1; solo N2, a N2; ambos, a N1.
  const nivelDefault: NivelAutorizacionFiltro = canN1 ? 'Nivel1' : 'Nivel2';
  const [nivelActivo, setNivelActivo] =
    useState<NivelAutorizacionFiltro>(nivelDefault);

  const tieneAmbos = canN1 && canN2;

  const query = usePendientesAutorizacionOc({ nivel: nivelActivo });
  // limit alto + includeInactivas para no perder proveedores
  // históricos en la columna proveedor de la bandeja.
  const proveedoresQuery = useProveedores({
    limit: 1000,
    includeInactivas: true,
  });
  const usuariosQuery = useUsuarios();

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

  function resolverProveedor(id: string): string {
    const p = proveedoresMap.get(id);
    if (p == null) return id.slice(0, 8) + '…';
    return p.nombreComercial ?? p.razonSocial;
  }

  function resolverUsuario(id: string): string {
    return usuariosMap.get(id)?.nombre ?? id.slice(0, 8) + '…';
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

  const items = query.data?.items ?? [];

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
              onClick={() => setNivelActivo('Nivel1')}
              label="Nivel 1 (Jefe Compras)"
            />
            <NivelTab
              activo={nivelActivo === 'Nivel2'}
              onClick={() => setNivelActivo('Nivel2')}
              label="Nivel 2 (Dirección)"
            />
          </nav>
        )}
      </header>

      {items.length === 0 ? (
        <EmptyState
          icon={<Inbox className="h-10 w-10" />}
          title={`Sin OCs pendientes en ${nivelActivo === 'Nivel1' ? 'Nivel 1' : 'Nivel 2'}.`}
          description="Cuando lleguen, las verás aquí ordenadas por antigüedad."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border bg-card">
          <table className="w-full min-w-[840px] text-sm">
            <thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 text-left font-medium">Folio</th>
                <th className="px-3 py-2 text-left font-medium">Proveedor</th>
                <th className="px-3 py-2 text-left font-medium">Comprador</th>
                <th className="px-3 py-2 text-left font-medium">Fecha doc.</th>
                <th className="px-3 py-2 text-left font-medium">Días esperando</th>
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
                  proveedor={resolverProveedor(oc.proveedorId)}
                  comprador={resolverUsuario(oc.compradorTitularId)}
                  ahoraMs={ahoraMs}
                />
              ))}
            </tbody>
          </table>
        </div>
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
}: {
  oc: OrdenCompraResumen;
  bgClass?: string;
  proveedor: string;
  comprador: string;
  ahoraMs: number;
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

  return (
    <tr className={cn(bgClass)} data-oc={oc.id}>
      <td className="px-3 py-2 font-mono text-xs">{oc.folio}</td>
      <td className="px-3 py-2">{proveedor}</td>
      <td className="px-3 py-2 text-muted-foreground">{comprador}</td>
      <td className="px-3 py-2">
        <DateTimeDisplay value={oc.fechaDocumento} />
      </td>
      <td
        className={cn(
          'px-3 py-2 tabular-nums',
          diasEsperando >= 7 && 'text-amber-700 font-medium',
          diasEsperando >= 14 && 'text-rose-700 font-semibold',
        )}
      >
        <span className="inline-flex items-center gap-1">
          <Clock className="h-3 w-3" aria-hidden="true" />
          {diasEsperando} {diasEsperando === 1 ? 'día' : 'días'}
        </span>
      </td>
      <td className="px-3 py-2">
        <EstadoBadge tipo="orden-compra" estado={oc.estado} />
      </td>
      <td className="px-3 py-2 text-right">
        <Button asChild size="sm" variant="outline">
          <Link
            to="/compras/ordenes/$id"
            params={{ id: oc.id }}
            data-action="ver-detalle"
          >
            Ver detalle
          </Link>
        </Button>
      </td>
    </tr>
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
