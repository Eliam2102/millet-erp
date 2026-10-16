import { useEffect, useMemo, useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { cn } from '@/lib/utils';
import { useSucursalesMovimiento, useTiposDocumento } from '../../api/dimensiones';
import type { Auxiliar, CentroOpcion, CentroRef, DimensionCentro } from '../../api/dimensiones-types';
import { useCrearPoliza, useEditarPoliza } from '../../api/polizas';
import type { PartidaRequest, Poliza, PolizaManualRequest } from '../../api/polizas-types';
import { ETIQUETA_DIMENSION } from '../../lib/dimensiones';
import { SELECT_CLASS } from '../../lib/estilos';
import { centavos, erroresPorCampo, mensajePoliza } from '../../lib/polizas';
import { AuxiliarSelector } from '../dimensiones/AuxiliarSelector';
import { CentroSelector } from '../dimensiones/CentroSelector';
import { CuentaSelector, type CuentaOpcion } from '../dimensiones/CuentaSelector';
import { CuadrePoliza } from './CuadrePoliza';

interface PartidaForm {
  key: string;
  cuenta: CuentaOpcion | null;
  concepto: string;
  sucursalId: string;
  dim1: CentroOpcion | null;
  dim2: CentroOpcion | null;
  dim3: CentroOpcion | null;
  proyecto: string;
  cliente: Auxiliar | null;
  proveedor: Auxiliar | null;
  banco: Auxiliar | null;
  cargo: string;
  abono: string;
}

const MAX_PARTIDAS = 500;

function partidaVacia(sucursalId = ''): PartidaForm {
  return {
    key: crypto.randomUUID(), cuenta: null, concepto: '', sucursalId, dim1: null, dim2: null, dim3: null,
    proyecto: '', cliente: null, proveedor: null, banco: null, cargo: '', abono: '',
  };
}

/** El detalle trae la referencia mínima del centro; el selector solo pinta clave y nombre. */
function centroDe(ref: CentroRef | null, nivel: DimensionCentro): CentroOpcion | null {
  return ref ? { id: ref.id, nivel, clave: ref.clave, nombre: ref.nombre, dim1Id: '', dim2Id: null, dim1Clave: '', dim2Clave: null } : null;
}

function inicial(p?: Poliza) {
  if (!p) {
    return { tipoId: '', fecha: hoyLocalISO(), ajuste: false, concepto: '', partidas: [partidaVacia(), partidaVacia()] };
  }
  return {
    tipoId: p.tipoOperacionId,
    fecha: p.fecha,
    ajuste: p.periodoNumero === 13,
    concepto: p.concepto,
    partidas: p.partidas.map<PartidaForm>((x) => ({
      key: x.id,
      cuenta: { id: x.cuentaId, codigo: x.cuentaCodigo, nombre: x.cuentaNombre ?? '' },
      concepto: x.concepto ?? '',
      sucursalId: x.sucursalId,
      dim1: centroDe(x.dim1, 'Dim1'),
      dim2: centroDe(x.dim2, 'Dim2'),
      dim3: centroDe(x.dim3, 'Dim3'),
      proyecto: x.proyecto ?? '',
      cliente: x.cliente,
      proveedor: x.proveedor,
      banco: x.cuentaBancaria,
      cargo: x.cargo ? String(x.cargo) : '',
      abono: x.abono ? String(x.abono) : '',
    })),
  };
}

function numero(s: string): number {
  const n = Number(s.replace(/,/g, ''));
  return Number.isFinite(n) ? n : 0;
}

function anioDe(fecha: string): number {
  const n = Number(fecha.slice(0, 4));
  return Number.isFinite(n) && n > 1999 ? n : new Date().getFullYear();
}

interface Props {
  /** Póliza en borrador a editar; sin ella se captura una nueva. */
  poliza?: Poliza;
  onGuardada: (p: Poliza) => void;
  onCancelar: () => void;
  onDirtyChange?: (dirty: boolean) => void;
}

/**
 * Captura de póliza manual (C1.3): encabezado + partidas con cuenta, sucursal y centros dependientes de la sucursal.
 * El borrador puede guardarse sin cuadrar; el indicador en vivo muestra cargos, abonos y diferencia (se exige al autorizar).
 */
export function PolizaForm({ poliza, onGuardada, onCancelar, onDirtyChange }: Props) {
  const tipos = useTiposDocumento();
  const sucursales = useSucursalesMovimiento();
  const crear = useCrearPoliza();
  const editar = useEditarPoliza();
  // El usuario corrige el borrador tras un rechazo: body distinto ⇒ key nueva; reintento idéntico ⇒ misma key.
  const keyFor = useBodyScopedIdempotencyKey();

  const base = useMemo(() => inicial(poliza), [poliza]);
  const [tipoId, setTipoId] = useState(base.tipoId);
  const [fecha, setFecha] = useState(base.fecha);
  const [ajuste, setAjuste] = useState(base.ajuste);
  const [concepto, setConcepto] = useState(base.concepto);
  const [partidas, setPartidas] = useState<PartidaForm[]>(base.partidas);
  const [errores, setErrores] = useState<Record<string, string[]>>({});
  const [mensaje, setMensaje] = useState('');

  const snapshot = JSON.stringify({ tipoId, fecha, ajuste, concepto, partidas: partidas.map((p) => ({ ...p, key: '' })) });
  const [inicialSnapshot] = useState(snapshot);
  const dirty = snapshot !== inicialSnapshot;
  useEffect(() => { onDirtyChange?.(dirty); }, [dirty, onDirtyChange]);

  const totalCargos = centavos(partidas.reduce((s, p) => s + numero(p.cargo), 0));
  const totalAbonos = centavos(partidas.reduce((s, p) => s + numero(p.abono), 0));

  // Un tipo inactivo (o de prueba fuera de la lista activa) se conserva visible al editar.
  const opcionesTipo = tipos.data ?? [];
  const tipoActualFuera = poliza && !opcionesTipo.some((t) => t.id === poliza.tipoOperacionId);

  function actualizar(i: number, cambio: Partial<PartidaForm>) {
    setPartidas((ps) => ps.map((p, j) => (j === i ? { ...p, ...cambio } : p)));
  }

  function cambiarSucursal(i: number, sucursalId: string) {
    // Los centros dependen de la sucursal: al cambiarla se limpian (un centro de otra sucursal no es seleccionable).
    actualizar(i, { sucursalId, dim1: null, dim2: null, dim3: null });
  }

  function cambiarDim2(i: number, c: CentroOpcion | null) {
    const p = partidas[i];
    actualizar(i, { dim2: c, dim3: p.dim3 && c && p.dim3.dim2Id !== c.id ? null : p.dim3 });
  }

  function cambiarImporte(i: number, lado: 'cargo' | 'abono', valor: string) {
    // Una partida va de un solo lado: capturar un importe limpia el otro.
    const otro = lado === 'cargo' ? 'abono' : 'cargo';
    actualizar(i, numero(valor) !== 0 ? { [lado]: valor, [otro]: '' } : { [lado]: valor });
  }

  function agregar() {
    setPartidas((ps) => [...ps, partidaVacia(ps[ps.length - 1]?.sucursalId ?? '')]);
  }

  function quitar(i: number) {
    setPartidas((ps) => ps.filter((_, j) => j !== i));
    setErrores({});
  }

  function cambiarAjuste(v: boolean) {
    setAjuste(v);
    if (v) setFecha(`${anioDe(fecha)}-12-31`);
  }

  const faltante = !tipoId ? 'Elige el tipo de operación' : !fecha ? 'Indica la fecha' : !concepto.trim() ? 'Escribe el concepto' : '';
  const pendiente = crear.isPending || editar.isPending;

  function cuerpo(): PolizaManualRequest {
    return {
      tipoOperacionId: tipoId,
      fecha,
      concepto: concepto.trim(),
      periodoNumero: ajuste ? 13 : null,
      documentoOrigen: null,
      partidas: partidas.map<PartidaRequest>((p) => ({
        cuentaId: p.cuenta?.id ?? '',
        concepto: p.concepto.trim() || null,
        cargo: numero(p.cargo),
        abono: numero(p.abono),
        sucursalId: p.sucursalId,
        dim1Id: p.dim1?.id ?? null,
        dim2Id: p.dim2?.id ?? null,
        dim3Id: p.dim3?.id ?? null,
        proyecto: p.proyecto.trim() || null,
        clienteId: p.cliente?.id ?? null,
        proveedorId: p.proveedor?.id ?? null,
        cuentaBancariaId: p.banco?.id ?? null,
      })),
    };
  }

  function guardar(e: React.FormEvent) {
    e.preventDefault();
    if (faltante) return;
    setErrores({});
    setMensaje('');
    const opciones = {
      onSuccess: (p: Poliza) => onGuardada(p),
      onError: (x: unknown) => {
        setErrores(erroresPorCampo(x));
        setMensaje(mensajePoliza(x));
      },
    };
    const body = cuerpo();
    const idempotencyKey = keyFor(body);
    if (poliza) editar.mutate({ id: poliza.id, version: poliza.version, body, idempotencyKey }, opciones);
    else crear.mutate({ body, idempotencyKey }, opciones);
  }

  const err = (campo: string) => errores[campo] ?? [];
  const errPartida = (i: number, campo: string) => err(`partidas[${i}].${campo}`);
  /** Errores de la partida que no corresponden a un campo visible (p. ej. «cargo o abono»). */
  const erroresGenerales = (i: number) =>
    Object.entries(errores)
      .filter(([k]) => k === `partidas[${i}]`)
      .flatMap(([, v]) => v);

  return (
    <form onSubmit={guardar} className="flex flex-col gap-5" aria-label={poliza ? 'Editar póliza' : 'Nueva póliza'} noValidate>
      <fieldset className="flex flex-col gap-3.5">
        <legend className="mb-2 text-sm font-semibold text-ink">1. Encabezado</legend>
        <div className="grid grid-cols-1 gap-x-4 gap-y-3.5 sm:grid-cols-2">
          <Campo id="pol-tipo" label="Tipo de operación" errores={err('tipoOperacionId')}>
            <select
              id="pol-tipo" className={SELECT_CLASS} value={tipoId} disabled={!!poliza}
              title={poliza ? 'El tipo de operación no se cambia: descarta el borrador y captura otro' : undefined}
              aria-invalid={err('tipoOperacionId').length > 0 || undefined} aria-describedby="pol-tipo-err"
              onChange={(e) => setTipoId(e.target.value)}
            >
              <option value="">{tipos.isLoading ? 'Cargando…' : 'Elegir tipo'}</option>
              {tipoActualFuera && <option value={poliza.tipoOperacionId}>{poliza.tipoClave} — {poliza.tipoNombre ?? ''}</option>}
              {opcionesTipo.map((t) => (
                <option key={t.id} value={t.id}>{t.clave} — {t.nombre}{t.tipoSat ? ` (${t.tipoSat})` : ''}</option>
              ))}
            </select>
          </Campo>
          <Campo id="pol-fecha" label="Fecha" errores={err('fecha')}>
            <Input
              id="pol-fecha" type="date" value={fecha} disabled={ajuste}
              title={ajuste ? 'El periodo 13 de ajustes usa el 31 de diciembre del ejercicio' : undefined}
              aria-invalid={err('fecha').length > 0 || undefined} aria-describedby="pol-fecha-err"
              onChange={(e) => setFecha(e.target.value)}
            />
          </Campo>
          <div className="flex flex-col gap-1.5 sm:col-span-2">
            <span className="flex items-center gap-2">
              <Checkbox id="pol-ajuste" checked={ajuste} onCheckedChange={(v) => cambiarAjuste(v === true)} />
              <Label htmlFor="pol-ajuste" className="font-normal">Periodo 13 (ajustes de auditoría)</Label>
            </span>
            {ajuste && (
              <span className="flex flex-wrap items-center gap-2 text-xs text-ink-muted">
                <Label htmlFor="pol-ejercicio" className="text-xs font-normal">Ejercicio</Label>
                <Input
                  id="pol-ejercicio" type="number" min={2000} max={2100} className="h-ctl-md w-24" value={anioDe(fecha)}
                  onChange={(e) => setFecha(`${e.target.value}-12-31`)}
                />
                <span>La póliza se registra al 31 de diciembre en el periodo de ajustes.</span>
              </span>
            )}
            {err('periodoNumero').map((m) => <span key={m} className="text-xs text-danger-fg">{m}</span>)}
          </div>
          <Campo id="pol-concepto" label="Concepto" errores={err('concepto')} className="sm:col-span-2">
            <Textarea
              id="pol-concepto" rows={2} maxLength={300} className="resize-none" value={concepto}
              aria-invalid={err('concepto').length > 0 || undefined} aria-describedby="pol-concepto-err"
              onChange={(e) => setConcepto(e.target.value)} placeholder="[CONCEPTO DE LA PÓLIZA]"
            />
          </Campo>
        </div>
      </fieldset>

      <fieldset className="flex flex-col gap-3">
        <legend className="mb-2 text-sm font-semibold text-ink">2. Partidas</legend>
        {err('partidas').map((m) => <p key={m} className="text-xs text-danger-fg">{m}</p>)}
        <ol className="flex flex-col gap-3" aria-label="Partidas de la póliza">
          {partidas.map((p, i) => {
            const id = (c: string) => `pol-p${i}-${c}`;
            const generales = erroresGenerales(i);
            return (
              <li key={p.key} className="rounded-lg bg-surface-card p-3 shadow-card-flat" aria-label={`Partida ${i + 1}`}>
                <div className="mb-2 flex items-center justify-between">
                  <span className="text-xs font-semibold text-ink-muted">Partida {i + 1}</span>
                  <Button
                    type="button" variant="ghost" size="icon" aria-label={`Quitar partida ${i + 1}`}
                    disabled={partidas.length <= 2} title={partidas.length <= 2 ? 'La póliza lleva al menos dos partidas' : undefined}
                    onClick={() => quitar(i)}
                  >
                    <Trash2 className="size-4" strokeWidth={1.6} aria-hidden="true" />
                  </Button>
                </div>
                <div className="grid grid-cols-1 gap-x-3 gap-y-3 md:grid-cols-6">
                  <Campo id={id('cuenta')} label="Cuenta" errores={errPartida(i, 'cuentaId')} className="md:col-span-3">
                    <CuentaSelector id={id('cuenta')} value={p.cuenta} onChange={(c) => actualizar(i, { cuenta: c })} soloAfectables />
                  </Campo>
                  <Campo id={id('sucursal')} label="Sucursal" errores={errPartida(i, 'sucursalId')} className="md:col-span-3">
                    <select
                      id={id('sucursal')} className={SELECT_CLASS} value={p.sucursalId}
                      aria-invalid={errPartida(i, 'sucursalId').length > 0 || undefined} aria-describedby={`${id('sucursal')}-err`}
                      onChange={(e) => cambiarSucursal(i, e.target.value)}
                    >
                      <option value="">{sucursales.isLoading ? 'Cargando…' : 'Elegir sucursal'}</option>
                      {(sucursales.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.clave} — {s.nombre}</option>)}
                    </select>
                  </Campo>
                  {(['Dim1', 'Dim2', 'Dim3'] as const).map((d) => {
                    const campo = d === 'Dim1' ? 'dim1Id' : d === 'Dim2' ? 'dim2Id' : 'dim3Id';
                    const value = d === 'Dim1' ? p.dim1 : d === 'Dim2' ? p.dim2 : p.dim3;
                    const set = (c: CentroOpcion | null) =>
                      d === 'Dim1' ? actualizar(i, { dim1: c }) : d === 'Dim2' ? cambiarDim2(i, c) : actualizar(i, { dim3: c });
                    return (
                      <Campo key={d} id={id(campo)} label={ETIQUETA_DIMENSION[d]} errores={errPartida(i, campo)} className="md:col-span-2">
                        <CentroSelector
                          id={id(campo)} nivel={d} sucursalId={p.sucursalId} dim2Id={d === 'Dim3' ? p.dim2?.id : undefined}
                          value={value} onChange={set} invalido={errPartida(i, campo).length > 0} describedBy={`${id(campo)}-err`}
                        />
                      </Campo>
                    );
                  })}
                  <Campo id={id('concepto')} label="Concepto" opcional errores={errPartida(i, 'concepto')} className="md:col-span-2">
                    <Input id={id('concepto')} maxLength={200} value={p.concepto} onChange={(e) => actualizar(i, { concepto: e.target.value })} />
                  </Campo>
                  <Campo id={id('cargo')} label="Cargo" errores={errPartida(i, 'cargo')} className="md:col-span-2">
                    <Input
                      id={id('cargo')} inputMode="decimal" className="text-right tabular-nums" value={p.cargo} placeholder="0.00"
                      aria-invalid={errPartida(i, 'cargo').length > 0 || undefined} aria-describedby={`${id('cargo')}-err`}
                      onChange={(e) => cambiarImporte(i, 'cargo', e.target.value)}
                    />
                  </Campo>
                  <Campo id={id('abono')} label="Abono" errores={errPartida(i, 'abono')} className="md:col-span-2">
                    <Input
                      id={id('abono')} inputMode="decimal" className="text-right tabular-nums" value={p.abono} placeholder="0.00"
                      aria-invalid={errPartida(i, 'abono').length > 0 || undefined} aria-describedby={`${id('abono')}-err`}
                      onChange={(e) => cambiarImporte(i, 'abono', e.target.value)}
                    />
                  </Campo>
                </div>
                <details className="mt-2" open={['proyecto', 'clienteId', 'proveedorId', 'cuentaBancariaId'].some((c) => errPartida(i, c).length > 0) || undefined}>
                  <summary className="cursor-pointer text-xs text-brand">Más dimensiones (proyecto, cliente, proveedor, banco)</summary>
                  <div className="mt-2 grid grid-cols-1 gap-x-3 gap-y-3 md:grid-cols-2">
                    <Campo id={id('proyecto')} label={ETIQUETA_DIMENSION.Proyecto} errores={errPartida(i, 'proyecto')}>
                      <Input
                        id={id('proyecto')} maxLength={40} value={p.proyecto} placeholder="[CLAVE DE PROYECTO]"
                        aria-invalid={errPartida(i, 'proyecto').length > 0 || undefined} aria-describedby={`${id('proyecto')}-err`}
                        onChange={(e) => actualizar(i, { proyecto: e.target.value })}
                      />
                    </Campo>
                    {([
                      ['Cliente', 'clienteId', p.cliente, (a: Auxiliar | null) => actualizar(i, { cliente: a })],
                      ['Proveedor', 'proveedorId', p.proveedor, (a: Auxiliar | null) => actualizar(i, { proveedor: a })],
                      ['Banco', 'cuentaBancariaId', p.banco, (a: Auxiliar | null) => actualizar(i, { banco: a })],
                    ] as const).map(([tipo, campo, value, set]) => (
                      <Campo key={tipo} id={id(campo)} label={ETIQUETA_DIMENSION[tipo]} errores={errPartida(i, campo)}>
                        <AuxiliarSelector
                          id={id(campo)} tipo={tipo} value={value} onChange={set}
                          invalido={errPartida(i, campo).length > 0} describedBy={`${id(campo)}-err`}
                        />
                      </Campo>
                    ))}
                  </div>
                </details>
                {generales.map((m) => <p key={m} className="mt-2 text-xs text-danger-fg">{m}</p>)}
              </li>
            );
          })}
        </ol>
        <Button
          type="button" variant="outline" className="self-start border-dashed" onClick={agregar}
          disabled={partidas.length >= MAX_PARTIDAS} title={partidas.length >= MAX_PARTIDAS ? 'La póliza lleva hasta 500 partidas' : undefined}
        >
          <Plus className="mr-1 size-3.5" aria-hidden="true" />Agregar partida
        </Button>
      </fieldset>

      <CuadrePoliza totalCargos={totalCargos} totalAbonos={totalAbonos} nota="El borrador puede guardarse sin cuadrar; para autorizar, cargos y abonos deben ser iguales y distintos de cero." />

      {mensaje && <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">{mensaje}</p>}

      <div className="sticky bottom-0 -mx-4 flex flex-wrap items-center justify-end gap-2 border-t border-line-divider bg-surface-card px-4 py-3">
        <Button type="button" variant="ghost" size="lg" className="mr-auto" onClick={onCancelar}>Cancelar</Button>
        {faltante && <span className="text-xs text-ink-muted">{faltante}.</span>}
        <Button type="submit" size="lg" disabled={!!faltante || pendiente} title={faltante || undefined}>
          {pendiente ? 'Guardando…' : 'Guardar borrador'}
        </Button>
      </div>
    </form>
  );
}

function Campo({
  id, label, errores = [], children, opcional, className,
}: {
  id: string; label: string; errores?: string[]; children: React.ReactNode; opcional?: boolean; className?: string;
}) {
  return (
    <div className={cn('flex min-w-0 flex-col gap-1.5', className)}>
      <Label htmlFor={id}>{label}{opcional && <span className="font-normal text-ink-muted"> (opcional)</span>}</Label>
      {children}
      <span id={`${id}-err`}>
        {errores.map((m) => <span key={m} className="block text-xs text-danger-fg">{m}</span>)}
      </span>
    </div>
  );
}
