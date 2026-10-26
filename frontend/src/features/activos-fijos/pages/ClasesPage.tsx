import { useCuenta } from '@/features/contabilidad/api/hooks';
import { CuentaActivoSelector } from '../components/CuentaActivoSelector';
import { useState } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableHead,
  TableHeader,
  TableRow,
  TableCell,
} from '@/components/ui/table';
import { useClasesActivo, useGuardarActivo } from '../api/hooks';
import type { Clase } from '../api/types';
function CuentaClase({ id }: { id: string | null }) {
  const q = useCuenta(id);
  const c = q.data?.data;
  return (
    <span title={c?.nombre}>
      {c
        ? `${c.codigo} · ${c.nombre}`
        : q.isLoading
          ? 'Cargando cuenta…'
          : q.error
            ? 'Error al consultar cuenta'
            : 'Sin cuenta configurada'}
    </span>
  );
}
export function ClasesPage() {
  const query = useClasesActivo();
  const guardar = useGuardarActivo();
  const [editar, setEditar] = useState<Partial<Clase> | null>(null);
  return (
    <main className="space-y-5 text-sm">
      <PageHeader
        titulo="Clases de activo"
        descripcion="Clases del catálogo de Millet, cuentas y tasas configurables."
        acciones={
          <Button
            onClick={() =>
              setEditar({
                activa: true,
                depreciable: true,
                tasaConfirmada: false,
                tasaFiscalAnual: null,
              })
            }
          >
            Nueva clase
          </Button>
        }
      />
      {/* eslint-disable-next-line ds/no-texto-interno -- FICHA_P7 exige marcar las sugerencias como Por confirmar con Millet. */}
      <p className="rounded-lg bg-warning-bg p-3 text-warning-fg">
        Las tasas sembradas son sugerencias: Por confirmar con Millet. Capture las tasas y configure
        las cuentas pendientes antes de depreciar.
      </p>
      {query.error && (
        <p role="alert" className="text-danger-fg">
          {query.error.message}
        </p>
      )}
      <div className="rounded-lg bg-surface-card shadow-card-flat">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Clave</TableHead>
              <TableHead>Nombre</TableHead>
              <TableHead>Cuenta de activo</TableHead>
              <TableHead>Depreciación acumulada</TableHead>
              <TableHead>Gasto</TableHead>
              <TableHead>Depreciable</TableHead>
              <TableHead>Tasa anual</TableHead>
              <TableHead>Estado</TableHead>
              <TableHead>Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {query.data?.map((c) => (
              <TableRow key={c.id}>
                <TableCell className="font-mono">{c.clave}</TableCell>
                <TableCell>{c.nombre}</TableCell>
                <TableCell>
                  <CuentaClase id={c.cuentaActivoId} />
                </TableCell>
                <TableCell>
                  <CuentaClase id={c.cuentaDepreciacionId} />
                </TableCell>
                <TableCell>
                  <CuentaClase id={c.cuentaGastoId} />
                </TableCell>
                <TableCell>{c.depreciable ? 'Sí' : 'No'}</TableCell>
                <TableCell className="tabular-nums">
                  {c.tasaFiscalAnual == null ? 'Sin tasa' : `${c.tasaFiscalAnual} % · Línea recta`}
                  {/* eslint-disable-next-line ds/no-texto-interno -- FICHA_P7 exige el estado de negocio Tasa por confirmar. */}
                  {!c.tasaConfirmada && <p className="text-warning-fg">Tasa por confirmar</p>}
                </TableCell>
                <TableCell>
                  <Badge variant={c.activa ? 'success' : 'neutral'}>
                    {c.activa ? 'Activa' : 'Inactiva'}
                  </Badge>
                  {c.requiereRevision && <Badge variant="warning">Revisar clase anterior</Badge>}
                </TableCell>
                <TableCell>
                  <Button variant="outline" onClick={() => setEditar(c)}>
                    Editar
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      {editar && (
        <form
          className="grid grid-cols-2 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat"
          onSubmit={(e) => {
            e.preventDefault();
            guardar.mutate(
              {
                ruta: editar.id ? `clases/${editar.id}` : 'clases',
                method: editar.id ? 'PUT' : 'POST',
                version: editar.version,
                body: editar,
              },
              { onSuccess: () => setEditar(null) },
            );
          }}
        >
          {(
            [
              'clave',
              'nombre',
              'cuentaActivoId',
              'cuentaDepreciacionId',
              'cuentaGastoId',
              'cuentaPuenteId',
            ] as const
          ).map((k, i) => (
            <label key={k} className="text-xs">
              {
                [
                  'Clave *',
                  'Nombre *',
                  'Cuenta de activo',
                  'Depreciación acumulada',
                  'Gasto por depreciación',
                  'Cuenta puente de la factura',
                ][i]
              }
              {i < 2 ? (
                <Input
                  required
                  readOnly={k === 'clave' && !!editar.id}
                  value={editar[k] ?? ''}
                  onChange={(e) => setEditar({ ...editar, [k]: e.target.value })}
                />
              ) : (
                <CuentaActivoSelector
                  value={editar[k] ?? null}
                  onChange={(id) => setEditar({ ...editar, [k]: id })}
                />
              )}
            </label>
          ))}
          <label className="text-xs">
            Tasa anual (sugerencia hasta confirmar)
            <Input
              type="number"
              min="0"
              max="100"
              step="0.01"
              value={editar.tasaFiscalAnual ?? ''}
              onChange={(e) =>
                setEditar({
                  ...editar,
                  tasaFiscalAnual: e.target.value === '' ? null : Number(e.target.value),
                  tasaConfirmada: false,
                })
              }
            />
          </label>
          <label className="flex items-center gap-2 text-xs">
            <input
              type="checkbox"
              checked={editar.depreciable ?? true}
              onChange={(e) => setEditar({ ...editar, depreciable: e.target.checked })}
            />
            Depreciable
          </label>
          <label className="flex items-center gap-2 text-xs">
            <input
              type="checkbox"
              checked={editar.tasaConfirmada ?? false}
              disabled={editar.tasaFiscalAnual == null}
              onChange={(e) => setEditar({ ...editar, tasaConfirmada: e.target.checked })}
            />
            Tasa confirmada con Millet (requiere capturar una tasa)
          </label>
          <label className="flex items-center gap-2 text-xs">
            <input
              type="checkbox"
              checked={editar.activa ?? true}
              onChange={(e) => setEditar({ ...editar, activa: e.target.checked })}
            />
            Activa
          </label>
          {guardar.error && (
            <p role="alert" className="text-danger-fg">
              {guardar.error.message}
            </p>
          )}
          <Button type="submit">Guardar clase</Button>
          <Button type="button" variant="outline" onClick={() => setEditar(null)}>
            Cancelar
          </Button>
        </form>
      )}
    </main>
  );
}
