import { useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { DateTimeDisplay } from '@/components/erp';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useEnviosOc, useRegistrarEnvioOc, type EnvioOc } from '../api/useEnviosOc';
import { descargarPdfOrdenCompra } from '../api/usePdfOrdenCompra';

const resultados: Record<EnvioOc['resultado'], string> = {
  pendiente: 'Resultado pendiente de verificar',
  registrado: 'Envío manual registrado',
  simulado: 'Correo simulado',
  aceptado: 'Aceptado para envío',
  fallido: 'No se pudo enviar',
};

export function EnviosOcPanel({ ordenCompraId, folio }: { ordenCompraId: string; folio?: string }) {
  const permiso = useHasPermission(PermisosCanonicos.ComprasOrdenesLeer);
  const puedeRegistrar = useHasPermission(PermisosCanonicos.ComprasOrdenesCrear);
  const consulta = useEnviosOc(ordenCompraId);
  const enviar = useRegistrarEnvioOc(ordenCompraId);
  const [capturado, setCapturado] = useState<string | null>(null);
  const [descargado, setDescargado] = useState(false);
  const [descargando, setDescargando] = useState(false);
  const [mensaje, setMensaje] = useState<string | null>(null);
  const intento = useRef<{ datos: string; clave: string } | null>(null);
  const ocupado = useRef(false);
  const data = consulta.data;
  if (!permiso) return null;
  const destinatario = capturado ?? data?.correoProveedor ?? '';
  const correoValido = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(destinatario.trim());
  const envioManualDisponible = !!data?.esSimulado || data?.envios[0]?.resultado === 'fallido';
  const bloqueado = !data?.puedeEnviar || !correoValido || enviar.isPending;

  async function descargar() {
    setDescargando(true);
    setMensaje(null);
    try {
      const url = URL.createObjectURL(await descargarPdfOrdenCompra(ordenCompraId));
      const enlace = document.createElement('a');
      enlace.href = url;
      enlace.download = folio ? `${folio}.pdf` : 'Orden-de-compra.pdf';
      enlace.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
      setDescargado(true);
    } catch {
      setMensaje(
        'No se pudo descargar el PDF. Vuelve a descargarlo antes de registrar el envío manual.',
      );
    } finally {
      setDescargando(false);
    }
  }

  async function registrar(modo: 'correo' | 'manual') {
    if (bloqueado || ocupado.current) return;
    ocupado.current = true;
    setMensaje(null);
    const datos = JSON.stringify({ destinatario: destinatario.trim(), modo });
    if (intento.current?.datos !== datos) intento.current = { datos, clave: crypto.randomUUID() };
    try {
      const envio = await enviar.mutateAsync({
        destinatario: destinatario.trim(),
        modo,
        idempotencyKey: intento.current.clave,
      });
      intento.current = null;
      setMensaje(resultados[envio.resultado]);
    } catch (error) {
      setMensaje(
        esApiError(error)
          ? (error.problem.detail ??
              'No se pudo registrar el envío. Revisa el historial antes de reintentar.')
          : 'No se pudo confirmar el registro. Revisa el historial antes de reintentar.',
      );
    } finally {
      ocupado.current = false;
    }
  }

  return (
    <section
      aria-label="Envíos al proveedor"
      className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
    >
      <h2 className="text-md font-semibold">Envíos al proveedor</h2>
      {consulta.isPending && <p className="text-sm text-ink-muted">Cargando envíos…</p>}
      {consulta.error && (
        <p role="alert" className="text-sm text-danger-fg">
          No se pudo consultar el historial. Recarga el detalle antes de enviar.
        </p>
      )}
      {data && (
        <>
          {data.esSimulado && (
            <p className="text-sm text-ink-muted">
              El correo está en modo simulado: no se entrega ningún correo al proveedor. Para
              enviarlo por tu cuenta, descarga el PDF y registra el envío manual.
            </p>
          )}
          {data.motivoBloqueo && <p className="text-sm text-ink-muted">{data.motivoBloqueo}</p>}
          {puedeRegistrar && (
            <>
              <label className="block space-y-1 text-sm">
                <span>Correo del proveedor *</span>
                <Input
                  type="email"
                  required
                  value={destinatario}
                  onChange={(e) => setCapturado(e.target.value)}
                  disabled={enviar.isPending}
                />
              </label>
              <p className="text-xs text-ink-muted">
                El correo capturado se usa sólo para este envío; no cambia el catálogo del
                proveedor.
              </p>
              <div className="flex flex-wrap items-center gap-3">
                <Button
                  disabled={bloqueado}
                  title={
                    data.motivoBloqueo ??
                    (!correoValido ? 'Captura un correo válido del proveedor.' : undefined)
                  }
                  onClick={() => void registrar('correo')}
                >
                  {data.esSimulado ? 'Probar envío (simulado)' : 'Enviar al proveedor'}
                </Button>
                {envioManualDisponible && (
                  <>
                    <Button
                      variant="secondary"
                      disabled={!data.puedeEnviar || descargando}
                      onClick={() => void descargar()}
                    >
                      Descargar PDF para envío manual
                    </Button>
                    <Button
                      variant="secondary"
                      disabled={bloqueado || !descargado}
                      title={
                        !descargado
                          ? 'Descarga el PDF y envíalo al proveedor antes de marcarlo.'
                          : undefined
                      }
                      onClick={() => void registrar('manual')}
                    >
                      Marcar como enviada
                    </Button>
                  </>
                )}
              </div>
            </>
          )}
          {mensaje && (
            <p role="status" className="text-sm">
              {mensaje}
            </p>
          )}
          <h3 className="text-sm font-semibold">Historial de envíos</h3>
          {data.envios.length === 0 && (
            <p className="text-sm text-ink-muted">Todavía no hay envíos registrados.</p>
          )}
          {data.envios.map((envio) => (
            <div key={envio.id} className="space-y-1 border-t border-line-divider pt-3 text-sm">
              <div className="flex flex-wrap items-center gap-2">
                <Badge
                  variant={
                    envio.resultado === 'fallido'
                      ? 'danger'
                      : envio.resultado === 'simulado'
                        ? 'warning'
                        : 'neutral'
                  }
                >
                  {resultados[envio.resultado]}
                </Badge>
                <span>{envio.modo === 'manual' ? 'Manual' : 'Correo'}</span>
                <DateTimeDisplay value={envio.fecha} variant="datetime" />
              </div>
              <p>{envio.destinatario}</p>
              <p className="text-ink-muted">
                Registró: <span>{envio.usuarioNombre}</span>
              </p>
            </div>
          ))}
        </>
      )}
    </section>
  );
}
