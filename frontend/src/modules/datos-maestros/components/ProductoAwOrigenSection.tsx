import { RefreshCw } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  mensajeErrorSincronizacionProductos,
  useProductoAwSincronizacion,
  useReintentarProductoAw,
} from '@/modules/datos-maestros/api';
import type {
  DiferenciaAplicacionAw,
  ProductoAwDetalle,
  ProductoAwVariante,
} from '@/modules/datos-maestros/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ResultadoSincronizacionBadge } from '@/modules/datos-maestros/components/master-badges';

const SIN_DATO = 'sin dato';

/** Nulo = A+W no lo informó: se muestra "sin dato", NUNCA 0. */
function mm(v: number | null | undefined) {
  return v == null ? SIN_DATO : `${v} mm`;
}

function fecha(v: string | null | undefined) {
  return v == null ? null : new Date(v).toLocaleString('es-MX');
}

/** El backend guarda las diferencias como JSON (sin garantía de casing). */
function parsearDiferencias(json: string | null | undefined): DiferenciaAplicacionAw[] {
  if (!json) return [];
  try {
    const arr = JSON.parse(json) as Record<string, string | null>[];
    return arr.map((d) => ({
      campo: d.campo ?? d.Campo ?? '',
      recibido: d.recibido ?? d.Recibido ?? null,
      conservado: d.conservado ?? d.Conservado ?? null,
      motivo: d.motivo ?? d.Motivo ?? '',
    }));
  } catch {
    return [];
  }
}

/** Aviso de baja: el producto se conserva, no se usa en documentos nuevos. */
export function ProductoAwBajaAviso({ producto }: { producto: ProductoAwDetalle }) {
  if (producto.fechaBaja == null) return null;
  return (
    <p
      role="status"
      className="mb-4 max-w-3xl rounded-md border border-amber-300 bg-amber-500/10 p-3 text-sm text-amber-800 dark:text-amber-300"
    >
      Inactivo: no se usa en documentos nuevos; el historial se conserva. Baja
      desde {new Date(producto.fechaBaja).toLocaleDateString('es-MX')}.
    </p>
  );
}

export function ProductoAwVariantesTable({ variantes }: { variantes: ProductoAwVariante[] }) {
  if (variantes.length === 0) return null;
  return (
    <section className="mt-4 max-w-3xl rounded-md border bg-card p-4" aria-label="Variantes">
      <h3 className="mb-2 text-sm font-semibold">Variantes ({variantes.length})</h3>
      <table className="w-full text-sm">
        <thead>
          <tr className="text-left text-xs text-muted-foreground">
            <th className="py-1 font-normal">Clave</th>
            <th className="py-1 font-normal">Alto</th>
            <th className="py-1 font-normal">Ancho</th>
            <th className="py-1 font-normal">Espesor</th>
            <th className="py-1 font-normal">Composición</th>
          </tr>
        </thead>
        <tbody>
          {variantes.map((v) => (
            <tr key={v.claveVariante} className="border-t">
              <td className="py-1 font-mono">{v.claveVariante}</td>
              <td className="py-1">{mm(v.altoMm)}</td>
              <td className="py-1">{mm(v.anchoMm)}</td>
              <td className="py-1">{mm(v.espesorMm)}</td>
              <td className="py-1">{v.composicion || SIN_DATO}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

/**
 * "Datos de origen A+W" del detalle de producto (F1-ADM-07): resultado,
 * última lectura, recibido vs conservado y reintento por fila con estado
 * propio. El reintento manda If-Match con la versión leída: si el
 * producto cambió, 409 y no se sobrescribe.
 */
export function ProductoAwOrigenSection({ producto }: { producto: ProductoAwDetalle }) {
  const canGestionar = useHasPermission(PermisosCanonicos.DatosMaestrosProductosAwGestionar);
  const estado = useProductoAwSincronizacion(producto.id);
  const reintentar = useReintentarProductoAw();
  const s = estado.data?.sincronizacion;
  if (!canGestionar || s == null) return null;

  const aplicado = s.aplicadoEnUtc != null && new Date(s.aplicadoEnUtc).getUTCFullYear() > 2000;
  const diferencias = parsearDiferencias(s.diferencias);

  function handleReintentar() {
    reintentar.mutate(
      { referencia: producto.referenciaExterna, version: estado.data?.version },
      {
        onSuccess: (r) =>
          r.errores + r.conflictos + r.pendientes === 0
            ? toast.success('Lectura completada.')
            : toast.warning(r.erroresPorReferencia[0]?.mensaje ?? 'La lectura no se pudo aplicar.'),
        onError: (e) => toast.error(mensajeErrorSincronizacionProductos(e)),
      },
    );
  }

  return (
    <section className="mt-4 max-w-3xl rounded-md border bg-card p-4" aria-label="Datos de origen A+W">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <h3 className="text-sm font-semibold">Datos de origen A+W</h3>
          {s.resultado != null && (
            <ResultadoSincronizacionBadge resultado={s.resultado} aplicado={aplicado} />
          )}
        </div>
        <Button type="button" variant="ghost" size="sm" disabled={reintentar.isPending} onClick={handleReintentar}>
          <RefreshCw className="mr-1.5 h-4 w-4" />
          {reintentar.isPending ? 'Leyendo…' : 'Reintentar lectura'}
        </Button>
      </div>

      {s.error != null && s.error !== '' && (
        <p role="alert" className="mb-3 text-sm text-destructive">
          {s.error}
        </p>
      )}

      <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-3">
        <Dato t="Última lectura" v={fecha(s.leidoEnUtc)} />
        <Dato t="Última aplicación" v={aplicado ? fecha(s.aplicadoEnUtc) : 'Nunca'} />
        <Dato t="Contrato / mapeo" v={`${s.versionContrato ?? '—'} / ${s.versionMapeo ?? '—'}`} />
        <Dato t="Unidad de origen" v={s.unidadOrigenCruda} />
      </dl>

      {diferencias.length > 0 && (
        <table className="mt-4 w-full text-sm">
          <caption className="mb-1 text-left text-xs text-muted-foreground">
            Recibido de A+W y no aplicado al producto
          </caption>
          <thead>
            <tr className="text-left text-xs text-muted-foreground">
              <th className="py-1 font-normal">Campo</th>
              <th className="py-1 font-normal">Recibido</th>
              <th className="py-1 font-normal">Se conserva</th>
              <th className="py-1 font-normal">Motivo</th>
            </tr>
          </thead>
          <tbody>
            {diferencias.map((d) => (
              <tr key={d.campo} className="border-t">
                <td className="py-1">{d.campo}</td>
                <td className="py-1">{d.recibido || '—'}</td>
                <td className="py-1">{d.conservado || '—'}</td>
                <td className="py-1 text-muted-foreground">{d.motivo}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

function Dato({ t, v }: { t: string; v: string | null }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{t}</dt>
      <dd>{v ?? '—'}</dd>
    </div>
  );
}
