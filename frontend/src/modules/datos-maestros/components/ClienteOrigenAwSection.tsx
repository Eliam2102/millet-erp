import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { useQueryClient } from '@tanstack/react-query';
import { RefreshCw } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { mensajeErrorSincronizacion, useReintentarCliente } from '@/modules/datos-maestros/api';
import type { ClienteDetalle } from '@/modules/datos-maestros/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  lecturaAwApagada,
  MOTIVO_LECTURA_AW_APAGADA,
  useAwOrigen,
} from '@/modules/datos-maestros/api/aw-origen';
import {
  useCambiosAwCliente,
  SINCRONIZACION_AW_KEY,
} from '@/modules/datos-maestros/api/sincronizacion-aw';
import { CambiosAwHistorial } from '@/modules/datos-maestros/components/CambiosAwHistorial';
import { ResultadoSincronizacionBadge } from '@/modules/datos-maestros/components/master-badges';

/**
 * Sección "Datos de origen A+W" del detalle de cliente (F1-ADM-06).
 * Solo para clientes con <c>origenAw</c>. Muestra la lectura de origen,
 * una tabla ERP vs Origen con SOLO los campos que difieren y, con
 * <c>sincronizar</c>, el botón "Reintentar lectura". La comparación de
 * CP fiscal solo aparece con <c>origen-ver</c> (el backend omite el
 * domicilio de origen sin ese permiso).
 */
export function ClienteOrigenAwSection({ cliente }: { cliente: ClienteDetalle }) {
  const canSincronizar = useHasPermission(PermisosCanonicos.DatosMaestrosClientesSincronizar);
  const canVerOrigen = useHasPermission(PermisosCanonicos.DatosMaestrosClientesOrigenVer);
  const queryClient = useQueryClient();
  const cambios = useCambiosAwCliente(cliente.id, canVerOrigen);
  const reintentar = useReintentarCliente();
  const apagada = lecturaAwApagada(useAwOrigen(canSincronizar).data, { productos: false });
  const o = cliente.origenAw;
  if (o == null) return null;

  // ultimaAplicacionUtc default(DateTime) = nunca aplicado.
  const aplicado =
    o.ultimaAplicacionUtc != null && new Date(o.ultimaAplicacionUtc).getUTCFullYear() > 2000;

  const monedaOrigen = o.monedaNormalizada ?? o.monedaOrigen;
  const filas = [
    ['Razón social / nombre', cliente.razonSocial, o.nombreComercialOrigen],
    ['Moneda', cliente.monedaDefault, monedaOrigen],
    ['Código postal fiscal', cliente.codigoPostalFiscal, o.domicilioOrigenCp],
  ].filter(([, erp, origen]) => origen != null && origen !== '' && norm(erp) !== norm(origen));

  function handleReintentar() {
    if (cliente.referenciaExterna == null) return;
    reintentar.mutate(
      { referencia: cliente.referenciaExterna },
      {
        onSuccess: (r) => {
          void queryClient.invalidateQueries({ queryKey: SINCRONIZACION_AW_KEY });
          if (r.estado === 'Completa') toast.success('Lectura completada.');
          else toast.warning(r.errores[0]?.mensaje ?? `La lectura terminó con estado ${r.estado}.`);
        },
        onError: (e) => toast.error(mensajeErrorSincronizacion(e)),
      },
    );
  }

  return (
    <section
      className="mt-4 max-w-3xl rounded-md border bg-surface-card p-4"
      aria-label="Datos de origen A+W"
    >
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <h3 className="text-sm font-semibold">Datos de origen A+W</h3>
          <ResultadoSincronizacionBadge resultado={o.resultado} aplicado={aplicado} />
        </div>
        {canSincronizar && cliente.referenciaExterna != null && (
          <div className="flex items-center gap-2">
            {apagada && (
              <p role="status" className="text-xs text-ink-muted">
                {MOTIVO_LECTURA_AW_APAGADA}
              </p>
            )}
            <Button
              type="button"
              variant="ghost"
              size="sm"
              disabled={reintentar.isPending || apagada}
              title={apagada ? MOTIVO_LECTURA_AW_APAGADA : undefined}
              onClick={handleReintentar}
            >
              <RefreshCw className="mr-1.5 h-4 w-4" />
              {reintentar.isPending ? 'Leyendo…' : 'Reintentar lectura'}
            </Button>
          </div>
        )}
      </div>

      {o.error != null && o.error !== '' && (
        <p role="alert" className="mb-3 text-sm text-danger-fg">
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
        <Dato
          t="Última lectura"
          v={fecha(o.ultimaLecturaUtc)}
          title={`Contrato ${o.versionContrato} · mapeo ${o.versionMapeo}`}
        />
        <Dato t="Última aplicación" v={aplicado ? fecha(o.ultimaAplicacionUtc) : 'Nunca'} />
      </dl>

      {filas.length > 0 && (
        <Table className="mt-4 w-full text-sm">
          <caption className="mb-1 text-left text-xs text-ink-muted">
            Diferencias entre el ERP y el origen
          </caption>
          <TableHeader>
            <TableRow className="text-left text-xs text-ink-muted">
              <TableHead className="py-1 font-normal">Campo</TableHead>
              <TableHead className="py-1 font-normal">ERP</TableHead>
              <TableHead className="py-1 font-normal">Origen</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {filas.map(([campo, erp, origen]) => (
              <TableRow key={campo} className="border-t">
                <TableCell className="py-1">{campo}</TableCell>
                <TableCell className="py-1">{erp || '—'}</TableCell>
                <TableCell className="py-1">{origen}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {(o.diferencias?.length ?? 0) > 0 && (
        <Table className="mt-4 w-full text-sm">
          <caption className="mb-1 text-left text-xs text-ink-muted">
            Recibido de A+W y no aplicado al cliente
          </caption>
          <TableHeader>
            <TableRow className="text-left text-xs text-ink-muted">
              <TableHead className="py-1 font-normal">Campo</TableHead>
              <TableHead className="py-1 font-normal">Recibido</TableHead>
              <TableHead className="py-1 font-normal">Se conserva</TableHead>
              <TableHead className="py-1 font-normal">Motivo</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {o.diferencias!.map((d) => (
              <TableRow key={d.campo} className="border-t">
                <TableCell className="py-1">{d.campo}</TableCell>
                <TableCell className="py-1">{d.recibido || '—'}</TableCell>
                <TableCell className="py-1">{d.conservado || '—'}</TableCell>
                <TableCell className="py-1 text-ink-muted">{d.motivo}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {canVerOrigen && (
        <CambiosAwHistorial
          cambios={cambios.data}
          isLoading={cambios.isLoading}
          isError={cambios.isError}
        />
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

function Dato({ t, v, title }: { t: string; v: string | null; title?: string }) {
  return (
    <div title={title}>
      <dt className="text-xs text-ink-muted">{t}</dt>
      <dd>{v ?? '—'}</dd>
    </div>
  );
}
