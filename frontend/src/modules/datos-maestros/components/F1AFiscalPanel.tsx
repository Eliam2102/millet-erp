import { PaginacionTabla, BarraFiltros } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { PageHeader } from '@/components/erp/PageHeader';
import { estadoFiscalTexto, faltantesFicha, cambiosFiscalesTexto } from './ficha-fiscal';
import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogTrigger,
  AlertDialogContent,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogCancel,
  AlertDialogAction,
} from '@/components/ui/alert-dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type { ProductoAwDetalle } from '../api/types';
import {
  FISCAL,
  useF1AQuery,
  useF1AMutation,
  mensajeF1A,
  fichaFiscalSchema,
  type FichaFiscal,
  type ReglaFiscal,
  type CambioFiscal,
  type ImportacionFiscal,
} from '../api/f1a';

function Historial({ id, tipo }: { id: string; tipo: 'Producto' | 'Regla' }) {
  const q = useF1AQuery<CambioFiscal[]>(`${FISCAL}/${tipo}/${id}/historial`);
  return (
    <details className="space-y-2">
      <summary className="cursor-pointer text-brand">Historial fiscal</summary>
      {q.isError && <p role="alert">No se pudo cargar el historial.</p>}
      {q.data?.map((c) => (
        <div key={c.id} className="border-b border-line-row py-2">
          <p>
            {c.accion} · {new Date(c.createdAt).toLocaleString('es-MX')} ·{' '}
            {c.usuarioId ?? 'Sincronización'}
          </p>
          <ul className="space-y-1 text-xs text-ink-muted">
            {cambiosFiscalesTexto(c.antesJson, c.despuesJson).map((texto) => (
              <li key={texto}>{texto}</li>
            ))}
          </ul>
        </div>
      ))}
    </details>
  );
}
export function F1AProductoFiscal({ producto }: { producto: ProductoAwDetalle }) {
  const accion = useF1AMutation();
  const faltantes = faltantesFicha(producto);
  function ejecutar(nombre: 'Confirmar' | 'VolverRegla') {
    accion.mutate(
      {
        path: `${FISCAL}/${producto.id}/acciones`,
        body: { id: producto.id, accion: nombre, version: producto.version },
      },
      {
        onSuccess: () => toast.success('Ficha fiscal actualizada'),
        onError: (e) => toast.error(mensajeF1A(e)),
      },
    );
  }
  return (
    <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat text-sm text-ink">
      <h2 className="text-lg font-semibold">Revisión fiscal del producto</h2>
      <div className="flex flex-wrap gap-2">
        <Badge variant={faltantes.length ? 'warning' : 'info'}>
          {estadoFiscalTexto(producto.estadoFiscal)}
        </Badge>
        <span>
          {producto.origenFiscal === 1 || producto.origenFiscal === 'ReglaFamilia'
            ? 'Regla de familia'
            : producto.origenFiscal === 2 || producto.origenFiscal === 'ExcepcionProducto'
              ? 'Excepción del producto'
              : 'Sin regla aplicada'}
        </span>
      </div>
      {faltantes.length > 0 && (
        <p role="note" className="rounded-md bg-warning-note-bg p-3 text-warning-note-fg">
          Falta {faltantes.join(', ')}. Completa los datos fiscales para poder timbrar este
          producto.
        </p>
      )}
      {producto.confirmadoEn && (
        <p className="text-xs text-ink-muted">
          Confirmada el {new Date(producto.confirmadoEn).toLocaleString('es-MX')} ·{' '}
          {producto.confirmadoPor}
        </p>
      )}
      <div className="flex gap-2">
        <Button
          disabled={accion.isPending || faltantes.length > 0}
          title={faltantes.length ? 'Completa los datos fiscales antes de confirmar' : undefined}
          onClick={() => ejecutar('Confirmar')}
        >
          Confirmar ficha fiscal
        </Button>
        <AlertDialog>
          <AlertDialogTrigger asChild>
            <Button variant="secondary" disabled={accion.isPending}>
              Volver a la regla
            </Button>
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Volver a la regla de familia</AlertDialogTitle>
              <AlertDialogDescription>
                Se reemplazarán los datos fiscales de este producto con la regla activa de su
                familia.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>Cancelar</AlertDialogCancel>
              <AlertDialogAction onClick={() => ejecutar('VolverRegla')}>
                Aplicar regla
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </div>
      <p className="text-xs text-ink-muted">
        Para capturar una excepción, edita y guarda los datos fiscales de este producto. Después
        confirma la ficha.
      </p>
      <Historial id={producto.id} tipo="Producto" />
    </section>
  );
}
const vacia: FichaFiscal = {
  claveProdServSat: '',
  claveUnidadSat: '',
  objetoImp: '',
  tasaIvaTraslado: 0,
  tasaRetencionIva: null,
  tasaRetencionIsr: null,
};
export function F1AFiscalPanel() {
  const reglas = useF1AQuery<ReglaFiscal[]>(`${FISCAL}/reglas`);
  const guardar = useF1AMutation();
  const importar = useF1AMutation<ImportacionFiscal>();
  const [editar, setEditar] = useState<ReglaFiscal | null>(null);
  const [campo, setCampo] = useState('Tipo');
  const [valor, setValor] = useState('');
  const [descripcion, setDescripcion] = useState('');
  const [ficha, setFicha] = useState<FichaFiscal>(vacia);
  const [error, setError] = useState('');
  const [pagina, setPagina] = useState(0);
  const [historial, setHistorial] = useState<string | null>(null);
  function reset() {
    setEditar(null);
    setCampo('Tipo');
    setValor('');
    setDescripcion('');
    setFicha(vacia);
    setError('');
  }
  return (
    <div className="space-y-5 text-sm text-ink">
      <PageHeader titulo={<>Reglas fiscales de productos A+W</>} />
      <p className="text-ink-muted">
        Mantén la ficha por Tipo, Grupo o Wgr. Las excepciones del producto se conservan al cambiar
        una regla.
      </p>
      <section className="rounded-lg bg-surface-card p-4 shadow-card-flat">
        {reglas.isError && (
          <p role="alert" className="text-danger-fg">
            No se pudieron cargar las reglas.{' '}
            <Button variant="secondary" onClick={() => void reglas.refetch()}>
              Reintentar
            </Button>
          </p>
        )}
        <ListadoModulo filtros={<BarraFiltros total={`${reglas.data?.length ?? 0} registros`} />}>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Familia</TableHead>
                <TableHead>Clave SAT</TableHead>
                <TableHead>Unidad</TableHead>
                <TableHead>IVA</TableHead>
                <TableHead>Acciones</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {reglas.data?.slice(pagina * 10, pagina * 10 + 10).map((r) => (
                <TableRow key={r.id}>
                  <TableCell>
                    {r.campo}: {r.valor}
                    <Badge variant={r.activa ? 'success' : 'neutral'}>
                      {r.activa ? 'Activa' : 'Inactiva'}
                    </Badge>
                  </TableCell>
                  <TableCell className="font-mono">{r.ficha.claveProdServSat}</TableCell>
                  <TableCell>{r.ficha.claveUnidadSat}</TableCell>
                  <TableCell className="tabular-nums">{r.ficha.tasaIvaTraslado * 100} %</TableCell>
                  <TableCell>
                    <Button
                      size="sm"
                      variant="secondary"
                      onClick={() => {
                        setEditar(r);
                        setCampo(r.campo);
                        setValor(r.valor);
                        setDescripcion(r.descripcion);
                        setFicha(r.ficha);
                      }}
                    >
                      Editar
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => setHistorial(r.id)}>
                      Historial
                    </Button>
                    <Button
                      size="sm"
                      variant="secondary"
                      disabled={guardar.isPending}
                      onClick={() =>
                        guardar.mutate(
                          {
                            path: `${FISCAL}/reglas/${r.id}`,
                            method: 'PUT',
                            body: { ...r, activa: !r.activa },
                          },
                          { onError: (e) => toast.error(mensajeF1A(e)) },
                        )
                      }
                    >
                      {r.activa ? 'Desactivar' : 'Reactivar'}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ListadoModulo>
        <PaginacionTabla
          total={reglas.data?.length ?? 0}
          offset={pagina * 10}
          limit={10}
          tamanos={[]}
          onChange={(offset) => setPagina(offset / 10)}
        />
        {historial && <Historial id={historial} tipo="Regla" />}
        <form
          className="mt-4   gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            const validacion = fichaFiscalSchema.safeParse(ficha);
            if (!validacion.success) {
              setError(validacion.error.issues.map((i) => i.message).join('. '));
              return;
            }
            guardar.mutate(
              {
                path: editar ? `${FISCAL}/reglas/${editar.id}` : `${FISCAL}/reglas`,
                method: editar ? 'PUT' : 'POST',
                body: {
                  id: editar?.id,
                  campo,
                  valor,
                  descripcion,
                  ficha,
                  activa: editar?.activa ?? true,
                  version: editar?.version,
                },
              },
              { onSuccess: reset, onError: (e) => setError(mensajeF1A(e)) },
            );
          }}
        >
          <FormSeccion titulo="Datos del registro">
            <FormCampo span={12}>
              <h2 className="col-span-2 text-lg font-semibold">
                {editar ? 'Editar regla fiscal' : 'Nueva regla fiscal'}
              </h2>
            </FormCampo>
            <FormCampo span={12}>
              <div>
                <Label htmlFor="regla-campo">Campo de A+W</Label>
                <Select value={campo} onValueChange={setCampo} disabled={editar !== null}>
                  <SelectTrigger id="regla-campo">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {['Tipo', 'Grupo', 'Wgr'].map((c) => (
                      <SelectItem key={c} value={c}>
                        {c}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </FormCampo>
            <FormCampo span={12}>
              <div>
                <Label htmlFor="regla-valor">Familia o valor *</Label>
                <Input
                  id="regla-valor"
                  required
                  maxLength={100}
                  value={valor}
                  disabled={editar !== null}
                  onChange={(e) => setValor(e.target.value)}
                />
              </div>
            </FormCampo>
            <FormCampo span={12}>
              <div className="col-span-2">
                <Label htmlFor="regla-descripcion">Descripción y fuente</Label>
                <Input
                  id="regla-descripcion"
                  value={descripcion}
                  maxLength={500}
                  onChange={(e) => setDescripcion(e.target.value)}
                />
              </div>
            </FormCampo>
            <FormCampo span={12}>
              {(['claveProdServSat', 'claveUnidadSat', 'objetoImp'] as const).map((k, i) => (
                <div key={k}>
                  <Label htmlFor={`regla-${k}`}>
                    {['Clave de producto SAT', 'Clave de unidad SAT', 'Objeto de impuesto'][i]}*
                  </Label>
                  <Input
                    id={`regla-${k}`}
                    required
                    value={ficha[k]}
                    onChange={(e) => setFicha((f) => ({ ...f, [k]: e.target.value.toUpperCase() }))}
                  />
                </div>
              ))}
            </FormCampo>
            <FormCampo span={12}>
              {(['tasaIvaTraslado', 'tasaRetencionIva', 'tasaRetencionIsr'] as const).map(
                (k, i) => (
                  <div key={k}>
                    <Label htmlFor={`regla-${k}`}>
                      {
                        [
                          'Tasa de IVA',
                          'Retención de IVA (opcional)',
                          'Retención de ISR (opcional)',
                        ][i]
                      }
                    </Label>
                    <Input
                      id={`regla-${k}`}
                      type="number"
                      step="0.000001"
                      min="0"
                      max="1"
                      required={i === 0}
                      value={ficha[k] ?? ''}
                      onChange={(e) =>
                        setFicha((f) => ({
                          ...f,
                          [k]: e.target.value === '' ? null : Number(e.target.value),
                        }))
                      }
                    />
                  </div>
                ),
              )}
            </FormCampo>
            <FormCampo span={12}>
              <p className="col-span-2 text-xs text-ink-muted">
                Tasas como fracción: 0.16 representa 16 %. Usa datos DEMO identificados hasta contar
                con la fuente validada.
              </p>
            </FormCampo>
            <FormCampo span={12}>
              {error && (
                <p role="alert" className="col-span-2 text-danger-fg">
                  {error}
                </p>
              )}
            </FormCampo>
            <FormCampo span={12}>
              <div className="col-span-2 flex gap-2">
                <Button disabled={guardar.isPending}>Guardar regla</Button>
                <Button type="button" variant="ghost" onClick={reset}>
                  Cancelar edición
                </Button>
              </div>
            </FormCampo>
          </FormSeccion>
        </form>
      </section>
      <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
        <h2 className="text-lg font-semibold">Importar reglas y excepciones CSV</h2>
        <p className="text-xs text-ink-muted">
          Encabezado:
          clase,campo,valor,descripcion,claveProdServSat,claveUnidadSat,objetoImp,tasaIvaTraslado,tasaRetencionIva,tasaRetencionIsr.
          Clase: Regla o Excepcion; para una excepción, valor es la referencia A+W del producto.
        </p>
        <Label htmlFor="fiscal-csv">Archivo CSV</Label>
        <Input
          id="fiscal-csv"
          type="file"
          accept=".csv,text/csv"
          disabled={importar.isPending}
          onChange={async (e) => {
            const archivo = e.target.files?.[0];
            if (!archivo) return;
            if (archivo.size > 2_000_000) {
              toast.error('El CSV no puede exceder 2 MB.');
              return;
            }
            const csv = await archivo.text();
            importar.mutate(
              { path: `${FISCAL}/importar`, body: { csv } },
              { onError: (e) => toast.error(mensajeF1A(e)) },
            );
          }}
        />
        {importar.data && (
          <div role="status">
            <p>
              {importar.data.aplicadas} filas aplicadas · {importar.data.sinCambios} sin cambios.
            </p>
            {importar.data.errores.map((e) => (
              <p key={e.fila} className="text-danger-fg">
                Fila {e.fila}: {e.mensaje}
              </p>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}
