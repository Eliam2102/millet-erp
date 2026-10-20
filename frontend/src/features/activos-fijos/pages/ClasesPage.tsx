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
export function ClasesPage() {
  const query = useClasesActivo();
  const guardar = useGuardarActivo();
  const [editar, setEditar] = useState<Partial<Clase> | null>(null);
  return (
    <main className="space-y-5 text-sm">
      <PageHeader
        titulo="Clases de activo"
        descripcion="Cuentas y tasas propuestas por VILO, pendientes de validación por Laura Cerón."
        acciones={<Button onClick={() => setEditar({ activa: true })}>Nueva clase</Button>}
      />
      <p className="rounded-lg bg-warning-bg p-3 text-warning-fg">
        Las clases DEMO pueden tener cuentas vacías. Configure las cuentas contables antes de
        capitalizar.
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
                <TableCell className="tabular-nums">{c.tasaFiscalAnual}% · Línea recta</TableCell>
                <TableCell>
                  <Badge variant={c.activa ? 'success' : 'neutral'}>
                    {c.activa ? 'Activa' : 'Inactiva'}
                  </Badge>
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
            Tasa anual *
            <Input
              required
              type="number"
              min="0"
              max="100"
              step="0.01"
              value={editar.tasaFiscalAnual ?? ''}
              onChange={(e) => setEditar({ ...editar, tasaFiscalAnual: Number(e.target.value) })}
            />
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
