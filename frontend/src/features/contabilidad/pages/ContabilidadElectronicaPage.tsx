import { useState, type ReactNode } from 'react';
import { Link } from '@tanstack/react-router';
import { CircleCheck, Download, FileCode2, TriangleAlert } from 'lucide-react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/erp/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { cn } from '@/lib/utils';
import {
  descargarArchivoCe,
  useRevisionContabilidadElectronica,
  type ArchivoCe,
  type ParametrosArchivoCe,
  type RevisionContabilidadElectronica,
  type TipoEnvioBalanza,
  type TipoSolicitudPolizas,
} from '../api/contabilidad-electronica';
import { motivoArchivoCe, resumenProblemasCe } from '../lib/contabilidad-electronica';

const SOLICITUDES: { valor: TipoSolicitudPolizas; etiqueta: string }[] = [
  { valor: 'AF', etiqueta: 'AF · Acto de fiscalización' },
  { valor: 'FC', etiqueta: 'FC · Fiscalización compulsa' },
  { valor: 'DE', etiqueta: 'DE · Devolución' },
  { valor: 'CO', etiqueta: 'CO · Compensación' },
];

const mesAnterior = () => {
  const [a, m] = hoyLocalISO().slice(0, 7).split('-').map(Number);
  return m === 1 ? `${a - 1}-12` : `${a}-${String(m - 1).padStart(2, '0')}`;
};

/**
 * K10.8 · Contabilidad electrónica del SAT (anexo 24, versión 1.3). Revisa antes de generar (K10.8-a: cuentas sin código
 * agrupador) y descarga el catálogo (CT), la balanza normal o complementaria (BN/BC) y las pólizas del periodo (PL), cada uno
 * validado en el servidor contra el esquema del SAT. El sello lo pone Millet al enviarlo con su e.firma.
 */
export function ContabilidadElectronicaPage() {
  const [periodo, setPeriodo] = useState(mesAnterior);
  const [cierre, setCierre] = useState(false);
  const [aplicado, setAplicado] = useState({ periodo: mesAnterior(), cierre: false });
  const [anio, mesBase] = aplicado.periodo.split('-').map(Number);
  const mes = aplicado.cierre ? 13 : mesBase;
  const query = useRevisionContabilidadElectronica(anio, mes);
  const revision = query.data;

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <FileCode2 className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Contabilidad electrónica
          </>
        }
        descripcion="Catálogo con código agrupador, balanza del mes y pólizas en XML del SAT (versión 1.3), validados contra el esquema oficial."
      />

      <section className="flex flex-wrap items-end gap-3 rounded-lg bg-surface-card px-5 py-4 shadow-card" aria-label="Periodo">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="ce-periodo">Mes *</Label>
          <Input id="ce-periodo" type="month" value={periodo} onChange={(e) => setPeriodo(e.target.value)} className="w-44" />
        </div>
        <div className="flex h-ctl-xl items-center gap-2">
          <Checkbox id="ce-cierre" checked={cierre} onCheckedChange={(v) => setCierre(v === true)} />
          <Label htmlFor="ce-cierre" className="font-normal">
            Periodo 13 (balanza de cierre)
          </Label>
        </div>
        <Button
          type="button"
          onClick={() => setAplicado({ periodo, cierre })}
          disabled={!periodo}
          title={periodo ? undefined : 'Elige el mes para revisar.'}
        >
          Revisar
        </Button>
      </section>

      {query.isLoading && <p className="text-sm text-ink-muted">Revisando el periodo…</p>}
      {query.error && (
        <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
          {esApiError(query.error) ? query.error.message : 'No se pudo revisar el periodo.'}{' '}
          <button type="button" className="underline" onClick={() => void query.refetch()}>
            Reintentar
          </button>
        </p>
      )}

      {revision && (
        <>
          <TarjetaRevision revision={revision} />
          <TarjetaArchivos revision={revision} />
        </>
      )}
    </div>
  );
}

function etiquetaPeriodo(anio: number, mes: number) {
  return mes === 13 ? `periodo 13 de ${anio}` : `${String(mes).padStart(2, '0')}/${anio}`;
}

/** Revisión previa: RFC, estado del periodo, avisos y la lista de cuentas que impiden generar el catálogo (K10.8-a). */
function TarjetaRevision({ revision: r }: { revision: RevisionContabilidadElectronica }) {
  return (
    <section
      aria-labelledby="ce-revision-titulo"
      className={cn(
        'flex flex-col gap-4 rounded-lg bg-surface-card px-5 py-4 shadow-card',
        r.problemas.length > 0 && 'ring-1 ring-danger-ring',
      )}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="ce-revision-titulo" className="text-lg font-semibold text-ink">
          Revisión del {etiquetaPeriodo(r.anio, r.mes)}
        </h2>
        {r.puedeGenerarCatalogo ? (
          <Badge variant="success">Catálogo listo</Badge>
        ) : (
          <Badge variant="danger">Catálogo con pendientes</Badge>
        )}
      </div>

      <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <Dato termino="RFC de la empresa" valor={r.rfc ?? 'Sin RFC'} alerta={!r.rfcValido} mono />
        <Dato termino="Estado del periodo" valor={r.estadoPeriodo} alerta={r.estadoPeriodo !== 'Cerrado'} />
        <Dato termino="Cuentas en el catálogo" valor={String(r.cuentasCatalogo)} />
        <Dato
          termino="Pólizas del mes"
          valor={r.mes === 13 ? 'No aplica' : `${r.polizas} (${r.polizasConCfdi} con CFDI)`}
        />
      </dl>

      {r.avisos.length > 0 && (
        <ul role="note" className="flex flex-col gap-1.5 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg">
          {r.avisos.map((a) => (
            <li key={a} className="flex items-start gap-2">
              <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
              <span>{a}</span>
            </li>
          ))}
        </ul>
      )}

      {r.problemas.length > 0 ? (
        <>
          <p className="text-sm text-ink">
            {resumenProblemasCe(r)} sin código agrupador del SAT o sin naturaleza. Corrígelas en el catálogo de cuentas y
            vuelve a revisar antes de generar el catálogo.
          </p>
          <Table aria-label="Cuentas pendientes para el catálogo del SAT">
            <TableHeader>
              <TableRow>
                <TableHead>Cuenta</TableHead>
                <TableHead>Nombre</TableHead>
                <TableHead>Código agrupador</TableHead>
                <TableHead>Pendiente</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {r.problemas.map((p) => (
                <TableRow key={`${p.cuentaId}-${p.problema}`}>
                  <TableCell className="font-mono text-xs">
                    <Link to="/contabilidad/catalogo/$id" params={{ id: p.cuentaId }} className="text-brand underline">
                      {p.codigo}
                    </Link>
                  </TableCell>
                  <TableCell>{p.nombre}</TableCell>
                  <TableCell className="font-mono text-xs">{p.codigoAgrupador ?? '—'}</TableCell>
                  <TableCell className="text-danger-fg">{p.problema}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </>
      ) : (
        <p className="flex items-center gap-2 text-xs text-ink-muted">
          <CircleCheck className="size-4 text-success-fg" strokeWidth={1.6} aria-hidden="true" />
          Todas las cuentas del catálogo tienen código agrupador del SAT y naturaleza.
        </p>
      )}
    </section>
  );
}

function Dato({ termino, valor, alerta, mono }: { termino: string; valor: string; alerta?: boolean; mono?: boolean }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-xs text-ink-muted">{termino}</dt>
      <dd className={cn('font-medium text-ink', mono && 'font-mono', alerta && 'text-danger-fg')}>{valor}</dd>
    </div>
  );
}

/** Los tres archivos del SAT con sus parámetros. Cada botón explica en su `title` por qué está deshabilitado. */
function TarjetaArchivos({ revision: r }: { revision: RevisionContabilidadElectronica }) {
  const [tipoEnvio, setTipoEnvio] = useState<TipoEnvioBalanza>('N');
  const [fechaModificacion, setFechaModificacion] = useState(hoyLocalISO());
  const [solicitud, setSolicitud] = useState<TipoSolicitudPolizas>('AF');
  const [numero, setNumero] = useState('');
  const [soloXml, setSoloXml] = useState(false);
  const [descargando, setDescargando] = useState<ArchivoCe | null>(null);

  const conOrden = solicitud === 'AF' || solicitud === 'FC';
  const parametros = (tipo: ArchivoCe): ParametrosArchivoCe => ({
    tipo,
    anio: r.anio,
    mes: r.mes,
    tipoEnvio,
    fechaModificacion,
    solicitud,
    numOrden: conOrden ? numero.trim().toUpperCase() : undefined,
    numTramite: conOrden ? undefined : numero.trim().toUpperCase(),
    formato: soloXml ? 'xml' : 'zip',
  });

  async function descargar(tipo: ArchivoCe) {
    setDescargando(tipo);
    try {
      const nombre = await descargarArchivoCe(parametros(tipo));
      toast.success(`Se descargó ${nombre}.`);
    } catch (e) {
      toast.error(esApiError(e) ? (e.problem.detail ?? e.message) : 'No se pudo generar el archivo.');
    } finally {
      setDescargando(null);
    }
  }

  const motivo = (tipo: ArchivoCe) => motivoArchivoCe(tipo, r, parametros(tipo)) ?? (descargando ? 'Generando otro archivo…' : undefined);

  return (
    <section
      aria-labelledby="ce-archivos-titulo"
      className="flex flex-col gap-4 rounded-lg bg-surface-card px-5 py-4 shadow-card"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="ce-archivos-titulo" className="text-lg font-semibold text-ink">
          Archivos del SAT
        </h2>
        <div className="flex items-center gap-2">
          <Checkbox id="ce-solo-xml" checked={soloXml} onCheckedChange={(v) => setSoloXml(v === true)} />
          <Label htmlFor="ce-solo-xml" className="font-normal">
            Descargar el XML sin comprimir
          </Label>
        </div>
      </div>
      <p className="text-xs text-ink-muted">
        Por omisión se descarga el .zip con el nombre que pide el buzón tributario (RFC, año, mes y tipo). El sello lo pone
        Millet al enviarlo con su e.firma.
      </p>

      <div className="grid gap-4 lg:grid-cols-3">
        <Archivo titulo="Catálogo de cuentas (CT)" descripcion="Cuentas activas con código agrupador, nivel, naturaleza y cuenta de mayor.">
          <BotonDescarga tipo="catalogo" motivo={motivo('catalogo')} descargando={descargando} onClick={descargar} />
        </Archivo>

        <Archivo titulo="Balanza de comprobación (BN/BC)" descripcion="Saldo inicial, cargos, abonos y saldo final de cada cuenta, según su naturaleza.">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="ce-tipo-envio">Tipo de envío</Label>
            <Select value={tipoEnvio} onValueChange={(v) => setTipoEnvio(v as TipoEnvioBalanza)}>
              <SelectTrigger id="ce-tipo-envio" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="N">Normal</SelectItem>
                <SelectItem value="C">Complementaria</SelectItem>
              </SelectContent>
            </Select>
          </div>
          {tipoEnvio === 'C' && (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="ce-fecha-mod">Fecha de la última modificación *</Label>
              <Input id="ce-fecha-mod" type="date" value={fechaModificacion} onChange={(e) => setFechaModificacion(e.target.value)} />
            </div>
          )}
          <BotonDescarga tipo="balanza" motivo={motivo('balanza')} descargando={descargando} onClick={descargar} />
        </Archivo>

        <Archivo titulo="Pólizas del periodo (PL)" descripcion="Solo cuando el SAT las pide: transacciones de cada póliza y el CFDI de origen.">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="ce-solicitud">Tipo de solicitud</Label>
            <Select value={solicitud} onValueChange={(v) => setSolicitud(v as TipoSolicitudPolizas)}>
              <SelectTrigger id="ce-solicitud" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {SOLICITUDES.map((s) => (
                  <SelectItem key={s.valor} value={s.valor}>
                    {s.etiqueta}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="ce-numero">{conOrden ? 'Número de orden *' : 'Número de trámite *'}</Label>
            <Input
              id="ce-numero"
              value={numero}
              onChange={(e) => setNumero(e.target.value)}
              placeholder={conOrden ? 'ABC1234567/12' : 'AB123456789012'}
              className="font-mono"
            />
          </div>
          <BotonDescarga tipo="polizas" motivo={motivo('polizas')} descargando={descargando} onClick={descargar} />
        </Archivo>
      </div>
    </section>
  );
}

function Archivo({ titulo, descripcion, children }: { titulo: string; descripcion: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-3 rounded-md border border-line px-4 py-3">
      <div>
        <h3 className="text-sm font-semibold text-ink">{titulo}</h3>
        <p className="text-xs text-ink-muted">{descripcion}</p>
      </div>
      {children}
    </div>
  );
}

function BotonDescarga({
  tipo,
  motivo,
  descargando,
  onClick,
}: {
  tipo: ArchivoCe;
  motivo: string | undefined;
  descargando: ArchivoCe | null;
  onClick: (tipo: ArchivoCe) => void;
}) {
  return (
    <div className="mt-auto flex flex-col gap-1">
      <Button type="button" onClick={() => onClick(tipo)} disabled={motivo !== undefined} title={motivo}>
        <Download className="size-4" strokeWidth={1.6} aria-hidden="true" />
        {descargando === tipo ? 'Generando…' : 'Descargar'}
      </Button>
      {motivo && descargando !== tipo && <p className="text-xs text-ink-muted">{motivo}</p>}
    </div>
  );
}
