import { useId, useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { PaginacionTabla } from '@/components/erp';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { apiRequest } from '@/lib/api';
import { apiFetch } from '@/lib/auth/api-client';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { datosMaestrosKeys } from '../api/keys';

type Catalogo = 'clientes' | 'articulos' | 'proveedores' | 'productos-aw-fiscal' | 'centros-costo-dim1' | 'centros-costo-dim2' | 'centros-costo-dim3';
type Estado = 'Nuevo' | 'Actualiza' | 'SinCambio' | 'Error';
export interface RenglonCarga {
  renglon: number; estado: Estado; valores: Record<string, string>;
  cambios: { campo: string; antes: string | null; despues: string | null }[];
  errores: { renglon: number; columna: string; codigo: string; mensaje: string }[];
}
export interface ResultadoCargaMaestros {
  huella: string; renglones: RenglonCarga[]; nuevos: number; actualiza: number; sinCambio: number; errores: number; confirmado: boolean;
  resumen?: { seccion: string; clave: string; total: number }[] | null;
}
const permisos: Record<Catalogo, string> = {
  clientes: PermisosCanonicos.DatosMaestrosClientesCargar, articulos: PermisosCanonicos.DatosMaestrosArticulosCargar,
  proveedores: PermisosCanonicos.DatosMaestrosProveedoresCargar, 'productos-aw-fiscal': PermisosCanonicos.DatosMaestrosProductosAwFiscalCargar,
  'centros-costo-dim1': PermisosCanonicos.CentrosCostoCatalogoCargar, 'centros-costo-dim2': PermisosCanonicos.CentrosCostoCatalogoCargar,
  'centros-costo-dim3': PermisosCanonicos.CentrosCostoCatalogoCargar,
};
const etiquetas: Record<Estado, string> = { Nuevo: 'Nuevo', Actualiza: 'Se actualiza', SinCambio: 'Sin cambios', Error: 'Con error' };
const tonos = { Nuevo: 'success', Actualiza: 'info', SinCambio: 'neutral', Error: 'danger' } as const;
// Cuerpo multipart con frontera fija: un reintento con la misma llave de idempotencia envía los mismos bytes.
function multipartFijo(archivo: File, huella: string) {
  // En minúsculas: el navegador baja a minúsculas el type del Blob (y con él el boundary del encabezado); la huella
  // viene en hex mayúsculas y el cuerpo no coincidía con el encabezado («No se pudo leer el formulario»).
  const frontera = `----cargamaestros${huella.slice(0, 32).toLowerCase()}`; // RFC 2046: máximo 70 caracteres.
  const nombre = archivo.name.replace(/["\r\n]/g, '_');
  return new Blob([
    `--${frontera}\r\nContent-Disposition: form-data; name="huella"\r\n\r\n${huella}\r\n`,
    `--${frontera}\r\nContent-Disposition: form-data; name="archivo"; filename="${nombre}"\r\nContent-Type: ${archivo.type || 'application/octet-stream'}\r\n\r\n`,
    archivo, `\r\n--${frontera}--\r\n`,
  ], { type: `multipart/form-data; boundary=${frontera}` });
}

function descargar(blob: Blob, nombre: string) {
  const url = URL.createObjectURL(blob); const enlace = document.createElement('a');
  enlace.href = url; enlace.download = nombre; enlace.click(); URL.revokeObjectURL(url);
}
async function descargarErroresCarga(renglones: RenglonCarga[]) {
  const { Workbook } = await import('exceljs');
  const libro = new Workbook(); const hoja = libro.addWorksheet('Errores');
  const errores = renglones.filter(r => r.estado === 'Error');
  const columnas = Object.keys(errores[0]?.valores ?? {});
  hoja.addRow([...columnas, 'Renglón original', 'Error']);
  errores.forEach(r => hoja.addRow([...columnas.map(c => r.valores[c]), r.renglon, r.errores.map(e => `${e.columna}: ${e.mensaje}`).join('; ')]));
  descargar(new Blob([await libro.xlsx.writeBuffer()], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }), 'errores-carga.xlsx');
}

export function CargaMaestrosDialog({ catalogo, etiqueta = 'Cargar desde Excel' }: { catalogo: Catalogo; etiqueta?: string }) {
  const permitido = useHasPermission(permisos[catalogo]);
  const [abierto, setAbierto] = useState(false);
  const [ocupado, setOcupado] = useState(false);
  if (!permitido) return null;
  return <><Button size="sm" variant="outline" onClick={() => setAbierto(true)}>{etiqueta}</Button>
    <Dialog open={abierto} onOpenChange={valor => { if (!ocupado) setAbierto(valor); }}>
      {abierto && <ContenidoCarga catalogo={catalogo} ocupado={ocupado} setOcupado={setOcupado} cerrar={() => setAbierto(false)} />}
    </Dialog></>;
}
function ContenidoCarga({ catalogo, cerrar, ocupado, setOcupado }: { catalogo: Catalogo; cerrar: () => void; ocupado: boolean; setOcupado: (valor: boolean) => void }) {
  const id = useId(); const qc = useQueryClient();
  const [archivo, setArchivo] = useState<File>(); const [resultado, setResultado] = useState<ResultadoCargaMaestros>();
  const [error, setError] = useState('');
  const [filtro, setFiltro] = useState('Todos'); const [pagina, setPagina] = useState(0);
  const llave = useRef(crypto.randomUUID());
  const cuerpoConfirmar = useRef<Blob | undefined>(undefined);
  const base = `/api/v1/datos-maestros/cargas/${catalogo}`;
  const paso = resultado?.confirmado ? 3 : resultado ? 2 : 1;
  const filas = resultado?.renglones.filter(r => filtro === 'Todos' || r.estado === filtro) ?? [];
  async function ejecutar(confirmar: boolean) {
    if (!archivo) return;
    setOcupado(true); setError('');
    try {
      const form = new FormData(); form.set('archivo', archivo);
      if (confirmar) form.set('huella', resultado!.huella);
      const respuesta = await apiRequest<ResultadoCargaMaestros>(`${base}/${confirmar ? 'confirmar' : 'previa'}`, {
        method: 'POST', body: confirmar ? (cuerpoConfirmar.current ??= multipartFijo(archivo, resultado!.huella)) : form, idempotencyKey: confirmar ? llave.current : undefined,
      });
      setResultado(respuesta.data); setPagina(0);
      if (confirmar) {
        await qc.invalidateQueries({ queryKey: datosMaestrosKeys.all });
        await qc.invalidateQueries({ queryKey: ['centros-costo'] });
      }
    } catch (e) { setError(e instanceof Error ? e.message : 'No se pudo procesar el archivo. Revise el archivo y vuelva a generar la vista previa.'); }
    finally { setOcupado(false); }
  }
  return <DialogContent size="lg" onInteractOutside={e => { if (ocupado) e.preventDefault(); }} onEscapeKeyDown={e => { if (ocupado) e.preventDefault(); }}>
    <DialogHeader><DialogTitle>Carga de datos maestros</DialogTitle>
      <DialogDescription>Paso {paso} de 3 · {['', 'Elegir archivo', 'Revisar cambios', 'Resultado de la carga'][paso]}. Las celdas vacías conservan el valor existente.</DialogDescription>
    </DialogHeader>
    <DialogBody className="space-y-5">
      {paso === 1 && <>
        <div className="flex gap-2">{(['xlsx', 'csv'] as const).map(formato => <Button key={formato} variant="outline" disabled={ocupado} onClick={async () => {
          try { const r = await apiFetch(`${base}/plantilla.${formato}`); if (!r.ok) throw new Error('No se pudo descargar la plantilla. Revise su acceso.'); descargar(await r.blob(), `plantilla.${formato}`); }
          catch (e) { setError(e instanceof Error ? e.message : 'No se pudo descargar la plantilla.'); }
        }}>Descargar plantilla {formato.toUpperCase()}</Button>)}</div>
        <FormGrid><FormCampo span={12}><Label htmlFor={id}>Archivo Excel o CSV *</Label>
        <Input id={id} type="file" accept=".xlsx,.csv" disabled={ocupado} onChange={e => {
          setError(''); const f = e.target.files?.[0];
          if (f && (f.size > 10 * 1024 * 1024 || !/\.(xlsx|csv)$/i.test(f.name))) { setArchivo(undefined); setError('Seleccione un archivo .xlsx o .csv de hasta 10 MB.'); return; }
          setArchivo(f); llave.current = crypto.randomUUID(); cuerpoConfirmar.current = undefined;
        }} /></FormCampo></FormGrid>
        <p className="text-sm text-ink-muted">Máximo 10 MB y 20,000 renglones. La plantilla Excel incluye las instrucciones por columna.</p>
      </>}
      {resultado && <>
        <div role="status" className="flex flex-wrap gap-3"><Badge variant="success">{resultado.nuevos} nuevos</Badge><Badge variant="info">{resultado.actualiza} actualizaciones</Badge><Badge variant="neutral">{resultado.sinCambio} sin cambios</Badge><Badge variant="danger">{resultado.errores} con error</Badge></div>
        {resultado.resumen?.length ? <dl className="text-sm" aria-label="Resumen de la carga">{[...new Set(resultado.resumen.map((c) => c.seccion))].map((sec) => <div key={sec}><dt className="font-medium">{sec}</dt><dd>{resultado.resumen!.filter((c) => c.seccion === sec).map((c) => `${c.clave}: ${c.total}`).join(' · ')}</dd></div>)}</dl> : null}
        {paso === 2 && resultado.nuevos + resultado.actualiza === 0 && <p>No hay renglones válidos con cambios para confirmar. Corrija los errores o elija otro archivo.</p>}
        {paso === 2 && <p>Al confirmar se aplicarán los renglones válidos. Los renglones con error se omitirán.</p>}
        {paso === 3 && <p>Se aplicaron {resultado.nuevos + resultado.actualiza} renglones. Puede descargar los errores, corregirlos y cargar otro archivo.</p>}
        <Select value={filtro} onValueChange={v => { setFiltro(v); setPagina(0); }}><SelectTrigger aria-label="Filtrar por estado"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="Todos">Todos los estados</SelectItem>{Object.entries(etiquetas).map(([k, v]) => <SelectItem key={k} value={k}>{v}</SelectItem>)}</SelectContent></Select>
        <Table><TableHeader><TableRow><TableHead>Renglón</TableHead><TableHead>Estado</TableHead><TableHead>Registro</TableHead><TableHead>Cambios o errores</TableHead></TableRow></TableHeader>
          <TableBody>{filas.slice(pagina * 10, pagina * 10 + 10).map(r => <TableRow key={r.renglon}><TableCell>{r.renglon}</TableCell><TableCell><Badge variant={tonos[r.estado]}>{etiquetas[r.estado]}</Badge></TableCell>
            <TableCell>{Object.values(r.valores).filter(Boolean).slice(0, 3).join(' · ')}</TableCell><TableCell>{r.errores.length ? r.errores.map(e => <p key={`${e.columna}-${e.codigo}`}>{e.columna}: {e.mensaje}</p>) : r.cambios.map(c => <p key={c.campo}>{c.campo}: {c.antes ?? 'Sin valor'} → {c.despues ?? 'Sin valor'}</p>)}</TableCell></TableRow>)}</TableBody></Table>
        <PaginacionTabla total={filas.length} offset={pagina * 10} limit={10} tamanos={[]} onChange={offset => setPagina(offset / 10)} />
        {resultado.errores > 0 && <Button variant="outline" onClick={() => { void descargarErroresCarga(resultado.renglones).catch(() => setError('No se pudo descargar el archivo de errores. Vuelva a intentarlo.')); }}>Descargar errores</Button>}
      </>}
      {error && <p role="alert" className="text-danger-fg">{error}</p>}
    </DialogBody>
    <DialogFooter>
      <Button variant="ghost" disabled={ocupado} onClick={cerrar}>Cerrar</Button>
      {paso === 2 && <Button variant="outline" disabled={ocupado} onClick={() => { setResultado(undefined); setFiltro('Todos'); llave.current = crypto.randomUUID(); cuerpoConfirmar.current = undefined; }}>Elegir otro archivo</Button>}
      {paso < 3 && <Button disabled={ocupado || !archivo || (paso === 2 && resultado!.nuevos + resultado!.actualiza === 0)} onClick={() => { void ejecutar(paso === 2); }}>{ocupado ? 'Procesando…' : paso === 1 ? 'Ver vista previa' : 'Confirmar carga'}</Button>}
    </DialogFooter>
  </DialogContent>;
}
