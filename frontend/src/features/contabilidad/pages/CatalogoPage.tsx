import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { BookOpen, Plus, Search, Upload } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useCuentas } from '../api/hooks';
import type { ClaseCuenta, FiltroEstatus, TipoCuenta } from '../api/types';
import { ArbolCuentas } from '../components/ArbolCuentas';
import { SELECT_CLASS } from '../lib/estilos';
import { InsigniasCuenta } from '../components/InsigniasCuenta';
import { SolicitudesCatalogo } from '../components/SolicitudesCatalogo';
import { NuevaCuentaSheet } from '../components/NuevaCuentaSheet';

const SELECT = SELECT_CLASS;
const LIMITE = 50;

/** Bandeja del catálogo contable: árbol perezoso o lista paginada, con búsqueda y filtros. */
export function CatalogoPage() {
  const puedeAdministrar = useHasPermission(PermisosCanonicos.ContabilidadCatalogoAdministrar);
  const puedeImportar = useHasPermission(PermisosCanonicos.ContabilidadCatalogoImportar);

  const [solicitudes, setSolicitudes] = useState(false);
  const [vista, setVista] = useState<'arbol' | 'lista'>('arbol');
  const [busqueda, setBusqueda] = useState('');
  const [estatus, setEstatus] = useState<FiltroEstatus>('');
  const [tipo, setTipo] = useState<TipoCuenta | ''>('');
  const [pendientes, setPendientes] = useState(false);
  const [clase, setClase] = useState<ClaseCuenta | ''>('');
  const [offset, setOffset] = useState(0);
  const [nueva, setNueva] = useState(false);

  const q = useDebouncedValue(busqueda.trim(), 200);
  // El endpoint del árbol solo filtra por estatus: con búsqueda/tipo/pendientes/rubros se muestra la lista.
  // Los rubros (P24) no son nodos del árbol: se consultan con el filtro «Solo rubros».
  const filtrando = q !== '' || tipo !== '' || pendientes || clase !== '';
  const enLista = vista === 'lista' || filtrando;
  const lista = useCuentas({ estatus, tipo, q, pendientes, clase: clase || undefined, offset, limit: LIMITE }, enLista);

  const vacioCatalogo = (
    <span data-testid="catalogo-vacio" className="flex flex-col items-start gap-2">
      <span>El catálogo está vacío. Importa el catálogo de cuentas para comenzar.</span>
      {puedeImportar ? (
        <Button asChild size="sm">
          <Link to="/contabilidad/importacion"><Upload className="mr-1 size-4" aria-hidden="true" />Importar catálogo</Link>
        </Button>
      ) : (
        <span className="text-xs">No tienes permiso para importar; contacta a Contabilidad.</span>
      )}
    </span>
  );

  function cambiar<T>(set: (v: T) => void) {
    return (v: T) => {
      set(v);
      setOffset(0);
    };
  }

  return (
    <div className="space-y-4 px-6 py-5">
      <header className="flex flex-wrap items-center gap-2">
        <div>
          <h1 className="flex items-center gap-2 text-3xl font-semibold">
            <BookOpen className="size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
            Catálogo de cuentas
          </h1>
          <p className="text-sm text-ink-muted">Consulta, alta y baja lógica de cuentas contables; marca las pendientes de validación.</p>
        </div>
        <div className="ml-auto flex items-center gap-2" data-print="hidden">
          <Button variant="outline" aria-pressed={solicitudes} onClick={() => setSolicitudes(!solicitudes)}>Autorizaciones</Button>
          {puedeImportar && (
            <Button asChild variant="outline">
              <Link to="/contabilidad/importacion"><Upload className="mr-1 size-4" aria-hidden="true" />Importar</Link>
            </Button>
          )}
          {puedeAdministrar && (
            <Button onClick={() => setNueva(true)}>
              <Plus className="mr-1 size-4" aria-hidden="true" />Nueva cuenta
            </Button>
          )}
        </div>
      </header>

      {solicitudes && <SolicitudesCatalogo />}

      <div className="flex flex-wrap items-center gap-3" data-print="hidden">
        <div className="relative">
          <Search className="pointer-events-none absolute left-2.5 top-2.5 size-4 text-ink-muted" aria-hidden="true" />
          <Input
            aria-label="Buscar cuenta"
            placeholder="Buscar por código o nombre…"
            className="w-72 pl-8"
            value={busqueda}
            onChange={(e) => cambiar(setBusqueda)(e.target.value)}
          />
        </div>
        <select aria-label="Filtro de estatus" className={SELECT} value={estatus} onChange={(e) => cambiar(setEstatus)(e.target.value as FiltroEstatus)}>
          <option value="">Todas</option>
          <option value="Activo">Activas</option>
          <option value="Inactivo">Inactivas</option>
        </select>
        <select aria-label="Filtro de tipo" className={SELECT} value={tipo} onChange={(e) => cambiar(setTipo)(e.target.value as TipoCuenta | '')}>
          <option value="">Acumulan y afectables</option>
          <option value="Titulo">Solo las que acumulan</option>
          <option value="Afectable">Solo afectables</option>
        </select>
        <select aria-label="Filtro de clase" className={SELECT} value={clase} onChange={(e) => cambiar(setClase)(e.target.value as ClaseCuenta | '')}>
          <option value="">Cuentas y rubros</option>
          <option value="Cuenta">Solo cuentas</option>
          <option value="Rubro">Solo rubros de reporte</option>
        </select>
        <div className="flex items-center gap-2">
          <Checkbox id="f-pendientes" checked={pendientes} onCheckedChange={(v) => cambiar(setPendientes)(v === true)} />
          <Label htmlFor="f-pendientes" className="text-sm font-normal">Pendientes de validación</Label>
        </div>
        <div className="ml-auto inline-flex rounded-md border border-line-control" role="group" aria-label="Vista">
          <Button variant={enLista ? 'ghost' : 'secondary'} size="sm" aria-pressed={!enLista} disabled={filtrando} title={filtrando ? 'Con búsqueda o filtros se muestra la lista' : undefined} onClick={() => setVista('arbol')}>Árbol</Button>
          <Button variant={enLista ? 'secondary' : 'ghost'} size="sm" aria-pressed={enLista} onClick={() => setVista('lista')}>Lista</Button>
        </div>
      </div>
      {filtrando && (
        <p className="text-xs text-ink-muted">Con búsqueda o filtros adicionales se muestra la lista.</p>
      )}

      {enLista ? (
        <ListaCuentas
          consulta={lista}
          offset={offset}
          setOffset={setOffset}
          vacio={filtrando || estatus !== '' ? 'Sin resultados para los filtros aplicados.' : vacioCatalogo}
        />
      ) : (
        <ArbolCuentas estatus={estatus} vacio={estatus !== '' ? 'Sin cuentas con ese estatus.' : vacioCatalogo} />
      )}

      {puedeAdministrar && <NuevaCuentaSheet open={nueva} onOpenChange={setNueva} />}
    </div>
  );
}

function ListaCuentas({
  consulta, offset, setOffset, vacio,
}: {
  consulta: ReturnType<typeof useCuentas>;
  offset: number;
  setOffset: (n: number) => void;
  vacio: React.ReactNode;
}) {
  if (consulta.isLoading) {
    return (
      <div className="space-y-2" data-testid="lista-cargando" aria-label="Cargando cuentas">
        <Skeleton className="h-8 w-full" /><Skeleton className="h-8 w-full" /><Skeleton className="h-8 w-full" />
      </div>
    );
  }
  if (consulta.isError) {
    return (
      <div role="alert" className="flex items-center gap-2 rounded-lg border border-danger p-4 text-danger-fg">
        No se pudo cargar el catálogo.
        <Button variant="ghost" size="sm" onClick={() => void consulta.refetch()}>Reintentar</Button>
      </div>
    );
  }
  const { items, total } = consulta.data!;
  if (items.length === 0) return <div className="rounded-lg bg-surface-card shadow-card-flat p-6 text-sm text-ink-muted">{vacio}</div>;
  return (
    <div className="space-y-2">
      <div className="overflow-x-auto rounded-lg bg-surface-card shadow-card-flat">
        <table className="w-full min-w-[640px] text-sm">
          <thead className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
            <tr><th className="px-3 py-2">Código</th><th className="px-3 py-2">Nombre</th><th className="px-3 py-2">Nivel</th><th className="px-3 py-2">Estado</th></tr>
          </thead>
          <tbody>
            {items.map((c) => (
              <tr key={c.id} className="border-b border-line-row hover:bg-surface-subtle">
                <td className="px-3 py-1.5 font-mono text-xs">
                  <Link to="/contabilidad/catalogo/$id" params={{ id: c.id }} className="hover:underline">{c.codigo}</Link>
                </td>
                <td className="px-3 py-1.5">{c.nombre}</td>
                <td className="px-3 py-1.5">{c.nivel}</td>
                <td className="px-3 py-1.5">
                  <InsigniasCuenta tipo={c.tipo} naturaleza={c.naturaleza} activa={c.activa} pendienteValidacion={c.pendienteValidacion} control={c.cuentaControl} clase={c.clase} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="flex items-center justify-between text-xs text-ink-muted" data-print="hidden">
        <span>{offset + 1}–{offset + items.length} de {total}</span>
        <span className="flex gap-2">
          <Button variant="outline" size="sm" disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - LIMITE))}>Anterior</Button>
          <Button variant="outline" size="sm" disabled={offset + items.length >= total} onClick={() => setOffset(offset + LIMITE)}>Siguiente</Button>
        </span>
      </div>
    </div>
  );
}
