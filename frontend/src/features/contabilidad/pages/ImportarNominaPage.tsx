import { useMemo, useRef, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { CircleCheck, Download, FileUp, TriangleAlert } from 'lucide-react';
import { toast } from 'sonner';
import { PageHeader } from '@/components/erp/PageHeader';
import { useTiposDocumento as useTiposAdjunto } from '@/components/erp/adjuntos/api/hooks';
import { useUploadFile } from '@/lib/hooks/useUploadFile';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { cn } from '@/lib/utils';
import { useTiposDocumento } from '../api/dimensiones';
import { useImportarNomina, useVistaPreviaNomina, type VistaPreviaNomina } from '../api/nomina';
import type { CuerpoImportacion } from '../api/types';
import { abrirArchivo, ArchivoInvalidoError, detectarEncabezado, prepararHoja } from '../lib/archivo';
import { ALIAS_NOMINA, filaReal, motivoImportar, PLANTILLA_NOMINA } from '../lib/nomina';
import { monto } from '../lib/polizas';

interface ArchivoListo {
  file: File;
  cuerpo: CuerpoImportacion;
  /** Filas de título antes del encabezado en un .xlsx: se suman a la fila que reporta el servidor. */
  desplazamiento: number;
}

/**
 * K10.11 · Póliza de la nómina externa. La nómina se calcula fuera del ERP; aquí entra su póliza por archivo (.csv o .xlsx):
 * se revisa (vista previa, sin guardar), y si ningún renglón tiene error se importa en borrador, cuadrada, con el archivo
 * original adjunto. El formato es la propuesta de VILO mientras Contabilidad entrega el suyo (supuesto a validar).
 */
export function ImportarNominaPage() {
  const tipos = useTiposDocumento();
  const [tipoOperacionId, setTipoOperacionId] = useState('');
  const [fecha, setFecha] = useState(hoyLocalISO());
  const [referencia, setReferencia] = useState('');
  const [concepto, setConcepto] = useState('');
  const [totalControl, setTotalControl] = useState('');
  const [archivo, setArchivo] = useState<ArchivoListo | null>(null);
  const [vista, setVista] = useState<VistaPreviaNomina | null>(null);
  const [polizaId, setPolizaId] = useState<string | null>(null);
  const clave = useRef(crypto.randomUUID());

  const vistaPrevia = useVistaPreviaNomina();
  const importar = useImportarNomina();
  const tiposAdjunto = useTiposAdjunto('poliza');
  const subida = useUploadFile();

  const tiposActivos = (tipos.data ?? []).filter((t) => t.activo);
  const cuerpo = () => ({
    tipoOperacionId,
    fecha,
    referencia: referencia.trim(),
    concepto: concepto.trim() || `Nómina ${referencia.trim()}`,
    totalControl: totalControl.trim() === '' ? null : Number(totalControl),
    archivo: archivo!.cuerpo,
  });
  const motivoRevisar = !archivo
    ? 'Elige el archivo de la nómina.'
    : !tipoOperacionId
      ? 'Elige el tipo de póliza de nómina.'
      : !referencia.trim()
        ? 'Indica la referencia del lote (periodo y tipo de nómina).'
        : undefined;
  const motivo = motivoImportar({ tipoOperacionId, fecha, referencia, archivo: archivo !== null, vistaValida: vista?.valida ?? null });

  function invalidarVista() {
    setVista(null);
    setPolizaId(null);
  }

  async function elegir(file: File | undefined) {
    invalidarVista();
    setArchivo(null);
    if (!file) return;
    try {
      const abierto = await abrirArchivo(file);
      if (abierto.tipo === 'csv') setArchivo({ file, cuerpo: abierto.cuerpo, desplazamiento: 0 });
      else {
        const idx = Math.max(0, abierto.hojas.findIndex((h) => detectarEncabezado(h.matriz, ALIAS_NOMINA) !== null));
        const h = prepararHoja(abierto.nombre, abierto.hojas[idx], ALIAS_NOMINA);
        setArchivo({ file, cuerpo: h.cuerpo, desplazamiento: h.desplazamiento });
      }
    } catch (e) {
      toast.error(e instanceof ArchivoInvalidoError ? e.message : 'No se pudo leer el archivo.');
    }
  }

  function revisar() {
    vistaPrevia.mutate(cuerpo(), {
      onSuccess: (v) => setVista(v),
      onError: (e) => toast.error(esApiError(e) ? (e.problem.detail ?? e.message) : 'No se pudo revisar el archivo.'),
    });
  }

  function importarPoliza() {
    importar.mutate(
      { body: cuerpo(), idempotencyKey: clave.current },
      {
        onSuccess: async (p) => {
          clave.current = crypto.randomUUID();
          setPolizaId(p.id);
          // El archivo original queda como documento soporte de la póliza (tipo de documento de adjuntos de «poliza»).
          const tipoAdjunto = tiposAdjunto.data?.[0];
          if (!tipoAdjunto || !archivo) {
            toast.warning('La póliza se creó en borrador; adjunta el archivo desde su detalle.');
            return;
          }
          try {
            await subida.upload({
              url: `/api/v1/contabilidad/polizas/${p.id}/adjuntos`,
              file: archivo.file,
              fieldName: 'archivo',
              extraFields: { tipoDocumentoId: tipoAdjunto.id },
              idempotencyKey: crypto.randomUUID(),
            });
            toast.success('Póliza de nómina creada en borrador con el archivo adjunto.');
          } catch {
            toast.warning('La póliza se creó en borrador, pero no se pudo adjuntar el archivo; adjúntalo desde su detalle.');
          }
        },
        onError: (e) => {
          clave.current = crypto.randomUUID();
          toast.error(esApiError(e) ? (e.problem.detail ?? e.message) : 'No se pudo importar la póliza.');
        },
      },
    );
  }

  function descargarPlantilla() {
    const url = URL.createObjectURL(new Blob([PLANTILLA_NOMINA], { type: 'text/csv;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = 'plantilla-poliza-nomina.csv';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  const erroresLote = useMemo(() => (vista?.errores ?? []).filter((e) => e.fila === null), [vista]);
  const erroresRenglon = useMemo(() => (vista?.errores ?? []).filter((e) => e.fila !== null), [vista]);

  return (
    <div className="flex flex-col gap-5">
      <PageHeader
        titulo={
          <>
            <FileUp className="mr-2 inline-block size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Importar póliza de nómina
          </>
        }
        descripcion="La nómina se calcula fuera del ERP: su póliza entra por archivo, se revisa renglón por renglón y nace en borrador, cuadrada y con el archivo adjunto."
      />

      <section aria-labelledby="nom-datos" className="flex flex-col gap-4 rounded-lg bg-surface-card px-5 py-4 shadow-card">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 id="nom-datos" className="text-lg font-semibold text-ink">
            Datos del lote
          </h2>
          <Button type="button" variant="outline" onClick={descargarPlantilla}>
            <Download className="size-4" strokeWidth={1.6} aria-hidden="true" />
            Plantilla CSV
          </Button>
        </div>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="nom-tipo">Tipo de póliza *</Label>
            <Select
              value={tipoOperacionId}
              onValueChange={(v) => {
                setTipoOperacionId(v);
                invalidarVista();
              }}
            >
              <SelectTrigger id="nom-tipo" className="w-full">
                <SelectValue placeholder="Elige el tipo (p. ej. nómina)" />
              </SelectTrigger>
              <SelectContent>
                {tiposActivos.map((t) => (
                  <SelectItem key={t.id} value={t.id}>
                    {t.clave} · {t.nombre}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="nom-fecha">Fecha contable *</Label>
            <Input
              id="nom-fecha"
              type="date"
              value={fecha}
              onChange={(e) => {
                setFecha(e.target.value);
                invalidarVista();
              }}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="nom-referencia">Referencia del lote *</Label>
            <Input
              id="nom-referencia"
              value={referencia}
              maxLength={60}
              placeholder="2026-Q22 ordinaria"
              onChange={(e) => {
                setReferencia(e.target.value);
                invalidarVista();
              }}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="nom-concepto">Concepto de la póliza</Label>
            <Input
              id="nom-concepto"
              value={concepto}
              maxLength={500}
              placeholder={referencia.trim() ? `Nómina ${referencia.trim()}` : 'Nómina …'}
              onChange={(e) => setConcepto(e.target.value)}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="nom-total">Total de control (cargos)</Label>
            <Input
              id="nom-total"
              type="number"
              min="0"
              step="0.01"
              value={totalControl}
              onChange={(e) => {
                setTotalControl(e.target.value);
                invalidarVista();
              }}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="nom-archivo">Archivo (.csv o .xlsx) *</Label>
            <Input id="nom-archivo" type="file" accept=".csv,.xlsx" onChange={(e) => void elegir(e.target.files?.[0])} />
          </div>
        </div>
        <p className="text-xs text-ink-muted">
          Un renglón por partida: cuenta, cargo, abono, sucursal (clave), centro de costo (clave, opcional), proyecto, concepto y
          empleado. La referencia identifica el lote: el mismo lote no se importa dos veces.
        </p>
        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" onClick={revisar} disabled={motivoRevisar !== undefined || vistaPrevia.isPending} title={motivoRevisar}>
            {vistaPrevia.isPending ? 'Revisando…' : 'Revisar archivo'}
          </Button>
          <Button type="button" onClick={importarPoliza} disabled={motivo !== undefined || importar.isPending || polizaId !== null} title={motivo}>
            {importar.isPending ? 'Importando…' : 'Importar póliza'}
          </Button>
        </div>
        {motivo && <p className="text-xs text-ink-muted">{motivo}</p>}
        {polizaId && (
          <p className="flex items-center gap-2 text-sm text-ink">
            <CircleCheck className="size-4 text-success-fg" strokeWidth={1.6} aria-hidden="true" />
            Póliza creada en borrador.{' '}
            <Link to="/contabilidad/polizas/$id" params={{ id: polizaId }} className="text-brand underline">
              Ver la póliza
            </Link>
          </p>
        )}
      </section>

      {vista && (
        <section
          aria-labelledby="nom-revision"
          className={cn('flex flex-col gap-4 rounded-lg bg-surface-card px-5 py-4 shadow-card', !vista.valida && 'ring-1 ring-danger-ring')}
        >
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 id="nom-revision" className="text-lg font-semibold text-ink">
              Revisión del archivo
            </h2>
            {vista.valida ? <Badge variant="success">Lista para importar</Badge> : <Badge variant="danger">Con errores</Badge>}
          </div>
          <dl className="grid gap-x-6 gap-y-2 text-sm sm:grid-cols-4">
            <Dato termino="Renglones" valor={String(vista.renglones)} />
            <Dato termino="Cargos" valor={monto(vista.cargos)} />
            <Dato termino="Abonos" valor={monto(vista.abonos)} />
            <Dato termino="Cuadre" valor={vista.cuadra ? 'Cuadra' : 'No cuadra'} alerta={!vista.cuadra} />
          </dl>
          {vista.columnasNoReconocidas.length > 0 && (
            <p className="text-xs text-ink-muted">Columnas que no se usan: {vista.columnasNoReconocidas.join(', ')}.</p>
          )}
          {erroresLote.length > 0 && (
            <ul role="note" className="flex flex-col gap-1.5 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg">
              {erroresLote.map((e) => (
                <li key={`${e.codigo}-${e.campo}`} className="flex items-start gap-2">
                  <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
                  <span>{e.mensaje}</span>
                </li>
              ))}
            </ul>
          )}
          {erroresRenglon.length > 0 && (
            <Table aria-label="Errores por renglón">
              <TableHeader>
                <TableRow>
                  <TableHead className="w-20">Fila</TableHead>
                  <TableHead className="w-32">Columna</TableHead>
                  <TableHead>Error</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {erroresRenglon.map((e, i) => (
                  <TableRow key={`${e.fila}-${e.codigo}-${i}`}>
                    <TableCell className="tabular-nums">{filaReal(e, archivo?.desplazamiento ?? 0)}</TableCell>
                    <TableCell className="font-mono text-xs">{e.campo}</TableCell>
                    <TableCell className="text-danger-fg">{e.mensaje}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
          <Table aria-label="Partidas del archivo">
            <TableHeader>
              <TableRow>
                <TableHead className="w-20">Fila</TableHead>
                <TableHead>Cuenta</TableHead>
                <TableHead>Sucursal</TableHead>
                <TableHead>Centro</TableHead>
                <TableHead>Concepto</TableHead>
                <TableHead className="text-right">Cargo</TableHead>
                <TableHead className="text-right">Abono</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {vista.partidas.map((p) => (
                <TableRow key={p.fila}>
                  <TableCell className="tabular-nums">{p.fila + (archivo?.desplazamiento ?? 0)}</TableCell>
                  <TableCell>
                    <span className="font-mono text-xs">{p.cuenta}</span>
                    {p.cuentaNombre && <span className="ml-2 text-ink-muted">{p.cuentaNombre}</span>}
                  </TableCell>
                  <TableCell className="font-mono text-xs">{p.sucursal ?? '—'}</TableCell>
                  <TableCell className="font-mono text-xs">{p.centro ?? '—'}</TableCell>
                  <TableCell>{p.concepto ?? '—'}</TableCell>
                  <TableCell className="text-right tabular-nums">{p.cargo ? monto(p.cargo) : ''}</TableCell>
                  <TableCell className="text-right tabular-nums">{p.abono ? monto(p.abono) : ''}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </section>
      )}
    </div>
  );
}

function Dato({ termino, valor, alerta }: { termino: string; valor: string; alerta?: boolean }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-xs text-ink-muted">{termino}</dt>
      <dd className={cn('font-medium text-ink', alerta && 'text-danger-fg')}>{valor}</dd>
    </div>
  );
}
