import { useState } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { Sheet, SheetContent, SheetHeader, SheetTitle, SheetBody } from '@/components/ui/sheet';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { formatDate, formatDateTime } from '@/lib/datetime';
import { useConservacion, resultadosConteo, type Conteo, type Pagina } from '../api/conservacion';
import { useGuardarActivo } from '../api/hooks';
export function ConteosPage() {
  const [sucursal, setSucursal] = useState<string | null>(null);
  const [offset, setOffset] = useState(0);
  const [seleccion, setSeleccion] = useState('');
  const [nuevo, setNuevo] = useState(false);
  const [dirtyNuevo, setDirtyNuevo] = useState(false);
  const [sucursalNueva, setSucursalNueva] = useState<string | null>(null);
  const [numero, setNumero] = useState('');
  const [resultado, setResultado] = useState(1);
  const [pagina, setPagina] = useState(0);
  const [diferencia, setDiferencia] = useState('');
  const [filtro, setFiltro] = useState('');
  const gestionar = useHasPermission(P.ActivosFijosConteosGestionar);
  const autorizar = useHasPermission(P.ActivosFijosConteosAutorizar);
  const usuario = useAuthStore((s) => s.user);
  const lista = useConservacion<Pagina<Conteo>>(
    `conteos?offset=${offset}${sucursal ? `&sucursalId=${sucursal}` : ''}`,
  );
  const detalle = useConservacion<Conteo>(`conteos/${seleccion}`, !!seleccion);
  const guardar = useGuardarActivo();
  const c = detalle.data;
  const partidas = c?.partidas.filter((p) => filtro === '' || p.resultado === Number(filtro)) ?? [];
  function accion(nombre: string, body: unknown) {
    if (c)
      guardar.mutate(
        { ruta: `conteos/${c.id}/${nombre}`, version: c.version, body },
        {
          onSuccess: () => {
            setNumero('');
            setDiferencia('');
          },
        },
      );
  }
  return (
    <main className="flex h-full min-h-0 flex-col gap-5 text-sm">
      <PageHeader
        titulo="Inventario físico"
        descripcion="Abra un conteo por sucursal, capture lo encontrado y autorice sus diferencias."
        acciones={
          gestionar && (
            <Button
              onClick={() => {
                setSucursalNueva(sucursal);
                setDirtyNuevo(false);
                setNuevo(true);
              }}
            >
              Abrir conteo
            </Button>
          )
        }
      />
      {guardar.error && (
        <p role="alert" className="text-danger-fg">
          {guardar.error.message}
        </p>
      )}
      {c ? (
        <section className="space-y-4 overflow-auto rounded-lg bg-surface-card p-4 shadow-card-flat">
          <div className="flex flex-wrap items-center gap-3">
            <h2 className="text-lg font-semibold">Conteo del {formatDate(c.createdAt)}</h2>
            <Badge variant={c.cerradoEn ? 'success' : 'warning'}>
              {c.cerradoEn ? 'Cerrado' : 'En captura'}
            </Badge>
            <Button variant="outline" onClick={() => setSeleccion('')}>
              Volver a conteos
            </Button>
            {c.cerradoEn && (
              <Button variant="outline" onClick={() => window.print()}>
                Imprimir reporte
              </Button>
            )}
          </div>
          <SucursalSelector value={c.sucursalId} onChange={() => {}} disabled />
          <p>
            Ubicación: {c.ubicacion ?? 'Todas las ubicaciones'}
            {c.cerradoEn ? ` · Cierre: ${formatDateTime(c.cerradoEn)}` : ''}
          </p>
          <div className="flex flex-wrap gap-3">
            {resultadosConteo.map((r, i) => (
              <Button
                key={r}
                variant="outline"
                onClick={() => {
                  setFiltro(String(i));
                  setPagina(0);
                }}
              >
                {r}: {c.partidas.filter((p) => p.resultado === i).length}
              </Button>
            ))}
            <Button
              variant="ghost"
              onClick={() => {
                setFiltro('');
                setPagina(0);
              }}
            >
              Ver todos
            </Button>
          </div>
          {!c.cerradoEn && gestionar && (
            <form
              className="space-y-3"
              onSubmit={(e) => {
                e.preventDefault();
                const f = new FormData(e.currentTarget);
                accion('capturar', {
                  captura: {
                    numero: numero || null,
                    resultado,
                    ubicacionReal: f.get('ubicacionReal') || null,
                    evidencia: f.get('evidencia'),
                  },
                });
              }}
            >
              <FormGrid>
                <FormCampo span={6}>
                  <label className="text-xs">
                    Escanear o escribir número{resultado !== 4 ? ' *' : ''}
                    <Input
                      value={numero}
                      onChange={(e) => setNumero(e.target.value)}
                      maxLength={60}
                      required={resultado !== 4}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') {
                          e.preventDefault();
                          e.currentTarget.form
                            ?.querySelector<HTMLInputElement>('[name="evidencia"]')
                            ?.focus();
                        }
                      }}
                    />
                  </label>
                </FormCampo>
                <FormCampo span={6}>
                  <label className="text-xs">
                    Resultado *
                    <select
                      value={resultado}
                      onChange={(e) => setResultado(Number(e.target.value))}
                      className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
                    >
                      {resultadosConteo.slice(1).map((r, i) => (
                        <option key={r} value={i + 1}>
                          {r}
                        </option>
                      ))}
                    </select>
                  </label>
                </FormCampo>
                <FormCampo span={6}>
                  <label className="text-xs">
                    Ubicación real{resultado === 3 ? ' *' : ''}
                    <Input name="ubicacionReal" maxLength={500} required={resultado === 3} />
                  </label>
                </FormCampo>
                <FormCampo span={6}>
                  <label className="text-xs">
                    Evidencia u observación *<Input name="evidencia" required maxLength={2000} />
                  </label>
                </FormCampo>
              </FormGrid>
              <Button type="submit" disabled={guardar.isPending} title="Guardar la captura actual">
                Registrar resultado
              </Button>
              <Button
                type="button"
                variant="outline"
                disabled={guardar.isPending || c.partidas.some((p) => p.resultado === 0)}
                title="Todos los activos esperados deben tener un resultado; el cierre impide modificar la captura"
                onClick={() => {
                  if (
                    window.confirm(
                      'El conteo cerrado no se puede modificar. ¿Cerrar con los resultados capturados?',
                    )
                  )
                    accion('cerrar', {});
                }}
              >
                Cerrar conteo
              </Button>
            </form>
          )}
          <Table className="print:hidden">
            <TableHeader>
              <TableRow>
                <TableHead>Activo</TableHead>
                <TableHead>Resultado</TableHead>
                <TableHead>Ubicaciones</TableHead>
                <TableHead>Evidencia</TableHead>
                <TableHead>Autorización y propuesta</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {partidas.slice(pagina * 10, pagina * 10 + 10).map((p) => (
                <TableRow key={p.id}>
                  <TableCell>
                    <span className="font-mono">{p.numero ?? 'Sin número'}</span>
                    <p>{p.descripcionEsperada}</p>
                  </TableCell>
                  <TableCell>{resultadosConteo[p.resultado]}</TableCell>
                  <TableCell>
                    Esperada: {p.ubicacionEsperada ?? 'Sin ubicación'}
                    <p>Real: {p.ubicacionReal ?? 'Sin registrar'}</p>
                  </TableCell>
                  <TableCell>{p.evidencia ?? 'Sin capturar'}</TableCell>
                  <TableCell>
                    {p.propuesta ? (
                      <>
                        <Badge variant="info">{p.propuesta}</Badge>
                        <p>{p.evidenciaAutorizacion}</p>
                        <p className="text-xs">Propuesta pendiente de ejecución</p>
                      </>
                    ) : c.cerradoEn && p.resultado >= 2 && autorizar ? (
                      <Button
                        variant="outline"
                        disabled={p.capturadoPor === usuario?.id}
                        title={
                          p.capturadoPor === usuario?.id
                            ? 'Otra persona debe autorizar lo que capturaste'
                            : 'Autorizar propuesta con evidencia'
                        }
                        onClick={() => setDiferencia(p.id)}
                      >
                        Autorizar diferencia
                      </Button>
                    ) : (
                      '—'
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <div className="hidden print:block">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Activo</TableHead>
                  <TableHead>Resultado</TableHead>
                  <TableHead>Ubicación esperada / real</TableHead>
                  <TableHead>Evidencia</TableHead>
                  <TableHead>Propuesta</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {c.partidas.map((p) => (
                  <TableRow key={p.id}>
                    <TableCell>
                      {p.numero ?? 'Sin número'} · {p.descripcionEsperada}
                    </TableCell>
                    <TableCell>{resultadosConteo[p.resultado]}</TableCell>
                    <TableCell>
                      {p.ubicacionEsperada ?? '—'} / {p.ubicacionReal ?? '—'}
                    </TableCell>
                    <TableCell>{p.evidencia}</TableCell>
                    <TableCell>
                      {p.propuesta ?? 'Sin autorizar'} · {p.evidenciaAutorizacion}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <div className="flex gap-2" data-print="hidden">
            <Button
              variant="outline"
              disabled={pagina === 0}
              title="Página anterior"
              onClick={() => setPagina(pagina - 1)}
            >
              Anterior
            </Button>
            <Button
              variant="outline"
              disabled={(pagina + 1) * 10 >= partidas.length}
              title="Página siguiente"
              onClick={() => setPagina(pagina + 1)}
            >
              Siguiente
            </Button>
          </div>
          {diferencia && (
            <form
              className="space-y-3"
              onSubmit={(e) => {
                e.preventDefault();
                const f = new FormData(e.currentTarget);
                accion('autorizar', { partidaId: diferencia, evidencia: f.get('autorizacion') });
              }}
            >
              <label className="text-xs">
                Evidencia de autorización *<Input name="autorizacion" required maxLength={2000} />
              </label>
              <p>
                La autorización deja una propuesta de traslado, baja o alta/investigación. Su
                ejecución requiere el flujo correspondiente.
              </p>
              <Button type="submit" disabled={guardar.isPending} title="Registrar autorización">
                Autorizar propuesta
              </Button>
              <Button type="button" variant="ghost" onClick={() => setDiferencia('')}>
                Cancelar
              </Button>
            </form>
          )}
        </section>
      ) : seleccion ? (
        <p role={detalle.error ? 'alert' : undefined}>
          {detalle.error?.message ?? 'Cargando conteo…'}
          <Button variant="ghost" onClick={() => setSeleccion('')}>
            Volver
          </Button>
        </p>
      ) : (
        <ListadoModulo
          filtros={
            <div className="p-4">
              <SucursalSelector
                value={sucursal}
                onChange={(id) => {
                  setSucursal(id);
                  setOffset(0);
                }}
              />
            </div>
          }
          pie={
            <div className="flex items-center justify-between p-3">
              <span>{lista.data?.total ?? 0} conteos</span>
              <div className="flex gap-2">
                <Button
                  variant="outline"
                  disabled={offset === 0}
                  title="Página anterior"
                  onClick={() => setOffset(offset - 10)}
                >
                  Anterior
                </Button>
                <Button
                  variant="outline"
                  disabled={offset + 10 >= (lista.data?.total ?? 0)}
                  title="Página siguiente"
                  onClick={() => setOffset(offset + 10)}
                >
                  Siguiente
                </Button>
              </div>
            </div>
          }
        >
          {lista.isPending ? (
            <p className="p-4">Cargando conteos…</p>
          ) : lista.error ? (
            <p role="alert" className="p-4">
              {lista.error.message}
            </p>
          ) : lista.data?.items.length === 0 ? (
            <p className="p-4">No hay conteos para esta sucursal.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Apertura</TableHead>
                  <TableHead>Ubicación</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Acción</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {lista.data?.items.map((x) => (
                  <TableRow key={x.id}>
                    <TableCell>{formatDateTime(x.createdAt)}</TableCell>
                    <TableCell>{x.ubicacion ?? 'Todas las ubicaciones'}</TableCell>
                    <TableCell>
                      <Badge variant={x.cerradoEn ? 'success' : 'warning'}>
                        {x.cerradoEn ? 'Cerrado' : 'En captura'}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <Button
                        variant="ghost"
                        onClick={() => {
                          setSeleccion(x.id);
                          setPagina(0);
                          setFiltro('');
                        }}
                      >
                        Abrir reporte
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </ListadoModulo>
      )}
      <Sheet
        open={nuevo}
        onOpenChange={(open) => {
          if (
            open ||
            (!guardar.isPending &&
              (!dirtyNuevo || window.confirm('¿Descartar los datos del nuevo conteo?')))
          )
            setNuevo(open);
        }}
      >
        <SheetContent size="lg">
          <SheetHeader>
            <SheetTitle>Abrir inventario físico</SheetTitle>
          </SheetHeader>
          <SheetBody>
            <form
              className="space-y-4"
              onSubmit={(e) => {
                e.preventDefault();
                const f = new FormData(e.currentTarget);
                guardar.mutate(
                  {
                    ruta: 'conteos',
                    body: { sucursalId: sucursalNueva, ubicacion: f.get('ubicacion') || null },
                  },
                  {
                    onSuccess: (r) => {
                      setSeleccion(r.data.id);
                      setNuevo(false);
                      setDirtyNuevo(false);
                      setPagina(0);
                      setFiltro('');
                    },
                  },
                );
              }}
            >
              <p>La lista de activos esperados quedará congelada al abrir.</p>
              <p className="text-xs">Sucursal *</p>
              <SucursalSelector
                value={sucursalNueva}
                onChange={(id) => {
                  setSucursalNueva(id);
                  setDirtyNuevo(true);
                }}
              />
              <label className="text-xs">
                Ubicación (opcional)
                <Input name="ubicacion" maxLength={500} onChange={() => setDirtyNuevo(true)} />
              </label>
              <Button
                type="submit"
                disabled={!sucursalNueva || guardar.isPending}
                title="Seleccione una sucursal"
              >
                Abrir conteo
              </Button>
              {guardar.error && <p role="alert">{guardar.error.message}</p>}
            </form>
          </SheetBody>
        </SheetContent>
      </Sheet>
    </main>
  );
}
