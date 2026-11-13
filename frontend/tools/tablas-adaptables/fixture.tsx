import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { ListadoModulo } from '../../src/components/erp/data/ListadoModulo';
import { PaginacionTabla } from '../../src/components/erp/data/PaginacionTabla';
import { usePaginacionLocal } from '../../src/components/erp/data/usePaginacionLocal';
import { PageHeader } from '../../src/components/erp/PageHeader';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '../../src/components/ui/table';
import './layout.css';
const datos = Array.from({ length: 357 }, (_, i) => ({
  id: i + 1,
  folio: `QA-${String(i + 1).padStart(6, '0')}`,
  nombre: '[PROVEEDOR FICTICIO] Distribución de vidrio y materiales',
  monto: (i + 1) * 123.45,
}));
export function App() {
  const [filtro, setFiltro] = useState('');
  const [captura, setCaptura] = useState('');
  const filtrados = datos.filter((x) => x.folio.includes(filtro));
  const pagina = usePaginacionLocal(filtrados, filtro);
  if (new URLSearchParams(location.search).has('consulta')) return <>
    <header className="flex h-14 items-center border-b border-line bg-surface-card px-4 text-sm">Prueba local de consulta · datos ficticios</header>
    <main className="flex h-[calc(100dvh-3.5rem)] min-w-0 flex-col gap-4 p-4 text-ink md:p-6">
      <PageHeader titulo="Consulta con tabla compartida" descripcion="Colección ficticia paginada sin modificar su origen." />
      <div className="flex flex-wrap gap-3">
        <label>Buscar folio<input aria-label="Buscar folio" value={filtro} onChange={e => setFiltro(e.target.value)} className="ml-2 min-h-11 rounded-md border border-line-control px-2" /></label>
        <label>Referencia de captura<input aria-label="Referencia de captura" value={captura} onChange={e => setCaptura(e.target.value)} className="ml-2 min-h-11 rounded-md border border-line-control px-2" /></label>
      </div>
      <Table paginada className="min-w-[48rem]">
        <TableHeader><TableRow><TableHead>Folio</TableHead><TableHead>Proveedor / descripción</TableHead><TableHead>Fecha</TableHead><TableHead className="text-right">Importe</TableHead><TableHead>Estado</TableHead></TableRow></TableHeader>
        <TableBody>{filtrados.map(x => <TableRow key={x.id}><TableCell className="font-mono">{x.folio}</TableCell><TableCell>{x.nombre}</TableCell><TableCell>12 nov 2026</TableCell><TableCell className="text-right tabular-nums">{x.monto.toLocaleString('es-MX',{style:'currency',currency:'MXN'})}</TableCell><TableCell>Registrado</TableCell></TableRow>)}</TableBody>
      </Table>
    </main>
  </>;
  return (
    <>
      <header className="flex h-14 items-center border-b border-line bg-surface-card px-4 text-sm text-ink">
        Prueba local · datos ficticios · sin conexión a QA
      </header>
      <main className="flex h-[calc(100dvh-3.5rem)] min-w-0 flex-col gap-4 p-4 text-ink md:p-6">
        <PageHeader
          titulo="Registros y bitácoras"
          descripcion="Revisión de cantidad, espacio, distribución y captura conservada."
        />
        <ListadoModulo
          filtros={
            <div className="flex flex-wrap gap-3">
              <label className="text-xs">
                Buscar folio
                <input
                  aria-label="Buscar folio"
                  className="ml-2 min-h-11 rounded-md border border-line-control px-2"
                  value={filtro}
                  onChange={(e) => setFiltro(e.target.value)}
                />
              </label>
              <label className="text-xs">
                Referencia de captura
                <input
                  aria-label="Referencia de captura"
                  className="ml-2 min-h-11 rounded-md border border-line-control px-2"
                  value={captura}
                  onChange={(e) => setCaptura(e.target.value)}
                />
              </label>
            </div>
          }
          pie={
            <PaginacionTabla regionId={pagina.regionId}
              total={filtrados.length}
              offset={pagina.offset}
              limit={pagina.limit}
              onChange={pagina.onChange}
            />
          }
        >
          <Table className="min-w-[48rem]">
            <TableHeader>
              <TableRow>
                <TableHead className="w-36">Folio</TableHead>
                <TableHead>Proveedor / descripción</TableHead>
                <TableHead className="w-28">Fecha</TableHead>
                <TableHead className="w-32 text-right">Importe</TableHead>
                <TableHead className="w-28">Estado</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {pagina.visibles.map((x) => (
                <TableRow key={x.id}>
                  <TableCell className="font-mono">{x.folio}</TableCell>
                  <TableCell>{x.nombre}</TableCell>
                  <TableCell>12 nov 2026</TableCell>
                  <TableCell className="text-right tabular-nums">
                    {x.monto.toLocaleString('es-MX', { style: 'currency', currency: 'MXN' })}
                  </TableCell>
                  <TableCell>Registrado</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ListadoModulo>
      </main>
    </>
  );
}
createRoot(document.getElementById('root')!).render(<App />);
