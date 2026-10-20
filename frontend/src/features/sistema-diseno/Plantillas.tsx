import { useState } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { TabsEstado } from '@/components/erp/data/TabsEstado';
import { BarraFiltros } from '@/components/erp/data/BarraFiltros';
import { PaginacionTabla } from '@/components/erp/data/PaginacionTabla';
import { KpiCard } from '@/components/erp/display/KpiCard';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { EmptyState } from '@/components/erp/feedback/EmptyState';
import { ErrorState } from '@/components/erp/feedback/ErrorState';
import { TableSkeleton } from '@/components/erp/feedback/TableSkeleton';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableHeader,
  TableHead,
  TableBody,
  TableRow,
  TableCell,
} from '@/components/ui/table';
const registros = Array.from({ length: 23 }, (_, i) => ({
  folio: `[DEMO-${String(i + 1).padStart(3, '0')}]`,
  estado: (i % 2) + 1,
}));
export function Plantillas() {
  const [estado, setEstado] = useState<number>();
  const [q, setQ] = useState('');
  const [offset, setOffset] = useState(0);
  const [vista, setVista] = useState('datos');
  const [activo, setActivo] = useState(registros[0].folio);
  const filtrados = registros.filter(
    (r) => (!estado || r.estado === estado) && r.folio.toLowerCase().includes(q.toLowerCase()),
  );
  return (
    <div className="space-y-5">
      <section
        aria-label="Plantilla 5.1"
        className="space-y-5 rounded-lg bg-surface-card p-4 shadow-card-flat"
      >
        <PageHeader
          titulo="5.1 · Listado ficticio"
          descripcion="Consulta documentos de muestra y prueba filtros y estados."
          acciones={
            <Button disabled title="La demostración no crea documentos">
              Registrar documento
            </Button>
          }
        />
        <div className="grid gap-3 sm:grid-cols-4">
          {['Por autorizar', 'Rechazadas', 'Por vencer', 'Con diferencia'].map((etiqueta, i) => (
            <KpiCard
              key={etiqueta}
              etiqueta={etiqueta}
              valor={i + 1}
              href="#tabla-demo"
              tono={i === 1 ? 'danger' : 'warning'}
              subdato="Datos ficticios"
            />
          ))}
        </div>
        <label>
          Estado del ejemplo
          <select
            className="ml-3 rounded-md border border-line-control p-2"
            value={vista}
            onChange={(e) => setVista(e.target.value)}
          >
            {['datos', 'vacío', 'cargando', 'error'].map((v) => (
              <option key={v}>{v}</option>
            ))}
          </select>
        </label>
        <div id="tabla-demo">
          <ListadoModulo
            tabs={
              <TabsEstado
                opciones={[
                  { valor: 1, label: 'Capturada' },
                  { valor: 2, label: 'Autorizada' },
                ]}
                valor={estado}
                onChange={(v) => {
                  setEstado(v);
                  setOffset(0);
                }}
                conteos={
                  vista === 'cargando'
                    ? undefined
                    : {
                        1: registros.filter(
                          (r) => r.estado === 1 && r.folio.toLowerCase().includes(q.toLowerCase()),
                        ).length,
                        2: registros.filter(
                          (r) => r.estado === 2 && r.folio.toLowerCase().includes(q.toLowerCase()),
                        ).length,
                      }
                }
              />
            }
            filtros={
              <BarraFiltros
                busqueda={
                  <label>
                    Buscar ejemplo
                    <Input
                      value={q}
                      onChange={(e) => {
                        setQ(e.target.value);
                        setOffset(0);
                      }}
                    />
                  </label>
                }
                onLimpiar={() => {
                  setQ('');
                  setEstado(undefined);
                  setOffset(0);
                }}
                total={`${filtrados.length} ejemplos`}
              />
            }
            pie={
              vista === 'datos' && (
                <PaginacionTabla
                  total={filtrados.length}
                  offset={offset}
                  limit={10}
                  tamanos={[]}
                  onChange={setOffset}
                />
              )
            }
          >
            {vista === 'cargando' ? (
              <TableSkeleton rows={10} />
            ) : vista === 'error' ? (
              <ErrorState
                title="Carga ficticia interrumpida"
                problem={{
                  type: 'about:blank',
                  title: 'Fallo simulado',
                  status: 503,
                  detail: 'Se simuló un fallo de lectura. Reintenta para ver los ejemplos.',
                }}
                onRetry={() => setVista('datos')}
              />
            ) : vista === 'vacío' || !filtrados.length ? (
              <EmptyState
                title="Sin ejemplos"
                description="Limpia los filtros o selecciona el estado datos."
              />
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Folio</TableHead>
                    <TableHead>Entidad</TableHead>
                    <TableHead className="text-right">Importe</TableHead>
                    <TableHead>Estado</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {filtrados.slice(offset, offset + 10).map((r) => (
                    <TableRow key={r.folio}>
                      <TableCell>
                        <a
                          href="#detalle-demo"
                          className="font-mono text-brand"
                          onClick={() => setActivo(r.folio)}
                        >
                          {r.folio}
                        </a>
                      </TableCell>
                      <TableCell>Vidrios y Aluminios del Sureste</TableCell>
                      <TableCell className="text-right">
                        <MoneyDisplay amount={1044} currency="MXN" />
                      </TableCell>
                      <TableCell>
                        <Badge variant={r.estado === 1 ? 'neutral' : 'success'}>
                          {r.estado === 1 ? 'Capturada' : 'Autorizada'}
                        </Badge>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </ListadoModulo>
        </div>
        <p className="text-xs text-ink-muted">
          Diez filas por página. Anterior se bloquea al inicio y siguiente al terminar.
        </p>
      </section>
      <section
        id="detalle-demo"
        aria-label="Plantilla 5.2"
        className="flex flex-col gap-5 rounded-lg bg-surface-card p-4 shadow-card-flat lg:flex-row"
      >
        <nav
          aria-label="Lista maestra ficticia"
          className="max-h-96 shrink-0 overflow-auto lg:w-80"
        >
          {registros.map((r) => (
            <button
              key={r.folio}
              className={`block w-full px-3 py-3 text-left font-mono ${activo === r.folio ? 'bg-surface-selected shadow-list-selected' : 'bg-surface-card'}`}
              aria-current={activo === r.folio ? 'true' : undefined}
              onClick={() => setActivo(r.folio)}
            >
              {r.folio}
            </button>
          ))}
        </nav>
        <div className="min-w-0 flex-1 space-y-5">
          <PageHeader
            titulo={activo}
            mono
            estado={<Badge variant="neutral">Capturada</Badge>}
            descripcion="5.2 · Detalle de Vidrios y Aluminios del Sureste"
            acciones={
              <Button disabled title="Registra la recepción ficticia para habilitar">
                Autorizar
              </Button>
            }
          />
          <dl className="grid grid-cols-2 gap-4 sm:grid-cols-4">
            {['Cliente', 'Sucursal', 'Moneda', 'Fecha'].map((v, i) => (
              <div key={v}>
                <dt className="text-xs text-ink-muted">{v}</dt>
                <dd>{['Vidrios y Aluminios del Sureste', 'Sucursal Circuito', 'MXN', '21 sep 2026'][i]}</dd>
              </div>
            ))}
          </dl>
          <h3 className="text-md font-semibold">Validación</h3>
          <AvisoEnLinea tono="advertencia">
            Falta recepción. Registra la recepción para autorizar el documento ficticio.
          </AvisoEnLinea>
          <h3 className="text-md font-semibold">Partidas</h3>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Descripción</TableHead>
                <TableHead>Cantidad</TableHead>
                <TableHead className="text-right">Importe</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow>
                <TableCell>Vidrio templado de 6 mm</TableCell>
                <TableCell>1</TableCell>
                <TableCell className="text-right">
                  <MoneyDisplay amount={1000} currency="MXN" />
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
          <ResumenImportes
            moneda="MXN"
            subtotal={1000}
            descuento={100}
            ivaTrasladado={144}
            total={1044}
          />
          <h3 className="text-md font-semibold">Actividad</h3>
          <p>Documento ficticio capturado · 21 sep 2026</p>
        </div>
      </section>
    </div>
  );
}
