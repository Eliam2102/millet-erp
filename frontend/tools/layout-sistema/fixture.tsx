import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { MaestroDetalle } from '../../src/components/erp/MaestroDetalle';
import { MaestroShell } from '../../src/modules/datos-maestros/components/MaestroShell';
import { PageHeader } from '../../src/components/erp/PageHeader';
import { ListadoModulo } from '../../src/components/erp/data/ListadoModulo';
import { BarraFiltros } from '../../src/components/erp/data/BarraFiltros';
import { TabsEstado } from '../../src/components/erp/data/TabsEstado';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '../../src/components/ui/tabs';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '../../src/components/ui/table';
import { Input } from '../../src/components/ui/input';
import { Button } from '../../src/components/ui/button';
import { ReporteShell } from '../../src/components/erp/reportes/ReporteShell';
import './layout.css';
const params = new URLSearchParams(location.search);
const mode = params.get('vista') ?? 'detalle';
const long = params.has('largo');
const texto = long
  ? 'MUESTRA-REFERENCIA-LARGA-SIN-ESPACIOS-'.repeat(6)
  : 'Muestra ficticia para revisión del layout';
const estados = [
  'Capturadas',
  'En revisión',
  'Por autorizar',
  'Autorizadas',
  'Parciales',
  'Con incidencias',
  'Por conciliar',
  'Programadas',
  'Cerradas',
  'Canceladas',
];
const opciones = estados.map((label, i) => ({ label, valor: i }));
function Tabla() {
  return (
    <Table className="min-w-[62rem]">
      <TableHeader>
        <TableRow>
          {['Folio', 'Proveedor', 'Referencia', 'Sucursal', 'Responsable', 'Estado', 'Importe'].map(
            (x) => (
              <TableHead key={x}>{x}</TableHead>
            ),
          )}
        </TableRow>
      </TableHeader>
      <TableBody>
        {[1, 2, 3].map((i) => (
          <TableRow key={i}>
            <TableCell className="font-mono">MUESTRA-00000{i}</TableCell>
            <TableCell>Proveedor ficticio {i}</TableCell>
            <TableCell>{texto}</TableCell>
            <TableCell>Conkal</TableCell>
            <TableCell>Responsable ficticio</TableCell>
            <TableCell>En revisión</TableCell>
            <TableCell>12,345.67</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
function Detalle() {
  return (
    <section
      aria-label="Detalle de muestra"
      className="@container min-h-0 min-w-0 flex-1 space-y-4 overflow-y-auto"
    >
      <PageHeader
        titulo={long ? texto : 'MUESTRA-2026-000001'}
        descripcion="Muestra ficticia · sin conexión al ERP"
        acciones={
          <>
            <Button variant="outline">Editar datos</Button>
            <Button>Guardar borrador</Button>
          </>
        }
      />
      <dl className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat @lg:grid-cols-2 @3xl:grid-cols-4">
        {['Proveedor', 'Referencia', 'Responsable', 'Sucursal'].map((x) => (
          <div key={x} className="min-w-0">
            <dt className="text-xs text-ink-muted">{x}</dt>
            <dd className="[overflow-wrap:anywhere]">
              {x === 'Referencia' ? texto : `${x} de muestra`}
            </dd>
          </div>
        ))}
      </dl>
      <label className="block">
        Referencia de captura
        <Input aria-label="Referencia de captura" defaultValue="Captura ficticia sin guardar" />
      </label>
      <Tabs defaultValue="0">
        <TabsList aria-label="Secciones de muestra">
          {estados.map((e, i) => (
            <TabsTrigger key={e} value={String(i)}>
              {e}
            </TabsTrigger>
          ))}
        </TabsList>
        {estados.map((e, i) => (
          <TabsContent key={e} value={String(i)}>
            <p>{e} · datos ficticios de revisión</p>
          </TabsContent>
        ))}
      </Tabs>
    </section>
  );
}
export function Fixture() {
  const [seleccion, setSeleccion] = useState(true);
  const [estado, setEstado] = useState<number | undefined>();
  const lista = (
    <aside aria-label="Lista de muestra" className="w-full space-y-3 md:w-80 md:shrink-0">
      <h2 className="font-semibold">Lista de registros</h2>
      <Input aria-label="Buscar en lista" placeholder="Buscar por folio o referencia" />
      <div className="overflow-auto rounded-lg bg-surface-card p-3 shadow-card-flat">
        {[1, 2, 3, 4].map((i) => (
          <Button key={i} variant="ghost" className="block" onClick={() => setSeleccion(true)}>
            MUESTRA-00000{i}
          </Button>
        ))}
      </div>
    </aside>
  );
  return (
    <div className="min-h-screen bg-surface-page text-sm text-ink">
      <div className="fixed inset-y-0 left-0 hidden w-rail bg-rail md:block" />
      <div className="md:ml-rail md:flex">
        <aside className="hidden h-screen w-module-panel shrink-0 border-r border-line bg-surface-card p-4 md:block">
          <h2 className="font-semibold">Millet ERP</h2>
          <p className="mt-3">Revisión transversal</p>
          {['detalle', 'maestros', 'listado', 'reporte'].map((x) => (
            <a
              key={x}
              href={`?vista=${x}${long ? '&largo=1' : ''}`}
              className="mt-3 block text-brand"
            >
              {x}
            </a>
          ))}
        </aside>
        <div className="min-w-0 flex-1">
          <div className="flex h-topbar items-center border-b border-line bg-surface-card px-6">
            MUESTRA FICTICIA · componentes reales · sin conexión
          </div>
          <main className="@container relative min-w-0 px-4 pt-6 pb-10 md:px-6">
            {mode === 'detalle' ? (
              <MaestroDetalle seleccionado={seleccion} etiquetaLista="registros" lista={lista}>
                <Detalle />
              </MaestroDetalle>
            ) : mode === 'maestros' ? (
              <MaestroShell
                idActivo="muestra"
                etiquetaLista="Lista de maestros"
                etiquetaDetalle="Detalle del maestro"
                cabecera={<h2>Maestros · muestra ficticia</h2>}
                filtros={<Input aria-label="Buscar maestro" />}
                lista={<p className="p-3">Proveedor de muestra</p>}
                sinSeleccion={<p>Selecciona un maestro</p>}
                detalle={<Detalle />}
              />
            ) : mode === 'listado' ? (
              <div className="flex min-w-0 flex-col gap-4">
                <PageHeader titulo="Bandeja de muestra" />
                <ListadoModulo
                  tabs={
                    <TabsEstado
                      opciones={opciones}
                      valor={estado}
                      onChange={setEstado}
                      conteos={{ 0: 1, 1: 2 }}
                    />
                  }
                  filtros={
                    <BarraFiltros
                      busqueda={<Input aria-label="Buscar registro" />}
                      filtros={<Button variant="outline">Todas las sucursales</Button>}
                      total="3 registros de muestra"
                    />
                  }
                  pie={<p className="p-3">3 registros</p>}
                >
                  <Tabla />
                </ListadoModulo>
              </div>
            ) : (
              <ReporteShell
                isLoading={false}
                error={undefined}
                reporte={{
                  titulo: long ? texto : 'Reporte de muestra',
                  generadoEn: '2026-11-11T12:00:00Z',
                  filtrosAplicados: { Referencia: texto },
                  columnas: [
                    { clave: 'folio', etiqueta: 'Folio', tipo: 'texto' },
                    { clave: 'referencia', etiqueta: 'Referencia', tipo: 'texto' },
                    { clave: 'importe', etiqueta: 'Importe', tipo: 'moneda' },
                  ],
                  filas: [{ folio: 'MUESTRA-000001', referencia: texto, importe: 12345.67 }],
                }}
              />
            )}
          </main>
        </div>
      </div>
    </div>
  );
}
createRoot(document.getElementById('root')!).render(<Fixture />);
