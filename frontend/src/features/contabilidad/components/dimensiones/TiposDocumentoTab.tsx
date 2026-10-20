import { ESTADOS_ACTIVIDAD } from '../../lib/estados-ui';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import { TabsContabilidad } from '../TabsContabilidad';
import { usePaginacionLocal } from '../../lib/use-paginacion-local';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useState } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import {
  useCrearTipoDocumento,
  useEditarTipoDocumento,
  useTiposDocumento,
} from '../../api/dimensiones';
import { TIPOS_SAT, type TipoPolizaSat } from '../../api/polizas-types';
import { mensajeError } from '../../lib/dimensiones';
import { SELECT_CLASS } from '../../lib/estilos';
import { CLAVE_OPERACION, ETIQUETA_TIPO_SAT } from '../../lib/polizas';

/**
 * Catálogo de tipos de documento contable, que son también los tipos de operación de las pólizas (K10.9): la clave de dos
 * letras forma el folio (TT-000123) y el tipo SAT clasifica la póliza (Ingreso, Egreso o Diario).
 */
export function TiposDocumentoTab({ puedeAdministrar }: { puedeAdministrar: boolean }) {
  const tipos = useTiposDocumento(true);
  const crear = useCrearTipoDocumento();
  const editar = useEditarTipoDocumento();
  const [clave, setClave] = useState('');
  const [nombre, setNombre] = useState('');
  const [esPrueba, setEsPrueba] = useState(true);
  const [tipoSat, setTipoSat] = useState<TipoPolizaSat>('Diario');
  const [error, setError] = useState('');
  const [errorFila, setErrorFila] = useState('');

  const [estadoVista, setEstadoVista] = useState<string>();
  const filas = (tipos.data ?? []).filter(
    (t) => !estadoVista || (t.activo ? 'Activo' : 'Inactivo') === estadoVista,
  );
  const pagina = usePaginacionLocal(filas, estadoVista ?? '');
  const claveNormalizada = clave.trim().toUpperCase();
  const claveInvalida =
    !esPrueba && claveNormalizada !== '' && !CLAVE_OPERACION.test(claveNormalizada);

  function agregar(e: React.FormEvent) {
    e.preventDefault();
    setError('');
    if (claveInvalida) return;
    crear.mutate(
      {
        clave: esPrueba ? clave.trim() : claveNormalizada,
        nombre: nombre.trim(),
        esPrueba,
        tipoSat,
      },
      {
        onSuccess: () => {
          setClave('');
          setNombre('');
        },
        onError: (x) => setError(mensajeError(x)),
      },
    );
  }

  return (
    <div className="flex flex-col gap-4">
      {puedeAdministrar && (
        <form
          onSubmit={agregar}
          className="flex flex-wrap items-end gap-3 rounded-lg border border-dashed border-brand p-4"
          data-print="hidden"
        >
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="td-clave">
              Clave<span aria-hidden="true"> *</span>
            </Label>
            <Input
              id="td-clave"
              required
              maxLength={esPrueba ? 20 : 2}
              className="w-40 font-mono uppercase"
              value={clave}
              onChange={(e) => setClave(e.target.value)}
              placeholder={esPrueba ? 'FIX-FP' : 'TT'}
              aria-invalid={claveInvalida || undefined}
              aria-describedby="td-clave-ayuda"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="td-nombre">
              Nombre<span aria-hidden="true"> *</span>
            </Label>
            <Input
              id="td-nombre"
              required
              maxLength={120}
              className="w-80"
              value={nombre}
              onChange={(e) => setNombre(e.target.value)}
              placeholder="[TIPO DE DOCUMENTO]"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="td-sat">Tipo SAT</Label>
            <select
              id="td-sat"
              className={SELECT_CLASS}
              value={tipoSat}
              onChange={(e) => setTipoSat(e.target.value as TipoPolizaSat)}
            >
              {TIPOS_SAT.map((t) => (
                <option key={t} value={t}>
                  {ETIQUETA_TIPO_SAT[t]}
                </option>
              ))}
            </select>
          </div>
          <div className="flex h-ctl-xl items-center gap-2">
            <Checkbox
              id="td-prueba"
              checked={esPrueba}
              onCheckedChange={(v) => setEsPrueba(v === true)}
            />
            <Label htmlFor="td-prueba" className="font-normal">
              De prueba
            </Label>
          </div>
          <Button
            type="submit"
            disabled={crear.isPending || claveInvalida}
            title={claveInvalida ? 'La clave del tipo de operación son dos letras' : undefined}
          >
            {crear.isPending ? 'Agregando…' : 'Agregar tipo'}
          </Button>
          <p
            id="td-clave-ayuda"
            className={
              claveInvalida
                ? 'basis-full text-xs text-danger-fg'
                : 'basis-full text-xs text-ink-muted'
            }
          >
            {esPrueba
              ? 'Los tipos de prueba conservan una clave libre (FIX-…).'
              : 'La clave del tipo de operación son dos letras (TT, PC, RF…): forma el folio de la póliza, por ejemplo TT-000123.'}
          </p>
          {error && (
            <p role="alert" className="basis-full text-sm text-danger-fg">
              {error}
            </p>
          )}
        </form>
      )}
      {errorFila && (
        <p role="alert" className="text-sm text-danger-fg">
          {errorFila}
        </p>
      )}
      <ListadoModulo
        tabs={
          <TabsContabilidad
            estados={[
              { estado: 'Activo', label: 'Activos' },
              { estado: 'Inactivo', label: 'Inactivos' },
            ]}
            valor={estadoVista}
            onChange={setEstadoVista}
          />
        }
        filtros={<BarraFiltros total={`${filas.length} tipos`} />}
        pie={
          <PaginacionTabla
            total={filas.length}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.onChange}
          />
        }
      >
        {tipos.isLoading ? (
          <div className="p-4" aria-label="Cargando tipos">
            <Skeleton className="h-8 w-full" />
          </div>
        ) : tipos.isError ? (
          <p role="alert" className="p-4 text-sm text-danger-fg">
            No se pudieron cargar los tipos de documento.
          </p>
        ) : tipos.data!.length === 0 ? (
          <p className="p-6 text-sm text-ink-muted">
            Aún no hay tipos de documento. La lista oficial la entrega Contabilidad de Millet.
          </p>
        ) : (
          <Table className="w-full text-sm">
            <TableHeader className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
              <TableRow>
                <TableHead className="px-3 py-2">Clave</TableHead>
                <TableHead className="px-3 py-2">Nombre</TableHead>
                <TableHead className="px-3 py-2">Tipo SAT</TableHead>
                <TableHead className="px-3 py-2">Estado</TableHead>
                {puedeAdministrar && (
                  <TableHead className="px-3 py-2">
                    <span className="sr-only">Acciones</span>
                  </TableHead>
                )}
              </TableRow>
            </TableHeader>
            <TableBody>
              {pagina.visibles.map((t) => (
                <TableRow key={t.id} className="border-b border-line-row">
                  <TableCell className="px-3 py-2 font-mono text-xs">{t.clave}</TableCell>
                  <TableCell className="px-3 py-2">{t.nombre}</TableCell>
                  <TableCell className="px-3 py-2">
                    {puedeAdministrar ? (
                      <select
                        aria-label={`Tipo SAT de ${t.clave}`}
                        className={SELECT_CLASS}
                        value={t.tipoSat ?? 'Diario'}
                        disabled={editar.isPending}
                        onChange={(e) => {
                          setErrorFila('');
                          editar.mutate(
                            { ...t, nuevoTipoSat: e.target.value as TipoPolizaSat },
                            { onError: (x) => setErrorFila(mensajeError(x)) },
                          );
                        }}
                      >
                        {TIPOS_SAT.map((s) => (
                          <option key={s} value={s}>
                            {ETIQUETA_TIPO_SAT[s]}
                          </option>
                        ))}
                      </select>
                    ) : (
                      (ETIQUETA_TIPO_SAT[t.tipoSat] ?? '—')
                    )}
                  </TableCell>
                  <TableCell className="px-3 py-2">
                    <span className="flex gap-1">
                      <Badge variant={ESTADOS_ACTIVIDAD[t.activo ? 'Activo' : 'Inactivo'].tono}>
                        {t.activo ? 'Activo' : 'Inactivo'}
                      </Badge>
                      {t.esPrueba && <Badge variant="warning">Prueba</Badge>}
                    </span>
                  </TableCell>
                  {puedeAdministrar && (
                    <TableCell className="px-3 py-2 text-right">
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={editar.isPending}
                        onClick={() => {
                          setErrorFila('');
                          editar.mutate(
                            { ...t, nuevoActivo: !t.activo },
                            { onError: (x) => setErrorFila(mensajeError(x)) },
                          );
                        }}
                      >
                        {t.activo ? 'Desactivar' : 'Reactivar'}
                      </Button>
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </ListadoModulo>
    </div>
  );
}
