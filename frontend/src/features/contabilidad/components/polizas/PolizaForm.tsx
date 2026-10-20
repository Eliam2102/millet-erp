import { SheetBody, SheetFooter } from '@/components/ui/sheet';
import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';

import { useEffect, useMemo, useState } from 'react';
import { Link2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { useBodyScopedIdempotencyKey } from '@/lib/api';
import { hoyLocalISO } from '@/lib/datetime';
import { useTiposDocumento } from '../../api/dimensiones';
import { useCrearPoliza, useEditarPoliza } from '../../api/polizas';
import type { Poliza, PolizaManualRequest } from '../../api/polizas-types';
import { SELECT_CLASS } from '../../lib/estilos';
import {
  centroDe,
  partidaVacia,
  partidasACuerpo,
  totalesPartidas,
  type PartidaForm,
} from '../../lib/partidas';
import { erroresPorCampo, mensajePoliza } from '../../lib/polizas';
import { CuadrePoliza } from './CuadrePoliza';
import { Campo, PartidasEditor } from './PartidasEditor';

/** K10.4: clave del tipo de operación que se sugiere para una reclasificación (Asiento contable), si existe. */
export const CLAVE_TIPO_RECLASIFICACION = 'AS';

/** Póliza autorizada que la nueva reclasifica (K10.4-a). */
export interface ReclasificaA {
  id: string;
  folio: string | null;
}

function inicial(p?: Poliza, reclasifica?: ReclasificaA) {
  if (!p) {
    const concepto = reclasifica
      ? `Reclasificación de ${reclasifica.folio ?? 'la póliza original'}`
      : '';
    return {
      tipoId: null,
      fecha: hoyLocalISO(),
      ajuste: false,
      concepto,
      partidas: [partidaVacia(), partidaVacia()],
    };
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
  /** K10.4: la nueva póliza reclasifica a esta (solo al crear). */
  reclasifica?: ReclasificaA;
}

/**
 * Captura de póliza manual (C1.3): encabezado + partidas con cuenta, sucursal y centros dependientes de la sucursal.
 * El borrador puede guardarse sin cuadrar; el indicador en vivo muestra cargos, abonos y diferencia (se exige al autorizar).
 */
export function PolizaForm({ poliza, onGuardada, onCancelar, onDirtyChange, reclasifica }: Props) {
  const tipos = useTiposDocumento();
  const crear = useCrearPoliza();
  const editar = useEditarPoliza();
  // El usuario corrige el borrador tras un rechazo: body distinto ⇒ key nueva; reintento idéntico ⇒ misma key.
  const keyFor = useBodyScopedIdempotencyKey();

  const base = useMemo(() => inicial(poliza, reclasifica), [poliza, reclasifica]);
  // null = el usuario no ha elegido tipo: en una reclasificación se sugiere el de Asiento contable.
  const [tipoId, setTipoId] = useState<string | null>(base.tipoId);
  const [fecha, setFecha] = useState(base.fecha);
  const [ajuste, setAjuste] = useState(base.ajuste);
  const [concepto, setConcepto] = useState(base.concepto);
  const [partidas, setPartidas] = useState<PartidaForm[]>(base.partidas);
  const [errores, setErrores] = useState<Record<string, string[]>>({});
  const [mensaje, setMensaje] = useState('');

  const snapshot = JSON.stringify({
    tipoId,
    fecha,
    ajuste,
    concepto,
    partidas: partidas.map((p) => ({ ...p, key: '' })),
  });
  const [inicialSnapshot] = useState(snapshot);
  const dirty = snapshot !== inicialSnapshot;
  useEffect(() => {
    onDirtyChange?.(dirty);
  }, [dirty, onDirtyChange]);

  const { totalCargos, totalAbonos } = totalesPartidas(partidas);

  // Un tipo inactivo (o de prueba fuera de la lista activa) se conserva visible al editar.
  const opcionesTipo = tipos.data ?? [];
  const tipoActualFuera = poliza && !opcionesTipo.some((t) => t.id === poliza.tipoOperacionId);
  const tipoSugerido =
    reclasifica && !poliza
      ? (opcionesTipo.find((t) => t.clave === CLAVE_TIPO_RECLASIFICACION)?.id ?? '')
      : '';
  const tipoSel = tipoId ?? tipoSugerido;

  function cambiarAjuste(v: boolean) {
    setAjuste(v);
    if (v) setFecha(`${anioDe(fecha)}-12-31`);
  }

  const faltante = !tipoSel
    ? 'Elige el tipo de operación'
    : !fecha
      ? 'Indica la fecha'
      : !concepto.trim()
        ? 'Escribe el concepto'
        : '';
  const pendiente = crear.isPending || editar.isPending;

  function cuerpo(): PolizaManualRequest {
    return {
      tipoOperacionId: tipoSel,
      fecha,
      concepto: concepto.trim(),
      periodoNumero: ajuste ? 13 : null,
      documentoOrigen: null,
      partidas: partidasACuerpo(partidas),
      // La liga con la original se fija al crear; editar el borrador la conserva en el servidor.
      ...(reclasifica && !poliza ? { reclasificaPolizaId: reclasifica.id } : {}),
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
    if (poliza)
      editar.mutate({ id: poliza.id, version: poliza.version, body, idempotencyKey }, opciones);
    else crear.mutate({ body, idempotencyKey }, opciones);
  }

  const err = (campo: string) => errores[campo] ?? [];

  return (
    <form
      onSubmit={guardar}
      className="flex min-h-0 flex-1 flex-col"
      aria-label={poliza ? 'Editar póliza' : 'Nueva póliza'}
      noValidate
    >
      <SheetBody className="space-y-5">
        {reclasifica && !poliza && (
          <p
            role="note"
            className="flex items-start gap-2 rounded-md bg-info-bg px-3 py-2.5 text-sm text-info-fg"
          >
            <Link2 className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} aria-hidden="true" />
            <span>
              Reclasifica a{' '}
              <span className="font-mono font-medium">{reclasifica.folio ?? '[FOLIO]'}</span>. Es
              una póliza manual: se guarda en borrador y la autoriza otro usuario; ambas pólizas
              quedan ligadas.
            </span>
          </p>
        )}
        <FormSeccion titulo="1. Encabezado">
          <FormCampo span={12}>
            <FormGrid>
              <FormCampo span={6}>
                <Campo id="pol-tipo" label="Tipo de operación *" errores={err('tipoOperacionId')}>
                  <select
                    id="pol-tipo"
                    className={SELECT_CLASS}
                    value={tipoSel}
                    disabled={!!poliza}
                    title={
                      poliza
                        ? 'El tipo de operación no se cambia: descarta el borrador y captura otro'
                        : undefined
                    }
                    aria-invalid={err('tipoOperacionId').length > 0 || undefined}
                    aria-describedby="pol-tipo-err"
                    onChange={(e) => setTipoId(e.target.value)}
                  >
                    <option value="">{tipos.isLoading ? 'Cargando…' : 'Elegir tipo'}</option>
                    {tipoActualFuera && (
                      <option value={poliza.tipoOperacionId}>
                        {poliza.tipoClave} — {poliza.tipoNombre ?? ''}
                      </option>
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

              <FormCampo span={6}>
                <Campo id="pol-fecha" label="Fecha *" errores={err('fecha')}>
                  <Input
                    id="pol-fecha"
                    type="date"
                    value={fecha}
                    disabled={ajuste}
                    title={
                      ajuste
                        ? 'El periodo 13 de ajustes usa el 31 de diciembre del ejercicio'
                        : undefined
                    }
                    aria-invalid={err('fecha').length > 0 || undefined}
                    aria-describedby="pol-fecha-err"
                    onChange={(e) => setFecha(e.target.value)}
                  />
                </Campo>
              </FormCampo>

              <FormCampo span={12}>
                <div className="flex flex-col gap-1.5 ">
                  <span className="flex items-center gap-2">
                    <Checkbox
                      id="pol-ajuste"
                      checked={ajuste}
                      onCheckedChange={(v) => cambiarAjuste(v === true)}
                    />
                    <Label htmlFor="pol-ajuste" className="font-normal">
                      Periodo 13 (ajustes de auditoría)
                    </Label>
                  </span>
                  {ajuste && (
                    <span className="flex flex-wrap items-center gap-2 text-xs text-ink-muted">
                      <Label htmlFor="pol-ejercicio" className="text-xs font-normal">
                        Ejercicio
                      </Label>
                      <Input
                        id="pol-ejercicio"
                        type="number"
                        min={2000}
                        max={2100}
                        className="h-ctl-md w-24"
                        value={anioDe(fecha)}
                        onChange={(e) => setFecha(`${e.target.value}-12-31`)}
                      />
                      <span>
                        La póliza se registra al 31 de diciembre en el periodo de ajustes.
                      </span>
                    </span>
                  )}
                  {err('periodoNumero').map((m) => (
                    <span key={m} className="text-xs text-danger-fg">
                      {m}
                    </span>
                  ))}
                </div>
              </FormCampo>

              <FormCampo span={12}>
                <Campo id="pol-concepto" label="Concepto *" errores={err('concepto')} className="">
                  <Textarea
                    id="pol-concepto"
                    rows={2}
                    maxLength={300}
                    className="resize-none"
                    value={concepto}
                    aria-invalid={err('concepto').length > 0 || undefined}
                    aria-describedby="pol-concepto-err"
                    onChange={(e) => setConcepto(e.target.value)}
                    placeholder="[CONCEPTO DE LA PÓLIZA]"
                  />
                </Campo>
              </FormCampo>
            </FormGrid>
          </FormCampo>
        </FormSeccion>

        <FormSeccion titulo="2. Partidas">
          <FormCampo span={12}>
            <PartidasEditor
              idPrefix="pol"
              etiqueta="Partidas de la póliza"
              partidas={partidas}
              setPartidas={setPartidas}
              errores={errores}
              onLimpiarErrores={() => setErrores({})}
            />
          </FormCampo>
        </FormSeccion>

        <CuadrePoliza
          totalCargos={totalCargos}
          totalAbonos={totalAbonos}
          nota="El borrador puede guardarse sin cuadrar; para autorizar, cargos y abonos deben ser iguales y distintos de cero."
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
          {pendiente ? 'Guardando…' : 'Guardar borrador'}
        </Button>
      </SheetFooter>
    </form>
  );
}
