import { useState } from 'react';
import { FlaskConical, TriangleAlert } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Skeleton } from '@/components/ui/skeleton';
import { esApiError } from '@/lib/api';
import { formatDateTime, hoyLocalISO } from '@/lib/datetime';
import {
  useConfirmarMovimientoPrueba, useMatrizEfectiva, useMovimientosPrueba, useSucursalesMovimiento, useTiposDocumento,
  useValidarMovimientoDimensiones,
} from '../api/dimensiones';
import type {
  Auxiliar, CampoMovimiento, CentroOpcion, Dimension, ErrorDimension, MovimientoBody, MovimientoPrueba, Requerimiento,
} from '../api/dimensiones-types';
import { AuxiliarSelector } from '../components/dimensiones/AuxiliarSelector';
import { CentroSelector } from '../components/dimensiones/CentroSelector';
import { CuentaSelector, type CuentaOpcion } from '../components/dimensiones/CuentaSelector';
import {
  ETIQUETA_DIMENSION, ETIQUETA_REQUERIMIENTO, VARIANTE_REQUERIMIENTO, fechaCorta, mensajeError, origenRequerimiento,
} from '../lib/dimensiones';
import { SELECT_CLASS } from '../lib/estilos';

type Resultado =
  | { tipo: 'valido' }
  | { tipo: 'registrado'; movimiento: MovimientoPrueba }
  | { tipo: 'errores'; errores: ErrorDimension[] }
  | { tipo: 'fallo'; mensaje: string };

const LIMITE = 10;

/**
 * F1-CON-02: captura de un movimiento de PRUEBA (aún no hay pólizas). Valida cuenta, tipo de documento, sucursal, centros y
 * reglas vigentes a la fecha contable; explica junto a cada campo qué dimensión falta o por qué la combinación no es válida.
 */
export function MovimientosPruebaPage() {
  const sucursales = useSucursalesMovimiento();
  const tipos = useTiposDocumento();
  const validar = useValidarMovimientoDimensiones();
  const confirmar = useConfirmarMovimientoPrueba();

  const [sucursalId, setSucursalId] = useState('');
  const [tipoId, setTipoId] = useState('');
  const [cuenta, setCuenta] = useState<CuentaOpcion | null>(null);
  const [fecha, setFecha] = useState(hoyLocalISO());
  const [dim1, setDim1] = useState<CentroOpcion | null>(null);
  const [dim2, setDim2] = useState<CentroOpcion | null>(null);
  const [dim3, setDim3] = useState<CentroOpcion | null>(null);
  const [proyecto, setProyecto] = useState('');
  const [cliente, setCliente] = useState<Auxiliar | null>(null);
  const [proveedor, setProveedor] = useState<Auxiliar | null>(null);
  const [banco, setBanco] = useState<Auxiliar | null>(null);
  const [referencia, setReferencia] = useState('');
  const [resultado, setResultado] = useState<Resultado | null>(null);

  const matriz = useMatrizEfectiva(cuenta?.id ?? '', tipoId, fecha);
  const requerimiento = (d: Dimension): Requerimiento | undefined => matriz.data?.find((r) => r.dimension === d)?.requerimiento;
  const errores = resultado?.tipo === 'errores' ? resultado.errores : [];
  const insignia = (d: Dimension) => {
    const req = requerimiento(d);
    return req && <Badge variant={VARIANTE_REQUERIMIENTO[req]}>{ETIQUETA_REQUERIMIENTO[req]}</Badge>;
  };
  const errorDe = (campo: CampoMovimiento) => errores.filter((e) => e.campo === campo);

  function limpiarResultado<T>(set: (v: T) => void) {
    return (v: T) => { set(v); setResultado(null); };
  }

  function cambiarSucursal(id: string) {
    // Los centros dependen de la sucursal: al cambiarla se limpian (un centro de otra sucursal no es seleccionable).
    setSucursalId(id); setDim1(null); setDim2(null); setDim3(null); setResultado(null);
  }

  function cambiarDim2(c: CentroOpcion | null) {
    setDim2(c);
    if (dim3 && c && dim3.dim2Id !== c.id) setDim3(null);
    setResultado(null);
  }

  const incompleto = !sucursalId || !tipoId || !cuenta || !fecha;
  const motivoIncompleto = 'Elige sucursal, tipo de documento, cuenta y fecha contable';

  function cuerpo(): MovimientoBody {
    return {
      cuentaId: cuenta!.id, tipoDocumentoId: tipoId, fechaContable: fecha, sucursalId,
      dim1Id: dim1?.id ?? null, dim2Id: dim2?.id ?? null, dim3Id: dim3?.id ?? null,
      proyecto: proyecto.trim() || null, clienteId: cliente?.id ?? null, proveedorId: proveedor?.id ?? null,
      cuentaBancariaId: banco?.id ?? null,
      origen: 'Manual', referencia: referencia.trim() || null,
    };
  }

  function onValidar() {
    validar.mutate(cuerpo(), {
      onSuccess: (v) => setResultado(v.valido ? { tipo: 'valido' } : { tipo: 'errores', errores: v.errores }),
      onError: (e) => setResultado({ tipo: 'fallo', mensaje: mensajeError(e) }),
    });
  }

  function onRegistrar() {
    confirmar.mutate(cuerpo(), {
      onSuccess: (m) => setResultado({ tipo: 'registrado', movimiento: m }),
      onError: (e) => {
        const lista = esApiError(e) && e.code === 'CONTAB_DIM_MOVIMIENTO_INVALIDO' ? (e.problem.errores as ErrorDimension[] | undefined) : undefined;
        setResultado(lista ? { tipo: 'errores', errores: lista } : { tipo: 'fallo', mensaje: mensajeError(e) });
      },
    });
  }

  const pendiente = validar.isPending || confirmar.isPending;

  return (
    <div className="flex flex-col gap-4 px-6 py-5">
      <header>
        <h1 className="flex items-center gap-2 text-3xl font-semibold">
          <FlaskConical className="size-5 text-brand" strokeWidth={1.6} aria-hidden="true" />
          Probar movimientos
        </h1>
        <p className="text-sm text-ink-muted">Valida una combinación de cuenta, tipo de documento y centros contra las reglas vigentes y registra movimientos de prueba.</p>
      </header>

      <p role="note" className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg">
        <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
        <span>Los movimientos de esta pantalla son de prueba: no son pólizas ni afectan saldos. Las reglas definitivas las confirma Contabilidad de Millet.</span>
      </p>

      <section aria-label="Capturar movimiento" className="rounded-lg bg-surface-card p-4 shadow-card-flat">
        <div className="grid grid-cols-1 gap-x-4 gap-y-3.5 md:grid-cols-2 xl:grid-cols-4">
          <Campo id="mov-sucursal" label="Sucursal" errores={errorDe('sucursalId')}>
            <select id="mov-sucursal" className={SELECT_CLASS} value={sucursalId} onChange={(e) => cambiarSucursal(e.target.value)} aria-describedby="mov-sucursal-err">
              <option value="">{sucursales.isLoading ? 'Cargando…' : 'Elegir sucursal'}</option>
              {(sucursales.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.clave} — {s.nombre}</option>)}
            </select>
            {sucursales.data?.length === 0 && <span className="text-xs text-ink-muted">No tienes sucursales asignadas; pide acceso a tu administrador.</span>}
          </Campo>
          <Campo id="mov-tipo" label="Tipo de documento" errores={errorDe('tipoDocumentoId')}>
            <select id="mov-tipo" className={SELECT_CLASS} value={tipoId} onChange={(e) => limpiarResultado(setTipoId)(e.target.value)} aria-describedby="mov-tipo-err">
              <option value="">Elegir tipo</option>
              {(tipos.data ?? []).map((t) => <option key={t.id} value={t.id}>{t.clave} — {t.nombre}</option>)}
            </select>
          </Campo>
          <Campo id="mov-cuenta" label="Cuenta" errores={errorDe('cuentaId')}>
            <CuentaSelector id="mov-cuenta" value={cuenta} onChange={limpiarResultado(setCuenta)} soloAfectables />
          </Campo>
          <Campo id="mov-fecha" label="Fecha contable">
            <Input id="mov-fecha" type="date" value={fecha} onChange={(e) => limpiarResultado(setFecha)(e.target.value)} />
          </Campo>
          {(['Dim1', 'Dim2', 'Dim3'] as const).map((d) => {
            const campo = (d === 'Dim1' ? 'dim1Id' : d === 'Dim2' ? 'dim2Id' : 'dim3Id') as CampoMovimiento;
            const value = d === 'Dim1' ? dim1 : d === 'Dim2' ? dim2 : dim3;
            const set = d === 'Dim1' ? limpiarResultado(setDim1) : d === 'Dim2' ? cambiarDim2 : limpiarResultado(setDim3);
            return (
              <Campo
                key={d}
                id={`mov-${campo}`}
                label={ETIQUETA_DIMENSION[d]}
                errores={errorDe(campo)}
                extra={insignia(d)}
                ayuda={d === 'Dim3' && dim3?.dim2Clave ? `Pertenece al CeCo ${dim3.dim2Clave}` : undefined}
              >
                <CentroSelector
                  id={`mov-${campo}`} nivel={d} sucursalId={sucursalId} dim2Id={d === 'Dim3' ? dim2?.id : undefined}
                  value={value} onChange={set} invalido={errorDe(campo).length > 0} describedBy={`mov-${campo}-err`}
                />
              </Campo>
            );
          })}
          <Campo id="mov-proyecto" label={ETIQUETA_DIMENSION.Proyecto} errores={errorDe('proyecto')} extra={insignia('Proyecto')}>
            <Input
              id="mov-proyecto" maxLength={40} value={proyecto} placeholder="[CLAVE DE PROYECTO]"
              aria-invalid={errorDe('proyecto').length > 0 || undefined} aria-describedby="mov-proyecto-err"
              onChange={(e) => limpiarResultado(setProyecto)(e.target.value)}
            />
          </Campo>
          {([
            ['Cliente', 'clienteId', cliente, setCliente],
            ['Proveedor', 'proveedorId', proveedor, setProveedor],
            ['Banco', 'cuentaBancariaId', banco, setBanco],
          ] as const).map(([tipo, campo, value, set]) => (
            <Campo key={tipo} id={`mov-${campo}`} label={ETIQUETA_DIMENSION[tipo]} errores={errorDe(campo)} extra={insignia(tipo)}>
              <AuxiliarSelector
                id={`mov-${campo}`} tipo={tipo} value={value} onChange={limpiarResultado(set)}
                invalido={errorDe(campo).length > 0} describedBy={`mov-${campo}-err`}
              />
            </Campo>
          ))}
          <Campo id="mov-ref" label="Referencia" opcional>
            <Input id="mov-ref" maxLength={100} value={referencia} onChange={(e) => setReferencia(e.target.value)} placeholder="[REFERENCIA]" />
          </Campo>
        </div>

        {cuenta && matriz.data && (
          <ul className="mt-3 flex flex-col gap-0.5 text-xs text-ink-muted" aria-label="Origen de cada requerimiento">
            {matriz.data.map((r) => <li key={r.dimension}>{ETIQUETA_DIMENSION[r.dimension]}: {origenRequerimiento(r)}.</li>)}
          </ul>
        )}

        <div className="mt-4 flex flex-wrap items-center justify-end gap-2 border-t border-line-divider pt-4">
          {incompleto && <span className="mr-auto text-xs text-ink-muted">{motivoIncompleto}.</span>}
          <Button variant="outline" size="lg" disabled={incompleto || pendiente} title={incompleto ? motivoIncompleto : undefined} onClick={onValidar}>
            {validar.isPending ? 'Validando…' : 'Validar'}
          </Button>
          <Button size="lg" disabled={incompleto || pendiente} title={incompleto ? motivoIncompleto : undefined} onClick={onRegistrar}>
            {confirmar.isPending ? 'Registrando…' : 'Registrar movimiento de prueba'}
          </Button>
        </div>

        <ResultadoMovimiento resultado={resultado} />
      </section>

      <MovimientosRegistrados />
    </div>
  );
}

function Campo({
  id, label, errores = [], children, extra, ayuda, opcional,
}: {
  id: string; label: string; errores?: ErrorDimension[]; children: React.ReactNode; extra?: React.ReactNode; ayuda?: string; opcional?: boolean;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <span className="flex items-center justify-between gap-2">
        <Label htmlFor={id}>{label}{opcional && <span className="font-normal text-ink-muted"> (opcional)</span>}</Label>
        {extra}
      </span>
      {children}
      {ayuda && <span className="text-xs text-ink-muted">{ayuda}</span>}
      <span id={`${id}-err`}>
        {errores.map((e) => <span key={e.codigo + e.mensaje} className="block text-xs text-danger-fg">{e.mensaje}</span>)}
      </span>
    </div>
  );
}

function ResultadoMovimiento({ resultado }: { resultado: Resultado | null }) {
  if (!resultado) return null;
  if (resultado.tipo === 'fallo') return <p role="alert" className="mt-3 rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">{resultado.mensaje}</p>;
  if (resultado.tipo === 'errores')
    return (
      <div role="alert" className="mt-3 rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
        <p className="font-medium">La combinación no es válida:</p>
        <ul className="mt-1 list-disc pl-5">{resultado.errores.map((e) => <li key={e.codigo + e.mensaje}>{e.mensaje}</li>)}</ul>
      </div>
    );
  return (
    <p role="status" className="mt-3 flex items-center gap-2 rounded-md bg-success-bg px-3 py-2.5 text-sm text-success-fg">
      {resultado.tipo === 'valido'
        ? 'La combinación es válida: cumple las reglas vigentes a la fecha contable.'
        : `Movimiento de prueba registrado con las reglas vigentes al ${fechaCorta(resultado.movimiento.fechaContable)}.`}
    </p>
  );
}

function centros(m: MovimientoPrueba): string {
  return [m.dim1, m.dim2, m.dim3].filter(Boolean).map((c) => `${c!.clave}${c!.activo ? '' : ' (baja)'}`).join(' · ') || 'Sin centros';
}

function otras(m: MovimientoPrueba): string {
  return [
    m.proyecto && `Proyecto ${m.proyecto}`,
    m.cliente && `Cliente ${m.cliente.clave}`,
    m.proveedor && `Proveedor ${m.proveedor.clave}`,
    m.cuentaBancaria && `Cuenta ${m.cuentaBancaria.clave}`,
  ].filter(Boolean).join(' · ');
}

function MovimientosRegistrados() {
  const [offset, setOffset] = useState(0);
  const movs = useMovimientosPrueba(offset, LIMITE);
  return (
    <section aria-label="Movimientos de prueba registrados" className="overflow-hidden rounded-lg bg-surface-card shadow-card-flat">
      <h2 className="border-b border-line-divider px-4 py-3 text-lg font-semibold">Movimientos de prueba registrados</h2>
      {movs.isLoading ? (
        <div className="p-4"><Skeleton className="h-8 w-full" /></div>
      ) : movs.isError ? (
        <p role="alert" className="p-4 text-sm text-danger-fg">No se pudieron cargar los movimientos.</p>
      ) : movs.data!.items.length === 0 ? (
        <p className="p-6 text-sm text-ink-muted">Aún no hay movimientos de prueba.</p>
      ) : (
        <>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[880px] text-sm">
              <thead className="border-b border-line-divider bg-surface-subtle text-left text-2xs font-semibold uppercase tracking-[0.04em] text-ink-muted">
                <tr>
                  <th className="px-3 py-2">Fecha contable</th><th className="px-3 py-2">Cuenta</th><th className="px-3 py-2">Tipo</th>
                  <th className="px-3 py-2">Sucursal</th><th className="px-3 py-2">Dimensiones</th><th className="px-3 py-2">Reglas con las que se validó</th>
                  <th className="px-3 py-2">Registró</th>
                </tr>
              </thead>
              <tbody>
                {movs.data!.items.map((m) => (
                  <tr key={m.id} className="border-b border-line-row align-top">
                    <td className="px-3 py-2 text-ink-secondary">{fechaCorta(m.fechaContable)}</td>
                    <td className="px-3 py-2 font-mono text-xs">{m.cuentaCodigo}</td>
                    <td className="px-3 py-2 font-mono text-xs">{m.tipoDocumentoClave}</td>
                    <td className="px-3 py-2">{m.sucursalNombre ?? '[SUCURSAL]'}</td>
                    <td className="px-3 py-2 font-mono text-xs">
                      {centros(m)}
                      {otras(m) && <span className="block font-sans text-ink-muted">{otras(m)}</span>}
                    </td>
                    <td className="px-3 py-2">
                      <details>
                        <summary className="cursor-pointer text-brand">Ver reglas</summary>
                        <ul className="mt-1 flex flex-col gap-1 text-xs">
                          {m.reglasAplicadas.map((r) => (
                            <li key={r.dimension}>
                              <span className="font-medium">{ETIQUETA_DIMENSION[r.dimension]}:</span> {ETIQUETA_REQUERIMIENTO[r.requerimiento]}
                              <span className="block text-ink-muted">{origenRequerimiento(r)}</span>
                            </li>
                          ))}
                        </ul>
                      </details>
                    </td>
                    <td className="px-3 py-2 text-xs text-ink-muted">{m.confirmadoPor ?? '[USUARIO]'}<span className="block">{formatDateTime(m.confirmadoEn)}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <div className="flex h-11 items-center justify-between border-t border-line-divider px-4 text-xs text-ink-muted" data-print="hidden">
            <span>{offset + 1}–{offset + movs.data!.items.length} de {movs.data!.total}</span>
            <span className="flex gap-2">
              <Button variant="outline" size="sm" disabled={offset === 0} onClick={() => setOffset(Math.max(0, offset - LIMITE))}>Anterior</Button>
              <Button variant="outline" size="sm" disabled={offset + movs.data!.items.length >= movs.data!.total} onClick={() => setOffset(offset + LIMITE)}>Siguiente</Button>
            </span>
          </div>
        </>
      )}
    </section>
  );
}
