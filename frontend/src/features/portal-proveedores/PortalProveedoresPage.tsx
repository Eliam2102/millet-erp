import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  cerrarSesionPortal,
  iniciarSesion,
  listarFacturas,
  listarPagos,
  PortalSesionExpirada,
  solicitarCodigo,
  subirCfdi,
  tokenPortal,
} from './portal-api';

const dinero = (n: number, moneda = 'MXN') => n.toLocaleString('es-MX', { style: 'currency', currency: moneda });
const fecha = (iso: string) => new Date(iso.length === 10 ? `${iso}T00:00:00` : iso).toLocaleDateString('es-MX');

/** Portal de proveedores (P8.3): entrada aparte del ERP, sin menú ni sesión de Entra. */
export function PortalProveedoresPage() {
  const [sesion, setSesion] = useState(() => tokenPortal() !== null);
  return (
    <main className="mx-auto min-h-screen max-w-4xl p-6">
      <h1 className="mb-6 text-xl font-semibold text-ink">Portal de proveedores Millet</h1>
      {sesion ? (
        <MisDocumentos alExpirar={() => setSesion(false)} />
      ) : (
        <Acceso alEntrar={() => setSesion(true)} />
      )}
    </main>
  );
}

function Acceso({ alEntrar }: { alEntrar: () => void }) {
  const [email, setEmail] = useState('');
  const [codigo, setCodigo] = useState('');
  const [enviado, setEnviado] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);

  async function ejecutar(accion: () => Promise<void>) {
    setError(null);
    setOcupado(true);
    try {
      await accion();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Intenta de nuevo.');
    } finally {
      setOcupado(false);
    }
  }

  return (
    <form
      className="max-w-sm space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        void ejecutar(async () => {
          if (!enviado) {
            await solicitarCodigo(email.trim());
            setEnviado(true);
          } else if (await iniciarSesion(email.trim(), codigo.trim())) {
            alEntrar();
          } else {
            setError('El código no es válido o venció. Solicita uno nuevo.');
          }
        });
      }}
    >
      <div>
        <Label htmlFor="portal-email">Correo autorizado</Label>
        <Input id="portal-email" type="email" required value={email} disabled={enviado || ocupado}
          onChange={(e) => setEmail(e.target.value)} />
      </div>
      {enviado && (
        <div>
          <p className="mb-2 text-sm text-ink-muted">
            Si el correo está autorizado, te enviamos un código de 6 dígitos. Vence en 10 minutos.
          </p>
          <Label htmlFor="portal-codigo">Código de acceso</Label>
          <Input id="portal-codigo" inputMode="numeric" autoComplete="one-time-code" maxLength={6} required value={codigo}
            onChange={(e) => setCodigo(e.target.value)} disabled={ocupado} />
        </div>
      )}
      {error && <Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>}
      <div className="flex gap-2">
        <Button type="submit" disabled={ocupado}>{enviado ? 'Entrar' : 'Enviar código'}</Button>
        {enviado && (
          <Button type="button" variant="ghost" disabled={ocupado} onClick={() => { setEnviado(false); setCodigo(''); setError(null); }}>
            Usar otro correo
          </Button>
        )}
      </div>
    </form>
  );
}

function MisDocumentos({ alExpirar }: { alExpirar: () => void }) {
  const [vista, setVista] = useState<'facturas' | 'pagos' | 'subir'>('facturas');
  const facturas = useQuery({ queryKey: ['portal-facturas'], queryFn: listarFacturas, retry: false });
  const pagos = useQuery({ queryKey: ['portal-pagos'], queryFn: listarPagos, retry: false, enabled: vista === 'pagos' });
  const activa = vista === 'pagos' ? pagos : facturas;

  if (activa.error instanceof PortalSesionExpirada) {
    alExpirar();
    return null;
  }

  return (
    <div>
      <div className="mb-4 flex items-center justify-between gap-2">
        <div role="tablist" className="flex gap-2">
          <Button role="tab" aria-selected={vista === 'facturas'} variant={vista === 'facturas' ? 'default' : 'ghost'} onClick={() => setVista('facturas')}>
            Mis facturas
          </Button>
          <Button role="tab" aria-selected={vista === 'pagos'} variant={vista === 'pagos' ? 'default' : 'ghost'} onClick={() => setVista('pagos')}>
            Pagos y complementos que debo
          </Button>
          <Button role="tab" aria-selected={vista === 'subir'} variant={vista === 'subir' ? 'default' : 'ghost'} onClick={() => setVista('subir')}>
            Subir factura o complemento
          </Button>
        </div>
        <Button variant="ghost" onClick={() => { cerrarSesionPortal(); alExpirar(); }}>Salir</Button>
      </div>
      {vista === 'subir' && <SubirDocumento alExpirar={alExpirar} />}
      {vista !== 'subir' && activa.isPending && <p className="text-sm text-ink-muted">Cargando…</p>}
      {vista !== 'subir' && activa.isError && <Alert variant="destructive"><AlertDescription>{activa.error.message}</AlertDescription></Alert>}
      {vista === 'facturas' && facturas.data && (
        facturas.data.length === 0 ? <p className="text-sm text-ink-muted">Todavía no tenemos facturas tuyas.</p> : (
          <table className="w-full text-sm">
            <thead><tr className="text-left"><th>Folio</th><th>UUID</th><th>Fecha</th><th className="text-right">Total</th><th>Estado</th><th>Pago programado</th></tr></thead>
            <tbody>
              {facturas.data.map((f) => (
                <tr key={f.id} className="border-t">
                  <td>{f.folio ?? '—'}</td>
                  <td className="font-mono text-xs">{f.uuid ?? '—'}</td>
                  <td>{fecha(f.fecha)}</td>
                  <td className="text-right tabular-nums">{dinero(f.total, f.moneda)}</td>
                  <td>
                    {f.estado}
                    {f.motivo && <div className="text-xs text-ink-muted">{f.motivo}</div>}
                  </td>
                  <td>{f.fechaProgramadaPago ? fecha(f.fechaProgramadaPago) : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      )}
      {vista === 'pagos' && pagos.data && (
        pagos.data.length === 0 ? <p className="text-sm text-ink-muted">Todavía no hay pagos aplicados.</p> : (
          <table className="w-full text-sm">
            <thead><tr className="text-left"><th>Factura</th><th>Fecha de pago</th><th className="text-right">Importe</th><th className="text-right">Complemento pendiente</th></tr></thead>
            <tbody>
              {pagos.data.map((p) => (
                <tr key={p.pagoId} className="border-t">
                  <td>{p.folio ?? '—'}</td>
                  <td>{fecha(p.fechaPago)}</td>
                  <td className="text-right tabular-nums">{dinero(p.importe)}</td>
                  <td className="text-right tabular-nums">
                    {p.complementoPendiente > 0 ? `Falta el complemento de pago por ${dinero(p.complementoPendiente)}` : 'Al corriente'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )
      )}
    </div>
  );
}

function SubirDocumento({ alExpirar }: { alExpirar: () => void }) {
  const [xml, setXml] = useState<File | null>(null);
  const [pdf, setPdf] = useState<File | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [mensaje, setMensaje] = useState<{ ok: boolean; texto: string } | null>(null);

  async function enviar(event: React.FormEvent) {
    event.preventDefault();
    if (!xml) return;
    setOcupado(true);
    setMensaje(null);
    try {
      const r = await subirCfdi(xml, pdf);
      setMensaje({ ok: true, texto: r.resultado === 'YaLoTeniamos' ? 'Ya lo teníamos.' : 'Recibimos tu documento.' });
      setXml(null);
      setPdf(null);
    } catch (e) {
      if (e instanceof PortalSesionExpirada) return alExpirar();
      setMensaje({ ok: false, texto: e instanceof Error ? e.message : 'Intenta de nuevo.' });
    } finally {
      setOcupado(false);
    }
  }

  return (
    <form className="max-w-sm space-y-4" onSubmit={(e) => void enviar(e)}>
      <p className="text-sm text-ink-muted">Sube tu factura (tipo Ingreso) o tu complemento de pago (tipo Pago) en XML. El PDF es opcional.</p>
      <div>
        <Label htmlFor="portal-xml">Archivo XML</Label>
        <Input id="portal-xml" type="file" accept=".xml,text/xml,application/xml" onChange={(e) => setXml(e.target.files?.[0] ?? null)} />
      </div>
      <div>
        <Label htmlFor="portal-pdf">Archivo PDF (opcional)</Label>
        <Input id="portal-pdf" type="file" accept=".pdf,application/pdf" onChange={(e) => setPdf(e.target.files?.[0] ?? null)} />
      </div>
      {mensaje && (
        <Alert variant={mensaje.ok ? 'default' : 'destructive'}><AlertDescription>{mensaje.texto}</AlertDescription></Alert>
      )}
      <Button type="submit" disabled={!xml || ocupado}>{ocupado ? 'Subiendo…' : 'Subir documento'}</Button>
    </form>
  );
}
