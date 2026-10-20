import { useId, useState, type ReactNode, type ComponentType } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { EmptyState } from '@/components/erp/feedback/EmptyState';
import { ErrorState } from '@/components/erp/feedback/ErrorState';
import { TableSkeleton } from '@/components/erp/feedback/TableSkeleton';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableHeader,
  TableHead,
  TableBody,
  TableRow,
  TableCell,
} from '@/components/ui/table';
import catalogo from './catalogo.json';
import { Fundamentos } from './Fundamentos';
import { Plantillas } from './Plantillas';

const demos = import.meta.glob<{ default: ComponentType }>('./ejemplos/*.tsx', { eager: true });
const fuentes = import.meta.glob<string>('./ejemplos/*.tsx', {
  eager: true,
  query: '?raw',
  import: 'default',
});

function Codigo({ codigo }: { codigo: string }) {
  const [estado, setEstado] = useState('');
  async function copiar() {
    try {
      await navigator.clipboard.writeText(codigo);
      setEstado('Código copiado');
    } catch {
      setEstado('No se pudo copiar. Selecciona el código y cópialo con el teclado.');
    }
  }
  return (
    <details>
      <summary className="cursor-pointer text-brand">Código de uso e imports</summary>
      <Button variant="secondary" size="sm" onClick={copiar}>
        Copiar código
      </Button>
      <span role="status" className="ml-3 text-xs">
        {estado}
      </span>
      <pre
        tabIndex={0}
        className="mt-3 overflow-auto rounded-md bg-surface-muted p-4 font-mono text-xs"
      >
        <code>{codigo}</code>
      </pre>
    </details>
  );
}

function EstadoEjemplo({ children }: { children: ReactNode }) {
  const [estado, setEstado] = useState('datos');
  const id = useId();
  return (
    <div className="space-y-3">
      <label htmlFor={id}>Estado del contenedor</label>
      <select
        id={id}
        className="ml-3 rounded-md border border-line-control bg-surface-card p-2"
        value={estado}
        onChange={(e) => setEstado(e.target.value)}
      >
        {['datos', 'vacío', 'cargando', 'error', 'deshabilitado'].map((v) => (
          <option key={v}>{v}</option>
        ))}
      </select>
      <p className="text-xs text-ink-muted">
        Carga, vacío y error son estados del contenedor; no añaden props al componente. Los
        componentes de lectura permanecen visibles al bloquear las acciones.
      </p>
      {estado === 'vacío' ? (
        <EmptyState
          title="Sin datos ficticios"
          description="Selecciona datos para restaurar el ejemplo."
        />
      ) : estado === 'cargando' ? (
        <TableSkeleton rows={2} />
      ) : estado === 'error' ? (
        <ErrorState
          title="Fallo simulado del ejemplo"
          problem={{
            type: 'about:blank',
            title: 'Fallo simulado',
            status: 503,
            detail: 'Se interrumpió la carga ficticia. Reintenta para restaurar los datos.',
          }}
          onRetry={() => setEstado('datos')}
        />
      ) : (
        <fieldset
          disabled={estado === 'deshabilitado'}
          aria-describedby={estado === 'deshabilitado' ? `${id}-motivo` : undefined}
          className="min-w-0 space-y-3"
        >
          <legend className="sr-only">Demostración</legend>
          {estado === 'deshabilitado' && (
            <p id={`${id}-motivo`}>
              Acciones bloqueadas porque falta seleccionar un documento ficticio. Selecciona datos
              para habilitarlas.
            </p>
          )}
          {children}
        </fieldset>
      )}
    </div>
  );
}

export function SistemaDisenoPage() {
  return (
    <div className="flex h-[calc(100dvh-7.5rem)] min-h-0 flex-col gap-5 text-sm text-ink">
      <PageHeader
        titulo="Sistema de diseño"
        descripcion="Explora los componentes, prueba sus estados y copia ejemplos para construir pantallas."
      />
      <AvisoEnLinea tono="advertencia">
        Todos los datos son ficticios. Las acciones de los ejemplos sólo cambian esta demostración.
      </AvisoEnLinea>
      <div className="flex min-h-0 flex-1 flex-col gap-5 md:flex-row">
        <nav
          aria-label="Secciones del catálogo"
          className="max-h-48 shrink-0 overflow-auto rounded-lg bg-surface-card p-4 shadow-card-flat md:max-h-none md:w-60"
        >
          <a className="block py-1 text-brand" href="#fundamentos">
            Fundamentos
          </a>
          <a className="block py-1 text-brand" href="#componentes">
            Componentes
          </a>
          {catalogo.map((c) => (
            <a key={c.nombre} className="block py-1 text-ink-secondary" href={`#${c.nombre}`}>
              {c.nombre}
            </a>
          ))}
          <a className="block py-1 text-brand" href="#plantillas">
            Plantillas 5.1 y 5.2
          </a>
          <a className="block py-1 text-brand" href="#contenido">
            Reglas de contenido
          </a>
        </nav>
        <div className="min-w-0 flex-1 space-y-5 overflow-auto">
          <section
            id="fundamentos"
            aria-labelledby="fundamentos-titulo"
            className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat"
          >
            <h2 id="fundamentos-titulo" className="text-lg font-semibold">
              Fundamentos
            </h2>
            <Fundamentos />
          </section>
          <section id="componentes" aria-labelledby="componentes-titulo" className="space-y-5">
            <h2 id="componentes-titulo" className="text-lg font-semibold">
              Componentes
            </h2>
            {catalogo.map((c) => {
              const ruta = `./ejemplos/${c.nombre}.tsx`;
              const Demo = demos[ruta].default;
              return (
                <article
                  key={c.nombre}
                  id={c.nombre}
                  aria-labelledby={`${c.nombre}-titulo`}
                  className="space-y-4 rounded-lg bg-surface-card p-4 shadow-card-flat"
                >
                  <h3 id={`${c.nombre}-titulo`} className="text-lg font-semibold">
                    {c.nombre}
                  </h3>
                  <p>
                    <strong>Para qué sirve: </strong>
                    {c.uso}
                  </p>
                  <p>
                    <strong>Cuándo no usarlo: </strong>
                    {c.noUsar}
                  </p>
                  <p className="text-xs text-ink-muted">Regla de diseño {c.regla}</p>
                  <div
                    aria-label={`Ejemplo de ${c.nombre}`}
                    className="rounded-md bg-surface-subtle p-4"
                  >
                    <EstadoEjemplo>
                      <Demo />
                    </EstadoEjemplo>
                  </div>
                  <div className="overflow-auto">
                    <Table>
                      <caption className="text-left text-xs text-ink-muted">
                        Props principales de {c.nombre}
                      </caption>
                      <TableHeader>
                        <TableRow>
                          <TableHead>Nombre</TableHead>
                          <TableHead>Tipo</TableHead>
                          <TableHead>Para qué</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {c.props.map(([nombre, tipo, uso]) => (
                          <TableRow key={nombre}>
                            <TableCell className="font-mono">{nombre}</TableCell>
                            <TableCell>{tipo}</TableCell>
                            <TableCell>{uso}</TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                  </div>
                  <Codigo codigo={fuentes[ruta]} />
                </article>
              );
            })}
          </section>
          <section id="plantillas" aria-labelledby="plantillas-titulo" className="space-y-4">
            <h2 id="plantillas-titulo" className="text-lg font-semibold">
              Plantillas 5.1 y 5.2
            </h2>
            <Plantillas />
          </section>
          <section
            id="contenido"
            aria-labelledby="contenido-titulo"
            className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
          >
            <h2 id="contenido-titulo" className="text-lg font-semibold">
              Reglas de contenido
            </h2>
            <p>
              §6 · Español de México y mayúscula sólo al inicio. Botones con verbo específico:
              Registrar factura, Aplicar pago, Rechazar.
            </p>
            <p>
              Los renglones se llaman Partidas. Los obligatorios llevan *. Los valores desconocidos
              se escriben [ENTRE CORCHETES].
            </p>
            <p>
              Importes: $412,875.40; porcentajes: 82 %; fechas en listas: 21 sep; detalle: 21 sep
              2026. Folios en Plex Mono.
            </p>
            <p>
              Los errores explican causa y acción. Ejemplo: Adjunta la cotización para solicitar
              autorización. Toda acción deshabilitada explica cómo habilitarla.
            </p>
            <p>
              §7.2 · Conserva el mapa de estados de cada entidad: un estado, un color y un texto
              visible. Máximo dos badges por fila.
            </p>
            <p>
              §8 · Labels asociados, foco visible, botones reales y nombres accesibles para acciones
              de ícono. No comuniques estados sólo con color.
            </p>
          </section>
        </div>
      </div>
    </div>
  );
}
