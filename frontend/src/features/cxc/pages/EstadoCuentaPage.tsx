import { PageHeader, EmptyState } from '@/components/erp';
import { ReporteCxc } from '../components/ReporteCxc';

import { useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { FileText, Send } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';

import {
  useEnviarEstadoCuenta,
  useEnviosEstadoCuenta,
  useEstadoCuentaCliente,
  usePendientesEstadoCuenta,
} from '@/features/cxc/api/useCartera';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import { EnviarEstadoCuentaDialog } from '@/features/cxc/components/EnviarEstadoCuentaDialog';
import { EnviosEstadoCuentaTabla } from '@/features/cxc/components/EnviosEstadoCuentaTabla';
import { PanelBloqueoEstadoCuenta } from '@/features/cxc/components/PanelBloqueoEstadoCuenta';
import { adaptarReporteCxc } from '@/features/cxc/lib/reporte-cxc-adapter';
import type { EstadoCuentaSearch } from '@/features/cxc/lib/cartera-search-schema';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

const FROM = '/_app/cxc/estado-cuenta/' as const;
const CODIGO_BLOQUEO = 'ESTADO_CUENTA_REPP_PENDIENTE';

/**
 * <c>Estado de cuenta</c> por cliente (CXC-FE-PR5, ADR-0036): facturas,
 * pagos, NCs y saldo corriente en un solo corte imprimible (impresión
 * limpia: filtros y header llevan <c>data-print="hidden"</c>; el shell
 * ya imprime solo la tabla).
 *
 * <para>F1-CXC-09 · C7.8: con pagos sin REP (o REP sin reflejar) el backend
 * responde 422 <c>ESTADO_CUENTA_REPP_PENDIENTE</c> y la pantalla muestra el
 * panel de bloqueo con esos pagos, sin exportar ni enviar. Sin bloqueo,
 * «Emitir y enviar» genera el PDF, lo descarga y registra el envío.</para>
 */
export function EstadoCuentaPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const clienteId = search.clienteId ?? null;

  const query = useEstadoCuentaCliente(clienteId);
  const pendientes = usePendientesEstadoCuenta(clienteId);
  const envios = useEnviosEstadoCuenta(clienteId);
  const enviar = useEnviarEstadoCuenta();
  const puedeEnviar = useHasPermission(PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar);
  const [dialogo, setDialogo] = useState(0);
  const [generando, setGenerando] = useState(false);

  const bloqueado =
    esApiError(query.error) && query.error.problem.code === CODIGO_BLOQUEO;
  const reporte = adaptarReporteCxc(query.data);
  const cliente =
    query.data?.filtrosAplicados.find((f) => f.label === 'Cliente')?.valor ?? 'Cliente';

  function actualizar(parcial: Partial<EstadoCuentaSearch>) {
    navigate({ to: '/cxc/estado-cuenta', search: { ...search, ...parcial } });
  }

  async function emitirYEnviar(datos: { destinatario: string; mensaje?: string }) {
    if (!reporte || clienteId == null) return;
    setGenerando(true);
    try {
      const { generarReportePdfBlob, descargarBlob, nombreArchivoPdf } = await import(
        '@/components/erp/reportes/exporters/reporte-pdf-exporter'
      );
      const nombre = nombreArchivoPdf('estado-cuenta-cxc');
      const pdf = await generarReportePdfBlob(reporte);
      descargarBlob(pdf, nombre);
      const envio = await enviar.mutateAsync({
        clienteId,
        destinatario: datos.destinatario,
        mensaje: datos.mensaje,
        pdf,
        nombreArchivo: nombre,
      });
      setDialogo(0);
      if (envio.estado === 'Enviado') {
        toast.success('Estado de cuenta enviado', { description: datos.destinatario });
      } else if (envio.estado === 'Fallido') {
        toast.error('El envío quedó registrado, pero el correo falló', {
          description: envio.detalleError ?? 'Reintenta o comparte el PDF descargado.',
        });
      } else {
        toast.info('Envío registrado', {
          description:
            'El correo saliente aún no está habilitado en este ambiente; comparte el PDF descargado.',
        });
      }
    } catch (err) {
      if (esApiError(err) && err.problem.code === CODIGO_BLOQUEO) {
        setDialogo(0);
        void query.refetch();
      }
      toast.error('No se pudo enviar el estado de cuenta', {
        description: esApiError(err)
          ? (err.problem.detail ?? err.problem.title)
          : err instanceof Error
            ? err.message
            : String(err),
      });
    } finally {
      setGenerando(false);
    }
  }

  const filtrosUi = (
    <div className="w-full max-w-md space-y-1">
      <Label className="text-xs text-ink-muted">Cliente *</Label>
      <ClienteSelectorCxc
        value={clienteId}
        onChange={(item) => actualizar({ clienteId: item?.id ?? undefined })}
        placeholder="Elige el cliente del estado de cuenta…"
      />
    </div>
  );

  const puedeEmitir =
    clienteId != null && reporte != null && !bloqueado && pendientes.data?.emitible !== false;

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <header data-print="hidden">
        <PageHeader
          titulo={<>Estado de cuenta</>}
          descripcion={
            <>Movimientos del cliente: facturas, pagos, notas de crédito y saldo corriente.</>
          }
          acciones={
            puedeEnviar && puedeEmitir ? (
              <Button size="sm" onClick={() => setDialogo((n) => n + 1)}>
                <Send className="h-4 w-4" aria-hidden="true" />
                Emitir y enviar
              </Button>
            ) : undefined
          }
        />
      </header>

      {clienteId == null ? (
        <>
          <div data-print="hidden" className="rounded-md border bg-surface-muted/30 p-3">
            {filtrosUi}
          </div>
          <EmptyState
            icon={<FileText className="h-10 w-10" />}
            title="Elige un cliente para generar su estado de cuenta."
            description="El estado de cuenta siempre se genera por cliente."
          />
        </>
      ) : (
        <>
          {bloqueado ? (
            <PanelBloqueoEstadoCuenta
              pendientes={pendientes.data?.pendientes}
              isLoading={pendientes.isLoading}
              filtros={filtrosUi}
            />
          ) : (
            <ReporteCxc
              reporte={reporte}
              isLoading={query.isLoading}
              error={esApiError(query.error) ? query.error : undefined}
              onRetry={() => query.refetch()}
              filtrosUi={filtrosUi}
              nombreArchivo="estado-cuenta-cxc"
            />
          )}
          <EnviosEstadoCuentaTabla envios={envios.data} isLoading={envios.isLoading} />
        </>
      )}

      {dialogo > 0 && (
        <EnviarEstadoCuentaDialog
          key={dialogo}
          open
          onOpenChange={(abierto) => !abierto && setDialogo(0)}
          cliente={cliente}
          emailSugerido={pendientes.data?.emailCliente ?? null}
          isPending={generando || enviar.isPending}
          onConfirm={(datos) => void emitirYEnviar(datos)}
        />
      )}
    </div>
  );
}
