import { RefreshCw } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  mensajeErrorSincronizacion,
  useReintentarCliente,
} from '@/modules/datos-maestros/api';
import type { ClienteDetalle } from '@/modules/datos-maestros/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ResultadoSincronizacionBadge } from '@/modules/datos-maestros/components/master-badges';

/**
 * Sección "Datos de origen A+W" del detalle de cliente (F1-ADM-06).
 * Solo para clientes con <c>origenAw</c>. Muestra la lectura de origen,
 * una tabla ERP vs Origen con SOLO los campos que difieren y, con
 * <c>sincronizar</c>, el botón "Reintentar lectura". La comparación de
 * CP fiscal solo aparece con <c>origen-ver</c> (el backend omite el
 * domicilio de origen sin ese permiso).
 */
export function ClienteOrigenAwSection({
  cliente,
}: {
  cliente: ClienteDetalle;
}) {
  const canSincronizar = useHasPermission(
    PermisosCanonicos.DatosMaestrosClientesSincronizar,
  );
  const reintentar = useReintentarCliente();
  const o = cliente.origenAw;
  if (o == null) return null;

  // ultimaAplicacionUtc default(DateTime) = nunca aplicado.
  const aplicado =
    o.ultimaAplicacionUtc != null &&
    new Date(o.ultimaAplicacionUtc).getUTCFullYear() > 2000;

  const monedaOrigen = o.monedaNormalizada ?? o.monedaOrigen;
  const filas = [
    ['Razón social / nombre', cliente.razonSocial, o.nombreComercialOrigen],
    ['Moneda', cliente.monedaDefault, monedaOrigen],
    ['Código postal fiscal', cliente.codigoPostalFiscal, o.domicilioOrigenCp],
  ].filter(
    ([, erp, origen]) =>
      origen != null && origen !== '' && norm(erp) !== norm(origen),
  );

  function handleReintentar() {
    if (cliente.referenciaExterna == null) return;
    reintentar.mutate(
      { referencia: cliente.referenciaExterna },
      {
        onSuccess: (r) => {
          if (r.estado === 'Completa') toast.success('Lectura completada.');
          else
            toast.warning(r.errores[0]?.mensaje ?? `La lectura terminó con estado ${r.estado}.`);
        },
        onError: (e) => toast.error(mensajeErrorSincronizacion(e)),
      },
    );
  }

  return (
    <section
      className="mt-4 max-w-3xl rounded-md border bg-card p-4"
      aria-label="Datos de origen A+W"
    >
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <h3 className="text-sm font-semibold">Datos de origen A+W</h3>
          <ResultadoSincronizacionBadge
            resultado={o.resultado}
            aplicado={aplicado}
          />
        </div>
        {canSincronizar && cliente.referenciaExterna != null && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={reintentar.isPending}
            onClick={handleReintentar}
          >
            <RefreshCw className="mr-1.5 h-4 w-4" />
            {reintentar.isPending ? 'Leyendo…' : 'Reintentar lectura'}
          </Button>
        )}
      </div>

      {o.error != null && o.error !== '' && (
        <p role="alert" className="mb-3 text-sm text-destructive">
          {o.error}
        </p>
      )}

      <dl className="grid grid-cols-1 gap-3 text-sm md:grid-cols-3">
        <Dato t="Condición de pago" v={o.condicionOrigen} />
        <Dato
          t="Días nominales"
          v={o.diasNominalesOrigen != null ? String(o.diasNominalesOrigen) : null}
        />
        <Dato t="Moneda de origen" v={o.monedaOrigen} />
        <Dato t="Última lectura" v={fecha(o.ultimaLecturaUtc)} />
        <Dato
          t="Última aplicación"
          v={aplicado ? fecha(o.ultimaAplicacionUtc) : 'Nunca'}
        />
        <Dato t="Contrato / mapeo" v={`${o.versionContrato} / ${o.versionMapeo}`} />
      </dl>

      {filas.length > 0 && (
        <table className="mt-4 w-full text-sm">
          <caption className="mb-1 text-left text-xs text-muted-foreground">
            Diferencias entre el ERP y el origen
          </caption>
          <thead>
            <tr className="text-left text-xs text-muted-foreground">
              <th className="py-1 font-normal">Campo</th>
              <th className="py-1 font-normal">ERP</th>
              <th className="py-1 font-normal">Origen</th>
            </tr>
          </thead>
          <tbody>
            {filas.map(([campo, erp, origen]) => (
              <tr key={campo} className="border-t">
                <td className="py-1">{campo}</td>
                <td className="py-1">{erp || '—'}</td>
                <td className="py-1">{origen}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {(o.diferencias?.length ?? 0) > 0 && (
        <table className="mt-4 w-full text-sm">
          <caption className="mb-1 text-left text-xs text-muted-foreground">
            Recibido de A+W y no aplicado al cliente
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
            {o.diferencias!.map((d) => (
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

function norm(v: string | null | undefined) {
  return (v ?? '').trim().toUpperCase();
}

function fecha(v: string | null) {
  return v == null ? null : new Date(v).toLocaleString('es-MX');
}

function Dato({ t, v }: { t: string; v: string | null }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{t}</dt>
      <dd>{v ?? '—'}</dd>
    </div>
  );
}
