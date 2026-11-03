import { useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient, type UseQueryResult } from '@tanstack/react-query';
import { CircleCheck, FileText, Inbox, LogOut, TriangleAlert, Upload } from 'lucide-react';
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { EmptyState } from '@/components/erp/feedback/EmptyState';
import { KpiCard } from '@/components/erp/display/KpiCard';
import { cn } from '@/lib/utils';
import {
  cerrarSesionPortal,
  emailPortal,
  iniciarSesion,
  listarFacturas,
  listarPagos,
  PortalSesionExpirada,
  solicitarCodigo,
  subirCfdi,
  tokenPortal,
  type FacturaPortal,
  type PagoPortal,
  type ResultadoSubida,
} from './portal-api';

const dinero = (n: number, moneda = 'MXN') => n.toLocaleString('es-MX', { style: 'currency', currency: moneda });
const fecha = (iso: string) =>
  new Date(iso.length === 10 ? `${iso}T00:00:00` : iso).toLocaleDateString('es-MX', { day: '2-digit', month: '2-digit', year: 'numeric' });

type Seccion = 'facturas' | 'pagos' | 'subir';
const SECCIONES: Seccion[] = ['facturas', 'pagos', 'subir'];
/** La sección vive en `?seccion=` para que sobreviva a recargar y se pueda compartir el enlace. */
const seccionInicial = (): Seccion => {
  const s = new URLSearchParams(window.location.search).get('seccion');
  return SECCIONES.find((x) => x === s) ?? 'facturas';
};
const guardarSeccion = (s: Seccion) => {
  const url = new URL(window.location.href);
  url.searchParams.set('seccion', s);
  window.history.replaceState(null, '', url);
};

/** Portal de proveedores (P8.3): entrada aparte del ERP, sin menú ni sesión de Entra. */
export function PortalProveedoresPage() {
  const [sesion, setSesion] = useState(() => tokenPortal() !== null);
  return sesion ? <Portal alSalir={() => setSesion(false)} /> : <Acceso alEntrar={() => setSesion(true)} />;
}

function Marca({ className }: { className?: string }) {
  return (
    <div className={cn('flex items-center gap-2.5', className)}>
      <img src="/favicon.svg" alt="" className="size-8 rounded-md" />
      <div className="leading-tight">
        <div className="text-sm font-semibold text-ink">Millet</div>
        <div className="text-xs text-ink-muted">Portal de proveedores</div>
      </div>
    </div>
  );
}

function Acceso({ alEntrar }: { alEntrar: () => void }) {
  const [email, setEmail] = useState('');
  const [codigo, setCodigo] = useState('');
  const [enviado, setEnviado] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [ocupado, setOcupado] = useState(false);

  async function ejecutar(accion: () => Promise<void>) {
    setError(null);
    setAviso(null);
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
    <main className="flex min-h-screen flex-col items-center justify-center bg-surface-page p-4">
      <Card className="w-full max-w-sm p-6 sm:p-8">
        <Marca className="mb-6" />
        <h1 className="text-lg font-semibold text-ink">{enviado ? 'Escribe tu código' : 'Entra a tu portal'}</h1>
        <p className="mb-5 mt-1 text-sm text-ink-muted">
          {enviado
            ? 'Si el correo está autorizado, te enviamos un código de 6 dígitos. Vence en 10 minutos.'
            : 'Consulta tus facturas y pagos, y sube tus facturas y complementos. Te enviaremos un código de 6 dígitos a tu correo; no necesitas contraseña.'}
        </p>
        <form
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault();
            void ejecutar(async () => {
              if (!enviado) {
                await solicitarCodigo(email.trim());
                setEnviado(true);
              } else if (await iniciarSesion(email.trim(), codigo.trim())) {
                alEntrar();
              } else {
                setError('El código no es válido o venció. Pide uno nuevo con «Reenviar código».');
              }
            });
          }}
        >
          <div className="space-y-1.5">
            <Label htmlFor="portal-email">Correo autorizado</Label>
            <Input id="portal-email" type="email" required autoComplete="email" placeholder="nombre@tuempresa.com" value={email} disabled={enviado || ocupado}
              onChange={(e) => setEmail(e.target.value)} />
          </div>
          {enviado && (
            <div className="space-y-1.5">
              <Label htmlFor="portal-codigo">Código de acceso</Label>
              <Input id="portal-codigo" inputMode="numeric" autoComplete="one-time-code" maxLength={6} required autoFocus
                className="text-center font-mono text-lg tracking-[0.4em]" placeholder="000000" value={codigo}
                onChange={(e) => setCodigo(e.target.value.replace(/\D/g, ''))} disabled={ocupado} />
            </div>
          )}
          {error && <Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>}
          {aviso && <p role="status" className="text-sm text-success-fg">{aviso}</p>}
          <Button type="submit" size="lg" className="w-full" disabled={ocupado}>
            {ocupado ? 'Un momento…' : enviado ? 'Entrar' : 'Enviar código'}
          </Button>
          {enviado && (
            <div className="flex flex-wrap justify-between gap-2">
              <Button type="button" variant="ghost" size="sm" disabled={ocupado}
                onClick={() => void ejecutar(async () => { await solicitarCodigo(email.trim()); setAviso('Te enviamos un código nuevo.'); })}>
                Reenviar código
              </Button>
              <Button type="button" variant="ghost" size="sm" disabled={ocupado}
                onClick={() => { setEnviado(false); setCodigo(''); setError(null); setAviso(null); }}>
                Usar otro correo
              </Button>
            </div>
          )}
        </form>
      </Card>
      <p className="mt-4 max-w-sm text-center text-xs text-ink-muted">
        ¿No te llega el código? Pide a tu contacto de Cuentas por Pagar que autorice tu correo.
      </p>
    </main>
  );
}

function Portal({ alSalir }: { alSalir: () => void }) {
  const [seccion, setSeccion] = useState<Seccion>(seccionInicial);
  const facturas = useQuery({ queryKey: ['portal-facturas'], queryFn: listarFacturas, retry: false });
  const pagos = useQuery({ queryKey: ['portal-pagos'], queryFn: listarPagos, retry: false, enabled: seccion === 'pagos' });
  const expirada = facturas.error instanceof PortalSesionExpirada || pagos.error instanceof PortalSesionExpirada;
  useEffect(() => { if (expirada) alSalir(); }, [expirada, alSalir]);
  useEffect(() => guardarSeccion(seccion), [seccion]);
  if (expirada) return null;

  return (
    <div className="min-h-screen bg-surface-page">
      <header className="border-b border-line bg-surface-card">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-x-4 gap-y-2 px-4 py-3">
          <Marca />
          <div className="flex min-w-0 items-center gap-3">
            <div className="min-w-0 text-right leading-tight">
              <div className="text-xs text-ink-muted">Sesión de proveedor</div>
              <div className="max-w-[14rem] truncate text-sm font-medium text-ink sm:max-w-xs">{emailPortal() ?? 'Proveedor'}</div>
            </div>
            <Button variant="outline" size="sm" onClick={() => { cerrarSesionPortal(); alSalir(); }}>
              <LogOut aria-hidden="true" />
              Cerrar sesión
            </Button>
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-5xl px-4 py-5">
        <h1 className="sr-only">Portal de proveedores Millet</h1>
        <Tabs value={seccion} onValueChange={(v) => setSeccion(v as Seccion)}>
          <TabsList aria-label="Secciones del portal" className="flex-nowrap overflow-x-auto">
            <TabsTrigger value="facturas">Mis facturas</TabsTrigger>
            <TabsTrigger value="pagos">Pagos y complementos que debo</TabsTrigger>
            <TabsTrigger value="subir">Subir factura o complemento</TabsTrigger>
          </TabsList>
          <TabsContent value="facturas"><MisFacturas q={facturas} ir={setSeccion} /></TabsContent>
          <TabsContent value="pagos"><MisPagos q={pagos} /></TabsContent>
          <TabsContent value="subir"><SubirDocumento alExpirar={alSalir} ir={setSeccion} /></TabsContent>
        </Tabs>
      </main>
    </div>
  );
}

function Cargando({ filas = 4 }: { filas?: number }) {
  return (
    <div aria-busy="true" aria-label="Cargando" className="space-y-2">
      {Array.from({ length: filas }, (_, i) => <Skeleton key={i} className="h-10 w-full" />)}
    </div>
  );
}

function ErrorCarga({ q }: { q: UseQueryResult }) {
  return (
    <Alert variant="destructive" className="flex flex-wrap items-center justify-between gap-3">
      <div>
        <AlertTitle>No pudimos cargar tu información</AlertTitle>
        <AlertDescription>{q.error instanceof Error ? q.error.message : 'Intenta de nuevo.'}</AlertDescription>
      </div>
      <Button variant="outline" size="sm" onClick={() => void q.refetch()}>Reintentar</Button>
    </Alert>
  );
}

const tonoEstado = (estado: string): NonNullable<BadgeProps['variant']> =>
  estado.startsWith('Detenida') ? 'danger'
    : estado === 'Pagada' ? 'success'
    : estado === 'Programada para pago' ? 'info'
    : estado === 'En revisión' ? 'warning'
    : 'neutral';

/** Una factura cae en un solo grupo; las canceladas no cuentan. */
const grupoDe = (f: FacturaPortal) =>
  f.detenida || f.estado.startsWith('Detenida') ? 'detenidas'
    : f.estado === 'Programada para pago' ? 'programadas'
    : f.estado === 'Pagada' ? 'pagadas'
    : f.estado === 'Cancelada' ? null
    : 'porPagar';

function resumir(facturas: FacturaPortal[]) {
  const g = { porPagar: [] as FacturaPortal[], programadas: [] as FacturaPortal[], detenidas: [] as FacturaPortal[], pagadas: [] as FacturaPortal[] };
  for (const f of facturas) { const k = grupoDe(f); if (k) g[k].push(f); }
  const importes = (l: FacturaPortal[]) => {
    const porMoneda = new Map<string, number>();
    for (const f of l) porMoneda.set(f.moneda, (porMoneda.get(f.moneda) ?? 0) + f.total);
    return porMoneda.size === 0 ? dinero(0) : [...porMoneda].map(([m, t]) => dinero(t, m)).join(' · ');
  };
  return { g, importes };
}

const esDeComplemento = (f: FacturaPortal) => f.estado.includes('complemento');

function MisFacturas({ q, ir }: { q: UseQueryResult<FacturaPortal[]>; ir: (s: Seccion) => void }) {
  if (q.isPending) return <Cargando />;
  if (q.isError) return <ErrorCarga q={q} />;
  const facturas = q.data;
  if (facturas.length === 0) {
    return (
      <Card><EmptyState icon={<Inbox className="size-10" />} title="Todavía no tenemos facturas tuyas."
        description="Cuando subas una factura o Cuentas por Pagar la registre, la verás aquí."
        action={<Button onClick={() => ir('subir')}>Subir factura</Button>} /></Card>
    );
  }
  const { g, importes } = resumir(facturas);
  const tarjetas = [
    { etiqueta: 'Por pagar', tono: 'neutral', lista: g.porPagar },
    { etiqueta: 'Programadas', tono: 'info', lista: g.programadas },
    { etiqueta: 'Detenidas', tono: 'danger', lista: g.detenidas },
    { etiqueta: 'Pagadas', tono: 'success', lista: g.pagadas },
  ] as const;
  return (
    <div className="space-y-4">
      <section aria-label="Resumen de tus facturas" className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        {tarjetas.map((t) => (
          <KpiCard key={t.etiqueta} etiqueta={t.etiqueta} tono={t.tono} valor={t.lista.length}
            subdato={<span className="tabular-nums">{importes(t.lista)}</span>} />
        ))}
      </section>
      {g.detenidas.length > 0 && (
        <Alert variant="warning" role="region" aria-label="Facturas detenidas">
          <TriangleAlert aria-hidden="true" className="size-4" />
          <AlertTitle>{g.detenidas.length === 1 ? 'Una factura está detenida' : `${g.detenidas.length} facturas están detenidas`}</AlertTitle>
          <AlertDescription>
            <ul className="mt-1 space-y-2">
              {g.detenidas.map((f) => (
                <li key={f.id} className="flex flex-wrap items-center justify-between gap-2">
                  <span>
                    <strong>Factura {f.folio ?? f.uuid ?? ''}:</strong>{' '}
                    {esDeComplemento(f)
                      ? 'sube el complemento en la pestaña «Subir».'
                      : 'sube tu opinión de cumplimiento con tu contacto de Cuentas por Pagar.'}
                  </span>
                  {esDeComplemento(f) && <Button size="sm" variant="outline" onClick={() => ir('subir')}>Ir a Subir</Button>}
                </li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      )}
      <Card className="overflow-x-auto">
        <Table className="min-w-[44rem]">
          <TableHeader>
            <tr>
              <TableHead>Factura</TableHead>
              <TableHead>Fecha</TableHead>
              <TableHead className="text-right">Total</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>Vencimiento</TableHead>
              <TableHead>Fecha programada de pago</TableHead>
            </tr>
          </TableHeader>
          <TableBody>
            {facturas.map((f) => (
              <TableRow key={f.id} className={cn('align-top', f.detenida && 'bg-warning-note-bg')}>
                <TableCell>
                  <div className="font-medium text-ink">{f.folio ?? '—'}</div>
                  <div title={f.uuid ?? undefined} className="max-w-[10rem] truncate font-mono text-2xs text-ink-muted">UUID {f.uuid ?? '—'}</div>
                </TableCell>
                <TableCell className="tabular-nums">{fecha(f.fecha)}</TableCell>
                <TableCell className="text-right tabular-nums">
                  {dinero(f.total, f.moneda)} <span className="text-2xs text-ink-muted">{f.moneda}</span>
                </TableCell>
                <TableCell>
                  <Badge variant={tonoEstado(f.estado)} className="h-auto min-h-ctl-xs whitespace-normal py-0.5">{f.estado}</Badge>
                  {f.motivo && <div className="mt-1 max-w-[16rem] text-xs text-ink-muted">{f.motivo}</div>}
                </TableCell>
                <TableCell className="tabular-nums">{fecha(f.fechaVencimiento)}</TableCell>
                <TableCell className="tabular-nums">
                  {f.fechaProgramadaPago ? fecha(f.fechaProgramadaPago) : <span className="text-ink-muted">{f.estado === 'Pagada' ? '—' : 'Sin programar'}</span>}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>
    </div>
  );
}

function MisPagos({ q }: { q: UseQueryResult<PagoPortal[]> }) {
  if (q.isPending) return <Cargando />;
  if (q.isError) return <ErrorCarga q={q} />;
  if (q.data.length === 0) {
    return <Card><EmptyState icon={<Inbox className="size-10" />} title="Todavía no hay pagos aplicados." description="Cuando te paguemos una factura, aparecerá aquí." /></Card>;
  }
  const pendientes = q.data.filter((p) => p.complementoPendiente > 0).length;
  return (
    <div className="space-y-4">
      {pendientes > 0 ? (
        <Alert variant="warning" role="region" aria-label="Complementos pendientes">
          <TriangleAlert aria-hidden="true" className="size-4" />
          <AlertDescription>
            Te falta {pendientes === 1 ? 'un complemento' : `${pendientes} complementos`} de pago para liberar tus facturas. Súbelo en la pestaña «Subir».
          </AlertDescription>
        </Alert>
      ) : (
        <p className="flex items-center gap-2 text-sm text-success-fg"><CircleCheck aria-hidden="true" className="size-4" />Estás al corriente con tus complementos.</p>
      )}
      <Card className="overflow-x-auto">
        <Table className="min-w-[36rem]">
          <TableHeader>
            <tr>
              <TableHead>Factura</TableHead>
              <TableHead>Fecha de pago</TableHead>
              <TableHead className="text-right">Importe</TableHead>
              <TableHead className="text-right">Complemento pendiente</TableHead>
            </tr>
          </TableHeader>
          <TableBody>
            {q.data.map((p) => (
              <TableRow key={p.pagoId}>
                <TableCell className="font-medium text-ink">{p.folio ?? '—'}</TableCell>
                <TableCell className="tabular-nums">{fecha(p.fechaPago)}</TableCell>
                <TableCell className="text-right tabular-nums">{dinero(p.importe)}</TableCell>
                <TableCell className="text-right tabular-nums">
                  {p.complementoPendiente > 0
                    ? <Badge variant="warning" className="h-auto whitespace-normal py-0.5 text-right">Falta el complemento de pago por {dinero(p.complementoPendiente)}</Badge>
                    : <Badge variant="success">Al corriente</Badge>}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Card>
    </div>
  );
}

/** Zona para soltar o elegir un archivo; el `<input>` real queda accesible por teclado y por etiqueta. */
function ZonaArchivo({ id, titulo, ayuda, extension, archivo, alElegir }: {
  id: string; titulo: string; ayuda: string; extension: '.xml' | '.pdf'; archivo: File | null; alElegir: (f: File | null) => void;
}) {
  const [encima, setEncima] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);
  const elegir = (f: File | null) => {
    if (f && !f.name.toLowerCase().endsWith(extension)) {
      setAviso(`Ese archivo no es ${extension.toUpperCase().slice(1)}. Elige un archivo ${extension}.`);
      return;
    }
    setAviso(null);
    alElegir(f);
  };
  return (
    <div>
      <label
        htmlFor={id}
        onDragOver={(e) => { e.preventDefault(); setEncima(true); }}
        onDragLeave={() => setEncima(false)}
        onDrop={(e) => { e.preventDefault(); setEncima(false); elegir(e.dataTransfer.files[0] ?? null); }}
        className={cn(
          'flex cursor-pointer flex-col items-center gap-1 rounded-lg border border-dashed border-line-control bg-surface-card px-4 py-6 text-center transition-colors hover:bg-surface-subtle focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-brand',
          encima && 'border-brand bg-surface-selected',
        )}
      >
        {archivo ? <FileText aria-hidden="true" className="size-6 text-brand" /> : <Upload aria-hidden="true" className="size-6 text-ink-muted" />}
        <span className="text-sm font-medium text-ink">{titulo}</span>
        {archivo
          ? <span className="max-w-full break-all text-sm text-brand-hover">{archivo.name} · {(archivo.size / 1024).toFixed(0)} KB</span>
          : <span className="text-xs text-ink-muted">{ayuda}</span>}
        <span className="mt-1 text-xs font-medium text-brand">{archivo ? 'Cambiar archivo' : 'Arrastra aquí o elige un archivo'}</span>
        <input id={id} aria-label={titulo} type="file" className="sr-only" accept={extension === '.xml' ? '.xml,text/xml,application/xml' : '.pdf,application/pdf'}
          onChange={(e) => { elegir(e.target.files?.[0] ?? null); e.target.value = ''; }} />
      </label>
      {aviso && <p role="status" className="mt-1.5 text-xs text-danger-fg">{aviso}</p>}
    </div>
  );
}

function SubirDocumento({ alExpirar, ir }: { alExpirar: () => void; ir: (s: Seccion) => void }) {
  const [xml, setXml] = useState<File | null>(null);
  const [pdf, setPdf] = useState<File | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const [resultado, setResultado] = useState<{ ok: true; r: ResultadoSubida } | { ok: false; texto: string } | null>(null);
  const panel = useRef<HTMLDivElement>(null);
  const queryClient = useQueryClient();

  async function enviar(event: React.FormEvent) {
    event.preventDefault();
    if (!xml) return;
    setOcupado(true);
    setResultado(null);
    try {
      const r = await subirCfdi(xml, pdf);
      setResultado({ ok: true, r });
      // Un complemento ligado cambia facturas y pagos: sin esto seguían diciendo «Falta el complemento» hasta recargar.
      void queryClient.invalidateQueries({ queryKey: ['portal-facturas'] });
      void queryClient.invalidateQueries({ queryKey: ['portal-pagos'] });
      setXml(null);
      setPdf(null);
    } catch (e) {
      if (e instanceof PortalSesionExpirada) return alExpirar();
      setResultado({ ok: false, texto: e instanceof Error ? e.message : 'Intenta de nuevo.' });
    } finally {
      setOcupado(false);
    }
  }
  useEffect(() => { if (resultado) panel.current?.scrollIntoView?.({ block: 'nearest' }); }, [resultado]);

  return (
    <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_20rem]">
      <form className="space-y-4" onSubmit={(e) => void enviar(e)}>
        <div>
          <h2 className="text-base font-semibold text-ink">Sube tu factura o complemento</h2>
          <p className="mt-1 text-sm text-ink-muted">
            Sube el XML de tu factura (tipo Ingreso) o de tu complemento de pago (tipo Pago). El PDF es opcional. Solo se aceptan archivos .xml y .pdf.
          </p>
        </div>
        <ZonaArchivo id="portal-xml" titulo="Archivo XML" ayuda="Obligatorio · CFDI en formato .xml" extension=".xml" archivo={xml} alElegir={setXml} />
        <ZonaArchivo id="portal-pdf" titulo="Archivo PDF (opcional)" ayuda="Representación impresa · formato .pdf" extension=".pdf" archivo={pdf} alElegir={setPdf} />
        <Button type="submit" size="lg" className="w-full sm:w-auto" disabled={!xml || ocupado}>{ocupado ? 'Subiendo…' : 'Subir documento'}</Button>
        {resultado && (
          <div ref={panel}>
            {resultado.ok ? <ResultadoOk r={resultado.r} ir={ir} /> : (
              <Alert variant="destructive">
                <AlertTitle>No pudimos aceptar el documento</AlertTitle>
                <AlertDescription>{resultado.texto}</AlertDescription>
                <p className="mt-1 text-xs">Revisa que sea un CFDI de tu empresa e inténtalo de nuevo.</p>
              </Alert>
            )}
          </div>
        )}
      </form>
      <Card className="h-fit space-y-2 p-4 text-sm text-ink-secondary">
        <h3 className="font-semibold text-ink">¿Qué pasa después?</h3>
        <ul className="list-disc space-y-1.5 pl-4">
          <li>Si es una <strong>factura</strong>, Cuentas por Pagar la revisa y la programa para pago.</li>
          <li>Si es un <strong>complemento de pago</strong>, lo ligamos a tu pago y se libera tu factura.</li>
          <li>Si ya lo habías subido, te lo decimos y no se duplica.</li>
        </ul>
      </Card>
    </div>
  );
}

function ResultadoOk({ r, ir }: { r: ResultadoSubida; ir: (s: Seccion) => void }) {
  const repetido = r.resultado === 'YaLoTeniamos';
  const ligado = r.mensaje.includes('Ligamos tu complemento');
  const titulo = repetido ? 'Documento repetido' : ligado ? 'Complemento ligado' : 'Documento recibido';
  const siguiente = ligado
    ? 'Tu factura se libera en unos segundos. Revisa «Pagos y complementos que debo».'
    : repetido ? 'No hace falta que lo vuelvas a subir.' : 'Cuentas por Pagar lo revisará; verás el avance en «Mis facturas».';
  return (
    <Alert className="border-line bg-surface-card">
      <CircleCheck aria-hidden="true" className="size-4 text-success" />
      <AlertTitle>{titulo}</AlertTitle>
      <AlertDescription>
        <p>{r.mensaje}</p>
        <p className="mt-1 text-xs text-ink-muted">{siguiente}</p>
        <div className="mt-3 flex flex-wrap gap-2">
          <Button type="button" size="sm" variant="outline" onClick={() => ir('facturas')}>Ver mis facturas</Button>
          <Button type="button" size="sm" variant="outline" onClick={() => ir('pagos')}>Ver mis pagos</Button>
        </div>
      </AlertDescription>
    </Alert>
  );
}
