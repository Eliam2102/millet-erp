import { useRef, useState } from 'react';
import { FileCheck2, FolderLock, Paperclip, RefreshCw } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { formatDateTime } from '@/lib/datetime';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  useAdjuntarAcuseIva,
  useDetectarDiferenciasIva,
  useLigarAjusteDiferenciaIva,
  usePapelTrabajoIva,
  type DiferenciaFiscalIva,
  type PapelTrabajoIva,
} from '../../api/papel-trabajo-iva';
import { errorAcuse } from '../../lib/papel-trabajo-iva';
import { mensajePoliza, monto } from '../../lib/polizas';

/**
 * M-311 · Papel de trabajo del IVA del mes: la foto guardada al generar la póliza de liquidación (no cambia aunque después se
 * revierta o aplique un pago), el acuse de la declaración y las diferencias contra la foto. Permisos: leer / capturar pólizas.
 */
export function PapelTrabajoIvaSeccion({ anio, mes }: { anio: number; mes: number }) {
  const query = usePapelTrabajoIva(anio, mes);
  const puedeCapturar = useAuthStore((s) => s.permisos.includes(PermisosCanonicos.ContabilidadPolizasCapturar));
  const papel = query.data;

  return (
    <section aria-labelledby="papel-iva-titulo" className="flex min-w-0 flex-col gap-4 rounded-lg bg-surface-card px-5 py-4 shadow-card">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="papel-iva-titulo" className="flex items-center gap-2 text-lg font-semibold text-ink">
          <FolderLock className="size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
          Papel de trabajo del IVA
        </h2>
        {papel && <Badge variant={papel.diferenciasPendientes > 0 ? 'warning' : 'success'}>{papel.diferenciasPendientes > 0 ? `${papel.diferenciasPendientes} diferencia(s) pendiente(s)` : 'Sin diferencias pendientes'}</Badge>}
      </div>

      {query.isLoading && <p className="text-sm text-ink-muted">Cargando el papel de trabajo…</p>}
      {query.isError && (
        <p role="alert" className="text-sm text-danger-fg">
          {mensajePoliza(query.error)}{' '}
          <Button type="button" variant="link" className="h-auto p-0" onClick={() => void query.refetch()}>
            Reintentar
          </Button>
        </p>
      )}
      {query.isSuccess && !papel && (
        <p className="text-sm text-ink-muted">
          Este mes aún no tiene foto: se guarda al generar la póliza de liquidación del mes (botón «Generar póliza»).
        </p>
      )}
      {papel && <Contenido papel={papel} puedeCapturar={puedeCapturar} />}
    </section>
  );
}

function Contenido({ papel, puedeCapturar }: { papel: PapelTrabajoIva; puedeCapturar: boolean }) {
  const detectar = useDetectarDiferenciasIva();
  const keyFor = useBodyScopedIdempotencyKey();

  function ejecutarDetectar() {
    const cuerpo = { anio: papel.anio, mes: papel.mes };
    detectar.mutate(
      { ...cuerpo, idempotencyKey: keyFor(cuerpo) },
      {
        onSuccess: (r) => toast.success(r.diferenciasPendientes > 0 ? `Se encontraron ${r.diferenciasPendientes} diferencia(s) pendiente(s).` : 'Sin diferencias contra la foto.'),
        onError: (e) => toast.error(mensajePoliza(e)),
      },
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <dl className="grid gap-3 text-sm @3xl:grid-cols-4">
        <Dato etiqueta="Póliza de liquidación" valor={papel.folioPoliza} />
        <Dato etiqueta="Foto guardada" valor={formatDateTime(papel.creadaEn)} />
        <Dato etiqueta="IVA a cargo (foto)" valor={monto(papel.resumen.ivaACargo)} />
        <Dato etiqueta="IVA a favor (foto)" valor={monto(papel.resumen.ivaAFavor)} />
      </dl>
      <p className="text-xs text-ink-muted">La foto no se modifica: refleja el cálculo del día en que se generó la póliza.</p>

      <AcuseBloque papel={papel} puedeCapturar={puedeCapturar} />

      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-base font-semibold text-ink">Diferencias contra la foto</h3>
        {puedeCapturar && (
          <Button type="button" variant="outline" onClick={ejecutarDetectar} disabled={detectar.isPending}>
            <RefreshCw className="size-4" aria-hidden="true" />
            {detectar.isPending ? 'Comparando…' : 'Detectar diferencias'}
          </Button>
        )}
      </div>
      {papel.diferencias.length === 0 ? (
        <p className="text-sm text-ink-muted">No hay diferencias registradas.</p>
      ) : (
        <TableContainer maxHeightClassName="" role="region" aria-label="Diferencias del IVA contra la foto" tabIndex={0}>
          <Table aria-label="Diferencias del IVA contra la foto">
            <TableHeader>
              <TableRow>
                <TableHead>Documento</TableHead>
                <TableHead className="text-right">IVA en la foto</TableHead>
                <TableHead className="text-right">IVA hoy</TableHead>
                <TableHead className="text-right">Diferencia</TableHead>
                <TableHead>Causa</TableHead>
                <TableHead>Estado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {papel.diferencias.map((d) => (
                <FilaDiferencia key={d.id} d={d} puedeCapturar={puedeCapturar} />
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      )}
    </div>
  );
}

function Dato({ etiqueta, valor }: { etiqueta: string; valor: string }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-ink-muted">{etiqueta}</dt>
      <dd className="font-medium tabular-nums text-ink">{valor}</dd>
    </div>
  );
}

function AcuseBloque({ papel, puedeCapturar }: { papel: PapelTrabajoIva; puedeCapturar: boolean }) {
  const adjuntar = useAdjuntarAcuseIva();
  const keyFor = useBodyScopedIdempotencyKey();
  const input = useRef<HTMLInputElement>(null);
  const [archivo, setArchivo] = useState<File | null>(null);
  const [error, setError] = useState('');

  if (papel.acuse) {
    return (
      <div className="flex flex-wrap items-center gap-2 text-sm">
        <FileCheck2 className="size-4 text-success-fg" aria-hidden="true" />
        <span className="font-medium text-ink">Acuse adjunto: {papel.acuse.nombre}</span>
        <span className="text-ink-muted">({formatDateTime(papel.acuse.adjuntadoEn)})</span>
        <code className="max-w-full truncate rounded bg-surface-card ring-1 ring-line px-1.5 py-0.5 text-xs" title={papel.acuse.sha256}>SHA-256 {papel.acuse.sha256.slice(0, 16)}…</code>
      </div>
    );
  }
  if (!puedeCapturar) return <p className="text-sm text-ink-muted">Aún no se adjunta el acuse de la declaración.</p>;

  function subir() {
    const e = errorAcuse(archivo);
    setError(e ?? '');
    if (e || !archivo) return;
    const cuerpo = { anio: papel.anio, mes: papel.mes, nombre: archivo.name, tamano: archivo.size };
    adjuntar.mutate(
      { anio: papel.anio, mes: papel.mes, archivo, idempotencyKey: keyFor(cuerpo) },
      {
        onSuccess: () => {
          toast.success('Acuse adjunto. Ya no se puede reemplazar.');
          setArchivo(null);
          if (input.current) input.current.value = '';
        },
        onError: (err) => setError(mensajePoliza(err)),
      },
    );
  }

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="acuse-iva">Acuse de la declaración (máx. 5 MB)</Label>
        <Input id="acuse-iva" ref={input} type="file" accept=".pdf,.xml,.png,.jpg,.jpeg" onChange={(e) => setArchivo(e.target.files?.[0] ?? null)} className="w-72" />
      </div>
      <Button type="button" onClick={subir} disabled={!archivo || adjuntar.isPending}>
        <Paperclip className="size-4" aria-hidden="true" />
        {adjuntar.isPending ? 'Adjuntando…' : 'Adjuntar acuse'}
      </Button>
      {error && (
        <p role="alert" className="w-full text-sm text-danger-fg">
          {error}
        </p>
      )}
    </div>
  );
}

function FilaDiferencia({ d, puedeCapturar }: { d: DiferenciaFiscalIva; puedeCapturar: boolean }) {
  const ligar = useLigarAjusteDiferenciaIva();
  const keyFor = useBodyScopedIdempotencyKey();
  const [poliza, setPoliza] = useState('');
  const [error, setError] = useState('');

  function ejecutar() {
    setError('');
    const cuerpo = { diferenciaId: d.id, ajustePolizaId: poliza.trim() };
    ligar.mutate(
      { ...cuerpo, idempotencyKey: keyFor(cuerpo) },
      { onSuccess: () => toast.success('Diferencia cerrada con la póliza de ajuste.'), onError: (e) => setError(mensajePoliza(e)) },
    );
  }

  return (
    <TableRow>
      <TableCell>{d.folio ?? d.uuid ?? 'Sin folio'}</TableCell>
      <TableCell className="text-right tabular-nums">{monto(d.ivaFoto)}</TableCell>
      <TableCell className="text-right tabular-nums">{monto(d.ivaActual)}</TableCell>
      <TableCell className="text-right tabular-nums">{monto(d.diferencia)}</TableCell>
      <TableCell>{d.causa}</TableCell>
      <TableCell>
        {d.estado === 'Cerrada' ? (
          <Badge variant="success">Cerrada</Badge>
        ) : puedeCapturar ? (
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <Badge variant="warning">Pendiente</Badge>
              <Input aria-label={`Id de la póliza de ajuste de ${d.folio ?? 'la diferencia'}`} placeholder="Id de la póliza de ajuste" value={poliza} onChange={(e) => setPoliza(e.target.value)} className="w-56" />
              <Button type="button" size="sm" onClick={ejecutar} disabled={!poliza.trim() || ligar.isPending}>
                Ligar ajuste
              </Button>
            </div>
            {error && (
              <p role="alert" className="text-xs text-danger-fg">
                {error}
              </p>
            )}
          </div>
        ) : (
          <Badge variant="warning">Pendiente</Badge>
        )}
      </TableCell>
    </TableRow>
  );
}
