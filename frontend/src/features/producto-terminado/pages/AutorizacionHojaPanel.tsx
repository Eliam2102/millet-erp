import { useState, type FormEvent } from 'react';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { errorUsuario } from '@/lib/error-usuario';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useAutorizarHojaSalida } from '../api/hooks';
import type { AutorizacionSalidaDto, HojaSalidaDto, NivelAutorizacionSalida } from '../api/types';

const selectClass = 'h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3 text-sm';

const nombreNivel: Record<NivelAutorizacionSalida, string> = {
  administracion: 'Dirección de Administración',
  planeacion: 'Dirección de Planeación (suplencia)',
  contraloria: 'Contraloría (suplencia)',
  credito: 'Crédito y Cobranza',
  ventas: 'Gerencia de Ventas (suplencia)',
};

/** «10425068:8» → «10425068 (8 piezas)». */
const pedidos = (alcance: string[]) =>
  alcance.map((x) => { const i = x.lastIndexOf(':'); return `${x.slice(0, i)} (${x.slice(i + 1)} piezas)`; }).join(', ');

/**
 * Reglas de la hoja de salida confirmadas por Millet (9-nov): sin factura timbrada del día solo sale con la excepción de Dirección de
 * Administración (suplentes Planeación y Contraloría); un pedido incompleto solo sale con Crédito y Cobranza (suplente Gerencia de Ventas).
 * Quien armó o cargó la hoja no puede autorizarla (el servidor lo rechaza).
 */
export function AutorizacionHojaPanel({ hoja }: { hoja: HojaSalidaDto }) {
  const autorizar = useAutorizarHojaSalida();
  const permitidos: Record<NivelAutorizacionSalida, boolean> = {
    administracion: useHasPermission(P.ProductoTerminadoSalidaAprobarAdministracion),
    planeacion: useHasPermission(P.ProductoTerminadoSalidaAprobarPlaneacion),
    contraloria: useHasPermission(P.ProductoTerminadoSalidaAprobarContraloria),
    credito: useHasPermission(P.ProductoTerminadoSalidaAprobarCredito),
    ventas: useHasPermission(P.ProductoTerminadoSalidaAprobarVentas),
  };
  const [nivelId, setNivelId] = useState<NivelAutorizacionSalida | ''>('');
  const [motivo, setMotivo] = useState('');
  const [suplencia, setSuplencia] = useState('');
  const [error, setError] = useState<string | null>(null);

  const r = hoja.reglas;
  if (!r || (r.sinFactura.length === 0 && r.parciales.length === 0)) return null;
  const faltaFactura = r.sinFactura.length > 0 && !r.excepcionFacturaVigente;
  const faltaParcial = r.parciales.length > 0 && !r.parcialVigente;
  const niveles = (Object.keys(nombreNivel) as NivelAutorizacionSalida[]).filter(
    (n) => permitidos[n] && (['credito', 'ventas'].includes(n) ? faltaParcial : faltaFactura),
  );
  const suplente = nivelId === 'planeacion' || nivelId === 'contraloria' || nivelId === 'ventas';

  const enviar = (e: FormEvent) => {
    e.preventDefault();
    if (!nivelId) return;
    setError(null);
    autorizar
      .mutateAsync({ id: hoja.id, body: { nivel: nivelId, motivo: motivo.trim(), suplencia: suplente ? suplencia.trim() : null } })
      .then(() => { setNivelId(''); setMotivo(''); setSuplencia(''); })
      .catch((err) => setError(errorUsuario(err)));
  };

  return (
    <section aria-label="Autorizaciones para emitir" className="flex flex-col gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <div>
        <h2 className="text-md font-semibold">Autorizaciones para emitir {hoja.folio}</h2>
        <p className="text-ink-muted">La hoja no se emite hasta que cada regla tenga su factura o su autorización vigente.</p>
      </div>
      {r.sinFactura.length > 0 && (
        <Regla
          titulo="Sin factura timbrada del día o anterior"
          detalle={pedidos(r.sinFactura)}
          vigente={r.excepcionFacturaVigente}
          ultima={r.excepcionFactura}
        />
      )}
      {r.parciales.length > 0 && (
        <Regla titulo="Salen incompletos" detalle={pedidos(r.parciales)} vigente={r.parcialVigente} ultima={r.parcial} />
      )}
      {error && (
        <AvisoEnLinea tono="error" titulo="No se registró la autorización">
          {error}
        </AvisoEnLinea>
      )}
      {niveles.length > 0 && (
        <form className="grid gap-2 sm:grid-cols-2" onSubmit={enviar}>
          <div className="flex flex-col gap-1">
            <Label htmlFor="pt-nivel-salida">Nivel de autorización</Label>
            <select id="pt-nivel-salida" className={selectClass} value={nivelId} required onChange={(e) => setNivelId(e.target.value as NivelAutorizacionSalida | '')}>
              <option value="">Seleccione una autorización</option>
              {niveles.map((n) => <option key={n} value={n}>{nombreNivel[n]}</option>)}
            </select>
          </div>
          <div className="flex flex-col gap-1">
            <Label htmlFor="pt-motivo-salida">Motivo</Label>
            <Input id="pt-motivo-salida" required minLength={5} maxLength={500} value={motivo} onChange={(e) => setMotivo(e.target.value)} />
          </div>
          {suplente && (
            <div className="flex flex-col gap-1 sm:col-span-2">
              <Label htmlFor="pt-suplencia-salida">Por qué no pudo autorizar el nivel anterior</Label>
              <Input id="pt-suplencia-salida" required minLength={5} maxLength={500} value={suplencia} onChange={(e) => setSuplencia(e.target.value)} />
            </div>
          )}
          <div className="sm:col-span-2">
            <Button type="submit" variant="outline" disabled={autorizar.isPending || !nivelId}>Registrar autorización</Button>
          </div>
        </form>
      )}
    </section>
  );
}

function Regla({ titulo, detalle, vigente, ultima }: { titulo: string; detalle: string; vigente: boolean; ultima: AutorizacionSalidaDto | null }) {
  return (
    <div className="flex flex-col gap-1 rounded-md border border-line-divider p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="font-medium">{titulo}</h3>
        <Badge variant={vigente ? 'success' : 'warning'}>{vigente ? 'Autorizada' : 'Requiere autorización'}</Badge>
      </div>
      <p>{detalle}</p>
      {ultima && (
        <p className="text-xs text-ink-muted">
          Última autorización: {nombreNivel[ultima.nivel]} · <DateTimeDisplay value={ultima.fecha} variant="datetime" /> · {ultima.motivo}
          {ultima.suplencia ? ` · Suplencia: ${ultima.suplencia}` : ''}
          {!vigente && ' · ya no cubre la carga actual'}
        </p>
      )}
    </div>
  );
}
