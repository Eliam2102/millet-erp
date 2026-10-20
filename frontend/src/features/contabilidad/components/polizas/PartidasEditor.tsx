import { FormCampo, FormGrid } from '@/components/erp/forms/FormGrid';

import type { Dispatch, ReactNode, SetStateAction } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { cn } from '@/lib/utils';
import { useSucursalesMovimiento } from '../../api/dimensiones';
import type { Auxiliar, CentroOpcion } from '../../api/dimensiones-types';
import { ETIQUETA_DIMENSION } from '../../lib/dimensiones';
import { SELECT_CLASS } from '../../lib/estilos';
import { MAX_PARTIDAS, numeroImporte, partidaVacia, type PartidaForm } from '../../lib/partidas';
import { AuxiliarSelector } from '../dimensiones/AuxiliarSelector';
import { CentroSelector } from '../dimensiones/CentroSelector';
import { CuentaSelector } from '../dimensiones/CuentaSelector';

interface Props {
  /** Prefijo de los ids de los campos (`pol` → `pol-p0-cargo`). */
  idPrefix: string;
  partidas: PartidaForm[];
  setPartidas: Dispatch<SetStateAction<PartidaForm[]>>;
  /** Errores por campo normalizados (`partidas[0].cuentaId`). */
  errores: Record<string, string[]>;
  /** Al quitar una partida los índices cambian: los errores del servidor dejan de corresponder. */
  onLimpiarErrores: () => void;
  /** Nombre accesible de la lista. */
  etiqueta: string;
}

/**
 * Editor de partidas compartido por la póliza manual (C1.3) y la plantilla recurrente (K10.4): cuenta, sucursal, centros
 * dependientes de la sucursal, dimensiones auxiliares y cargo/abono (una partida va de un solo lado).
 */
export function PartidasEditor({
  idPrefix,
  partidas,
  setPartidas,
  errores,
  onLimpiarErrores,
  etiqueta,
}: Props) {
  const sucursales = useSucursalesMovimiento();

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
    actualizar(i, numeroImporte(valor) !== 0 ? { [lado]: valor, [otro]: '' } : { [lado]: valor });
  }

  function agregar() {
    setPartidas((ps) => [...ps, partidaVacia(ps[ps.length - 1]?.sucursalId ?? '')]);
  }

  function quitar(i: number) {
    setPartidas((ps) => ps.filter((_, j) => j !== i));
    onLimpiarErrores();
  }

  const err = (campo: string) => errores[campo] ?? [];
  const errPartida = (i: number, campo: string) => err(`partidas[${i}].${campo}`);
  /** Errores de la partida que no corresponden a un campo visible (p. ej. «cargo o abono»). */
  const erroresGenerales = (i: number) =>
    Object.entries(errores)
      .filter(([k]) => k === `partidas[${i}]`)
      .flatMap(([, v]) => v);

  return (
    <>
      {err('partidas').map((m) => (
        <p key={m} className="text-xs text-danger-fg">
          {m}
        </p>
      ))}
      <ol className="flex flex-col gap-3" aria-label={etiqueta}>
        {partidas.map((p, i) => {
          const id = (c: string) => `${idPrefix}-p${i}-${c}`;
          const generales = erroresGenerales(i);
          return (
            <li
              key={p.key}
              className="rounded-lg bg-surface-card p-3 shadow-card-flat"
              aria-label={`Partida ${i + 1}`}
            >
              <div className="mb-2 flex items-center justify-between">
                <span className="text-xs font-semibold text-ink-muted">Partida {i + 1}</span>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  aria-label={`Quitar partida ${i + 1}`}
                  disabled={partidas.length <= 2}
                  title={partidas.length <= 2 ? 'La póliza lleva al menos dos partidas' : undefined}
                  onClick={() => quitar(i)}
                >
                  <Trash2 className="size-4" strokeWidth={1.6} aria-hidden="true" />
                </Button>
              </div>
              <FormGrid>
                <FormCampo span={6}>
                  <Campo
                    id={id('cuenta')}
                    label="Cuenta *"
                    errores={errPartida(i, 'cuentaId')}
                    className=""
                  >
                    <CuentaSelector
                      id={id('cuenta')}
                      value={p.cuenta}
                      onChange={(c) => actualizar(i, { cuenta: c })}
                      soloAfectables
                    />
                  </Campo>
                </FormCampo>

                <FormCampo span={6}>
                  <Campo
                    id={id('sucursal')}
                    label="Sucursal *"
                    errores={errPartida(i, 'sucursalId')}
                    className=""
                  >
                    <select
                      id={id('sucursal')}
                      className={SELECT_CLASS}
                      value={p.sucursalId}
                      aria-invalid={errPartida(i, 'sucursalId').length > 0 || undefined}
                      aria-describedby={`${id('sucursal')}-err`}
                      onChange={(e) => cambiarSucursal(i, e.target.value)}
                    >
                      <option value="">
                        {sucursales.isLoading ? 'Cargando…' : 'Elegir sucursal'}
                      </option>
                      {(sucursales.data ?? []).map((s) => (
                        <option key={s.id} value={s.id}>
                          {s.clave} — {s.nombre}
                        </option>
                      ))}
                    </select>
                  </Campo>
                </FormCampo>

                <FormCampo span={12}>
                  {(['Dim1', 'Dim2', 'Dim3'] as const).map((d) => {
                    const campo = d === 'Dim1' ? 'dim1Id' : d === 'Dim2' ? 'dim2Id' : 'dim3Id';
                    const value = d === 'Dim1' ? p.dim1 : d === 'Dim2' ? p.dim2 : p.dim3;
                    const set = (c: CentroOpcion | null) =>
                      d === 'Dim1'
                        ? actualizar(i, { dim1: c })
                        : d === 'Dim2'
                          ? cambiarDim2(i, c)
                          : actualizar(i, { dim3: c });
                    return (
                      <Campo
                        key={d}
                        id={id(campo)}
                        label={ETIQUETA_DIMENSION[d]}
                        errores={errPartida(i, campo)}
                        className=""
                      >
                        <CentroSelector
                          id={id(campo)}
                          nivel={d}
                          sucursalId={p.sucursalId}
                          dim2Id={d === 'Dim3' ? p.dim2?.id : undefined}
                          value={value}
                          onChange={set}
                          invalido={errPartida(i, campo).length > 0}
                          describedBy={`${id(campo)}-err`}
                        />
                      </Campo>
                    );
                  })}
                </FormCampo>

                <FormCampo span={4}>
                  <Campo
                    id={id('concepto')}
                    label="Concepto"
                    opcional
                    errores={errPartida(i, 'concepto')}
                    className=""
                  >
                    <Input
                      id={id('concepto')}
                      maxLength={200}
                      value={p.concepto}
                      onChange={(e) => actualizar(i, { concepto: e.target.value })}
                    />
                  </Campo>
                </FormCampo>

                <FormCampo span={4}>
                  <Campo
                    id={id('cargo')}
                    label="Cargo"
                    errores={errPartida(i, 'cargo')}
                    className=""
                  >
                    <Input
                      id={id('cargo')}
                      inputMode="decimal"
                      className="text-right tabular-nums"
                      value={p.cargo}
                      placeholder="0.00"
                      aria-invalid={errPartida(i, 'cargo').length > 0 || undefined}
                      aria-describedby={`${id('cargo')}-err`}
                      onChange={(e) => cambiarImporte(i, 'cargo', e.target.value)}
                    />
                  </Campo>
                </FormCampo>

                <FormCampo span={4}>
                  <Campo
                    id={id('abono')}
                    label="Abono"
                    errores={errPartida(i, 'abono')}
                    className=""
                  >
                    <Input
                      id={id('abono')}
                      inputMode="decimal"
                      className="text-right tabular-nums"
                      value={p.abono}
                      placeholder="0.00"
                      aria-invalid={errPartida(i, 'abono').length > 0 || undefined}
                      aria-describedby={`${id('abono')}-err`}
                      onChange={(e) => cambiarImporte(i, 'abono', e.target.value)}
                    />
                  </Campo>
                </FormCampo>
              </FormGrid>
              <details
                className="mt-2"
                open={
                  ['proyecto', 'clienteId', 'proveedorId', 'cuentaBancariaId'].some(
                    (c) => errPartida(i, c).length > 0,
                  ) || undefined
                }
              >
                <summary className="cursor-pointer text-xs text-brand">
                  Más dimensiones (proyecto, cliente, proveedor, banco)
                </summary>
                <FormGrid>
                  <FormCampo span={6}>
                    <Campo
                      id={id('proyecto')}
                      label={ETIQUETA_DIMENSION.Proyecto}
                      errores={errPartida(i, 'proyecto')}
                    >
                      <Input
                        id={id('proyecto')}
                        maxLength={40}
                        value={p.proyecto}
                        placeholder="Ej. PRY-2026-01"
                        aria-invalid={errPartida(i, 'proyecto').length > 0 || undefined}
                        aria-describedby={`${id('proyecto')}-err`}
                        onChange={(e) => actualizar(i, { proyecto: e.target.value })}
                      />
                    </Campo>
                  </FormCampo>

                  <FormCampo span={12}>
                    {(
                      [
                        [
                          'Cliente',
                          'clienteId',
                          p.cliente,
                          (a: Auxiliar | null) => actualizar(i, { cliente: a }),
                        ],
                        [
                          'Proveedor',
                          'proveedorId',
                          p.proveedor,
                          (a: Auxiliar | null) => actualizar(i, { proveedor: a }),
                        ],
                        [
                          'Banco',
                          'cuentaBancariaId',
                          p.banco,
                          (a: Auxiliar | null) => actualizar(i, { banco: a }),
                        ],
                      ] as const
                    ).map(([tipo, campo, value, set]) => (
                      <Campo
                        key={tipo}
                        id={id(campo)}
                        label={ETIQUETA_DIMENSION[tipo]}
                        errores={errPartida(i, campo)}
                      >
                        <AuxiliarSelector
                          id={id(campo)}
                          tipo={tipo}
                          value={value}
                          onChange={set}
                          invalido={errPartida(i, campo).length > 0}
                          describedBy={`${id(campo)}-err`}
                        />
                      </Campo>
                    ))}
                  </FormCampo>
                </FormGrid>
              </details>
              {generales.map((m) => (
                <p key={m} className="mt-2 text-xs text-danger-fg">
                  {m}
                </p>
              ))}
            </li>
          );
        })}
      </ol>
      <Button
        type="button"
        variant="outline"
        className="self-start border-dashed"
        onClick={agregar}
        disabled={partidas.length >= MAX_PARTIDAS}
        title={partidas.length >= MAX_PARTIDAS ? 'La póliza lleva hasta 500 partidas' : undefined}
      >
        <Plus className="mr-1 size-3.5" aria-hidden="true" />
        Agregar partida
      </Button>
    </>
  );
}

export function Campo({
  id,
  label,
  errores = [],
  children,
  opcional,
  className,
}: {
  id: string;
  label: string;
  errores?: string[];
  children: ReactNode;
  opcional?: boolean;
  className?: string;
}) {
  return (
    <div className={cn('flex min-w-0 flex-col gap-1.5', className)}>
      <Label htmlFor={id}>
        {label.replace(/ \*$/, '')}
        {label.endsWith(' *') && <span aria-hidden="true"> *</span>}
        {opcional && <span className="font-normal text-ink-muted"> (opcional)</span>}
      </Label>
      {children}
      <span id={`${id}-err`}>
        {errores.map((m) => (
          <span key={m} className="block text-xs text-danger-fg">
            {m}
          </span>
        ))}
      </span>
    </div>
  );
}
