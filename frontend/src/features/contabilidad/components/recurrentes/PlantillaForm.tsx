import { SheetBody, SheetFooter } from '@/components/ui/sheet';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';

import { useEffect, useMemo, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { useTiposDocumento } from '../../api/dimensiones';
import type { Auxiliar, CentroRef } from '../../api/dimensiones-types';
import { useCrearPlantilla, useEditarPlantilla, useEtiquetasCuentas } from '../../api/recurrentes';
import {
  ULTIMO_DIA_DEL_MES,
  type PartidaPlantilla,
  type PlantillaRecurrente,
  type PlantillaRecurrenteRequest,
} from '../../api/recurrentes-types';
import { SELECT_CLASS } from '../../lib/estilos';
import {
  centroDe,
  partidaVacia,
  partidasACuerpo,
  totalesPartidas,
  type PartidaForm,
} from '../../lib/partidas';
import { erroresPorCampo, nombrePeriodo } from '../../lib/polizas';
import {
  DIA_MAXIMO,
  NOMBRE_MAXIMO,
  PERIODOS_MAXIMO,
  PERIODOS_MINIMO,
  etiquetaVigencia,
  mensajeRecurrente,
} from '../../lib/recurrentes';
import { CuadrePoliza } from '../polizas/CuadrePoliza';
import { Campo, PartidasEditor } from '../polizas/PartidasEditor';

/** El DTO de la plantilla hoy solo trae ids: si no viene la referencia, el selector muestra un marcador. */
function refCentro(ref: CentroRef | null | undefined, id: string | null): CentroRef | null {
  if (ref) return ref;
  return id ? { id, clave: 'Sin centro de costo', nombre: '', activo: true } : null;
}

function refAuxiliar(
  ref: Auxiliar | null | undefined,
  id: string | null,
  marcador: string,
): Auxiliar | null {
  if (ref) return ref;
  return id ? { id, clave: marcador, nombre: '', activo: true } : null;
}

function partidaDe(x: PartidaPlantilla, i: number): PartidaForm {
  return {
    key: `p${i}-${x.cuentaId}`,
    cuenta: x.cuentaId
      ? { id: x.cuentaId, codigo: x.cuentaCodigo ?? '', nombre: x.cuentaNombre ?? '' }
      : null,
    concepto: x.concepto ?? '',
    sucursalId: x.sucursalId,
    dim1: centroDe(refCentro(x.dim1, x.dim1Id), 'Dim1'),
    dim2: centroDe(refCentro(x.dim2, x.dim2Id), 'Dim2'),
    dim3: centroDe(refCentro(x.dim3, x.dim3Id), 'Dim3'),
    proyecto: x.proyecto ?? '',
    cliente: refAuxiliar(x.cliente, x.clienteId, 'Sin cliente'),
    proveedor: refAuxiliar(x.proveedor, x.proveedorId, 'Sin proveedor'),
    banco: refAuxiliar(x.cuentaBancaria, x.cuentaBancariaId, 'Sin cuenta bancaria'),
    cargo: x.cargo ? String(x.cargo) : '',
    abono: x.abono ? String(x.abono) : '',
  };
}

function inicial(p?: PlantillaRecurrente) {
  if (!p) {
    const hoy = new Date();
    return {
      nombre: '',
      tipoId: '',
      concepto: '',
      anio: String(hoy.getFullYear()),
      mes: String(hoy.getMonth() + 1),
      periodos: '12',
      dia: String(ULTIMO_DIA_DEL_MES),
      reversa: false,
      naceAutorizada: false,
      partidas: [partidaVacia(), partidaVacia()],
    };
  }
  return {
    nombre: p.nombre,
    tipoId: p.tipoOperacionId,
    concepto: p.concepto,
    anio: String(p.anioInicio),
    mes: String(p.mesInicio),
    periodos: String(p.numeroPeriodos),
    dia: String(p.diaDelMes),
    reversa: p.reversaAutomatica,
    naceAutorizada: p.naceAutorizada,
    partidas: p.partidas.length ? p.partidas.map(partidaDe) : [partidaVacia(), partidaVacia()],
  };
}

function entero(s: string): number | null {
  if (!/^\d+$/.test(s.trim())) return null;
  return Number(s.trim());
}

interface Props {
  /** Plantilla en borrador a editar; sin ella se prepara una nueva. */
  plantilla?: PlantillaRecurrente;
  onGuardada: (p: PlantillaRecurrente) => void;
  onCancelar: () => void;
  onDirtyChange?: (dirty: boolean) => void;
}

/**
 * Plantilla de póliza recurrente (K10.4): encabezado, vigencia (inicio, número de periodos y día del mes), reversa
 * automática, si las pólizas nacen autorizadas y las partidas fijas en MXN. A diferencia del borrador de póliza, la
 * plantilla debe cuadrar para guardarse.
 */
export function PlantillaForm({ plantilla, onGuardada, onCancelar, onDirtyChange }: Props) {
  const tipos = useTiposDocumento();
  const crear = useCrearPlantilla();
  const editar = useEditarPlantilla();
  const keyFor = useBodyScopedIdempotencyKey();

  const base = useMemo(() => inicial(plantilla), [plantilla]);
  const [nombre, setNombre] = useState(base.nombre);
  const [tipoId, setTipoId] = useState(base.tipoId);
  const [concepto, setConcepto] = useState(base.concepto);
  const [anio, setAnio] = useState(base.anio);
  const [mes, setMes] = useState(base.mes);
  const [periodos, setPeriodos] = useState(base.periodos);
  const [dia, setDia] = useState(base.dia);
  const [reversa, setReversa] = useState(base.reversa);
  const [naceAutorizada, setNaceAutorizada] = useState(base.naceAutorizada);
  const [partidas, setPartidas] = useState<PartidaForm[]>(base.partidas);
  const [errores, setErrores] = useState<Record<string, string[]>>({});
  const [mensaje, setMensaje] = useState('');

  // Al editar, el DTO trae solo el id de la cuenta: su código y nombre se completan con el catálogo al pintar.
  const etiquetas = useEtiquetasCuentas(plantilla ? plantilla.partidas.map((x) => x.cuentaId) : []);
  const partidasVista = partidas.map((p) => {
    const e = p.cuenta && !p.cuenta.codigo ? etiquetas.get(p.cuenta.id) : undefined;
    return e && p.cuenta ? { ...p, cuenta: { ...p.cuenta, ...e } } : p;
  });

  // La etiqueta de la cuenta no cuenta como cambio: solo su id.
  const snapshot = JSON.stringify({
    nombre,
    tipoId,
    concepto,
    anio,
    mes,
    periodos,
    dia,
    reversa,
    naceAutorizada,
    partidas: partidas.map((p) => ({ ...p, key: '', cuenta: p.cuenta?.id ?? null })),
  });
  const [inicialSnapshot] = useState(snapshot);
  const dirty = snapshot !== inicialSnapshot;
  useEffect(() => {
    onDirtyChange?.(dirty);
  }, [dirty, onDirtyChange]);

  const { totalCargos, totalAbonos } = totalesPartidas(partidas);
  const cuadra = totalCargos === totalAbonos && totalCargos !== 0;

  const opcionesTipo = tipos.data ?? [];
  const tipoActualFuera =
    plantilla && !opcionesTipo.some((t) => t.id === plantilla.tipoOperacionId);

  const anioN = entero(anio);
  const periodosN = entero(periodos);
  const anioInvalido = anioN === null || anioN < 2000 || anioN > 2100;
  const periodosInvalido =
    periodosN === null || periodosN < PERIODOS_MINIMO || periodosN > PERIODOS_MAXIMO;

  const faltante = !nombre.trim()
    ? 'Escribe el nombre de la plantilla'
    : !tipoId
      ? 'Elige el tipo de operación'
      : !concepto.trim()
        ? 'Escribe el concepto'
        : anioInvalido
          ? 'Indica un año de inicio válido'
          : periodosInvalido
            ? `El número de periodos va de ${PERIODOS_MINIMO} a ${PERIODOS_MAXIMO}`
            : !cuadra
              ? 'La plantilla debe cuadrar: cargos y abonos iguales y distintos de cero'
              : '';
  const pendiente = crear.isPending || editar.isPending;

  function cuerpo(): PlantillaRecurrenteRequest {
    return {
      nombre: nombre.trim(),
      tipoOperacionId: tipoId,
      concepto: concepto.trim(),
      anioInicio: anioN ?? 0,
      mesInicio: Number(mes),
      numeroPeriodos: periodosN ?? 0,
      diaDelMes: Number(dia),
      reversaAutomatica: reversa,
      naceAutorizada,
      partidas: partidasACuerpo(partidas),
    };
  }

  function guardar(e: React.FormEvent) {
    e.preventDefault();
    if (faltante) return;
    setErrores({});
    setMensaje('');
    const opciones = {
      onSuccess: (p: PlantillaRecurrente) => onGuardada(p),
      onError: (x: unknown) => {
        setErrores(erroresPorCampo(x));
        setMensaje(mensajeRecurrente(x));
      },
    };
    const body = cuerpo();
    const idempotencyKey = keyFor(body);
    if (plantilla)
      editar.mutate(
        { id: plantilla.id, version: plantilla.version, body, idempotencyKey },
        opciones,
      );
    else crear.mutate({ body, idempotencyKey }, opciones);
  }

  const err = (campo: string) => errores[campo] ?? [];

  return (
    <form
      onSubmit={guardar}
      className="flex min-h-0 flex-1 flex-col"
      aria-label={plantilla ? 'Editar plantilla' : 'Nueva plantilla'}
      noValidate
    >
      <SheetBody className="space-y-5">
        <FormSeccion titulo="1. Encabezado">
          <FormCampo span={12}>
            <FormGrid>
              <FormCampo span={6}>
                <Campo id="rec-nombre" label="Nombre de la plantilla *" errores={err('nombre')}>
                  <Input
                    id="rec-nombre"
                    maxLength={NOMBRE_MAXIMO}
                    value={nombre}
                    placeholder="Pago mensual a proveedor de vidrio"
                    aria-invalid={err('nombre').length > 0 || undefined}
                    aria-describedby="rec-nombre-err"
                    onChange={(e) => setNombre(e.target.value)}
                  />
                </Campo>
              </FormCampo>

              <FormCampo span={6}>
                <Campo id="rec-tipo" label="Tipo de operación *" errores={err('tipoOperacionId')}>
                  <select
                    id="rec-tipo"
                    className={SELECT_CLASS}
                    value={tipoId}
                    aria-invalid={err('tipoOperacionId').length > 0 || undefined}
                    aria-describedby="rec-tipo-err"
                    onChange={(e) => setTipoId(e.target.value)}
                  >
                    <option value="">{tipos.isLoading ? 'Cargando…' : 'Elegir tipo'}</option>
                    {tipoActualFuera && (
                      <option value={plantilla.tipoOperacionId}>{plantilla.tipoClave}</option>
                    )}
                    {opcionesTipo.map((t) => (
                      <option key={t.id} value={t.id}>
                        {t.clave} — {t.nombre}
                        {t.tipoSat ? ` (${t.tipoSat})` : ''}
                      </option>
                    ))}
                  </select>
                </Campo>
              </FormCampo>

              <FormCampo span={12}>
                <Campo
                  id="rec-concepto"
                  label="Concepto de las pólizas *"
                  errores={err('concepto')}
                  className=""
                >
                  <Textarea
                    id="rec-concepto"
                    rows={2}
                    maxLength={300}
                    className="resize-none"
                    value={concepto}
                    aria-invalid={err('concepto').length > 0 || undefined}
                    aria-describedby="rec-concepto-err"
                    onChange={(e) => setConcepto(e.target.value)}
                    placeholder="Pago a proveedor de vidrio templado"
                  />
                </Campo>
              </FormCampo>
            </FormGrid>
          </FormCampo>
        </FormSeccion>

        <FormSeccion titulo="2. Vigencia y generación">
          <FormCampo span={12}>
            <FormGrid>
              <FormCampo span={3}>
                <Campo id="rec-anio" label="Año de inicio *" errores={err('anioInicio')}>
                  <Input
                    id="rec-anio"
                    type="number"
                    inputMode="numeric"
                    min={2000}
                    max={2100}
                    value={anio}
                    aria-invalid={err('anioInicio').length > 0 || anioInvalido || undefined}
                    aria-describedby="rec-anio-err"
                    onChange={(e) => setAnio(e.target.value)}
                  />
                </Campo>
              </FormCampo>

              <FormCampo span={3}>
                <Campo id="rec-mes" label="Mes de inicio *" errores={err('mesInicio')}>
                  <select
                    id="rec-mes"
                    className={SELECT_CLASS}
                    value={mes}
                    aria-describedby="rec-mes-err"
                    onChange={(e) => setMes(e.target.value)}
                  >
                    {Array.from({ length: 12 }, (_, i) => i + 1).map((n) => (
                      <option key={n} value={n}>
                        {String(n).padStart(2, '0')} {nombrePeriodo(n)}
                      </option>
                    ))}
                  </select>
                </Campo>
              </FormCampo>

              <FormCampo span={3}>
                <Campo
                  id="rec-periodos"
                  label="Número de periodos *"
                  errores={[
                    ...err('numeroPeriodos'),
                    ...(periodos.trim() && periodosInvalido
                      ? [`De ${PERIODOS_MINIMO} a ${PERIODOS_MAXIMO} periodos.`]
                      : []),
                  ]}
                >
                  <Input
                    id="rec-periodos"
                    type="number"
                    inputMode="numeric"
                    min={PERIODOS_MINIMO}
                    max={PERIODOS_MAXIMO}
                    value={periodos}
                    aria-invalid={err('numeroPeriodos').length > 0 || periodosInvalido || undefined}
                    aria-describedby="rec-periodos-err"
                    onChange={(e) => setPeriodos(e.target.value)}
                  />
                </Campo>
              </FormCampo>

              <FormCampo span={3}>
                <Campo id="rec-dia" label="Día del mes *" errores={err('diaDelMes')}>
                  <select
                    id="rec-dia"
                    className={SELECT_CLASS}
                    value={dia}
                    aria-describedby="rec-dia-err"
                    onChange={(e) => setDia(e.target.value)}
                  >
                    <option value={ULTIMO_DIA_DEL_MES}>Último día del mes</option>
                    {Array.from({ length: DIA_MAXIMO }, (_, i) => i + 1).map((n) => (
                      <option key={n} value={n}>
                        Día {n}
                      </option>
                    ))}
                  </select>
                </Campo>
              </FormCampo>
            </FormGrid>
            {!anioInvalido && !periodosInvalido && (
              <p className="text-xs text-ink-muted">
                Vigencia: {etiquetaVigencia(anioN!, Number(mes), periodosN!)}.
              </p>
            )}
            <div className="flex flex-col gap-2.5">
              <span className="flex items-start gap-2">
                <Checkbox
                  id="rec-reversa"
                  checked={reversa}
                  onCheckedChange={(v) => setReversa(v === true)}
                />
                <span className="flex flex-col">
                  <Label htmlFor="rec-reversa" className="font-normal">
                    Reversa automática el día 1 del mes siguiente
                  </Label>
                  <span className="text-xs text-ink-muted">
                    Genera otra póliza con las mismas partidas en negativo («Reversa de TT-000123»).
                  </span>
                </span>
              </span>
              <span className="flex items-start gap-2">
                <Checkbox
                  id="rec-autorizada"
                  checked={naceAutorizada}
                  onCheckedChange={(v) => setNaceAutorizada(v === true)}
                />
                <span className="flex flex-col">
                  <Label htmlFor="rec-autorizada" className="font-normal">
                    Las pólizas nacen autorizadas
                  </Label>
                  <span className="text-xs text-ink-muted">
                    {naceAutorizada
                      ? 'Se generan con folio, sin autorización adicional.'
                      : 'Se generan en borrador y alguien las autoriza en la bandeja de pólizas.'}
                  </span>
                </span>
              </span>
            </div>
          </FormCampo>
        </FormSeccion>

        <FormSeccion titulo="3. Partidas fijas (MXN)">
          <FormCampo span={12}>
            <PartidasEditor
              idPrefix="rec"
              etiqueta="Partidas de la plantilla"
              partidas={partidasVista}
              setPartidas={setPartidas}
              errores={errores}
              onLimpiarErrores={() => setErrores({})}
            />
          </FormCampo>
        </FormSeccion>

        <CuadrePoliza
          totalCargos={totalCargos}
          totalAbonos={totalAbonos}
          nota="La plantilla debe cuadrar para guardarse: cargos y abonos iguales y distintos de cero."
        />

        {mensaje && (
          <p role="alert" className="rounded-md bg-danger-bg px-3 py-2.5 text-sm text-danger-fg">
            {mensaje}
          </p>
        )}
      </SheetBody>
      <SheetFooter className="flex-row flex-wrap items-center">
        <Button type="button" variant="ghost" size="lg" className="mr-auto" onClick={onCancelar}>
          Cancelar
        </Button>
        {faltante && <span className="text-xs text-ink-muted">{faltante}.</span>}
        <Button
          type="submit"
          size="lg"
          disabled={!!faltante || pendiente}
          title={faltante || undefined}
        >
          {pendiente ? 'Guardando…' : 'Guardar plantilla'}
        </Button>
      </SheetFooter>
    </form>
  );
}
