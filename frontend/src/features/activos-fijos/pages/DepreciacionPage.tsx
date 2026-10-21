import { agruparDepreciacion, type AgrupacionDepreciacion } from '../api/agrupacion';
import { useState } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { useGuardarActivo } from '../api/hooks';
import { useLibroQuery, type Corrida, type Depreciacion } from '../api/libro';
import { TablaLibro } from '../components/TablaLibro';
import { columnasDepreciacion } from '../components/columnas-depreciacion';
export function DepreciacionPage() {
  const [periodo, setPeriodo] = useState('2026-02');
  const [consultado, setConsultado] = useState('');
  const [motivo, setMotivo] = useState('');
  const [agrupacion, setAgrupacion] = useState<AgrupacionDepreciacion>('clase');
  const previa = useLibroQuery<Depreciacion[]>(
    `depreciacion/previa?periodo=${consultado}-01`,
    !!consultado,
  );
  const historial = useLibroQuery<Corrida[]>('depreciacion');
  const guardar = useGuardarActivo();
  const anular = useHasPermission(P.ActivosFijosDepreciacionAnular);
  const corrida = historial.data?.find((c) => c.periodo === `${periodo}-01` && !c.anulada);
  return (
    <main className="space-y-5 text-sm">
      <PageHeader
        titulo="Depreciación"
        descripcion="Revise la cuota mensual y confirme la póliza del libro de activos."
      />
      <p className="text-ink-muted">
        Línea recta desde el mes siguiente al inicio de uso. Tasas propuestas sujetas a validación
        de Millet.
      </p>
      <div className="flex flex-wrap items-end gap-3">
        <label>
          Periodo *
          <Input
            type="month"
            value={periodo}
            onChange={(e) => {
              setPeriodo(e.target.value);
              setConsultado('');
            }}
          />
        </label>
        <Button
          variant="outline"
          disabled={!periodo}
          title={!periodo ? 'Seleccione un periodo' : undefined}
          onClick={() => setConsultado(periodo)}
        >
          Vista previa
        </Button>
        <Button
          disabled={!previa.data || consultado !== periodo || guardar.isPending || !!corrida}
          title={
            corrida
              ? 'El periodo ya está confirmado'
              : !previa.data
                ? 'Consulte la vista previa primero'
                : guardar.isPending
                  ? 'Guardando corrida'
                  : undefined
          }
          onClick={() =>
            guardar.mutate({ ruta: 'depreciacion', body: { periodo: `${periodo}-01` } })
          }
        >
          Confirmar corrida
        </Button>
        {corrida && <Badge variant="success">Ya confirmada</Badge>}
      </div>
      {(previa.error || historial.error || guardar.error) && (
        <p role="alert" className="text-danger-fg">
          {(previa.error || historial.error || guardar.error)?.message}
        </p>
      )}
      {consultado && (
        <TablaLibro
          titulo={`Vista previa ${consultado}`}
          filas={previa.data}
          columnas={columnasDepreciacion}
        />
      )}
      {anular && (
        <label className="block">
          Motivo de anulación *<Input value={motivo} onChange={(e) => setMotivo(e.target.value)} />
        </label>
      )}
      <label className="block">
        Agrupar reporte por
        <select
          className="ml-3 rounded-md border border-line-control bg-surface-card p-2"
          value={agrupacion}
          onChange={(e) => setAgrupacion(e.target.value as AgrupacionDepreciacion)}
        >
          <option value="clase">Clase</option>
          <option value="sucursal">Sucursal</option>
          <option value="centroCosto">Centro de costo</option>
        </select>
      </label>
      {corrida && (
        <TablaLibro
          titulo={`Depreciación por ${agrupacion} · ${periodo}`}
          filas={agruparDepreciacion(corrida.detalles, agrupacion)}
          columnas={[
            { clave: 'grupo', etiqueta: 'Grupo', tipo: 'texto' },
            { clave: 'activos', etiqueta: 'Activos', tipo: 'numero' },
            { clave: 'importe', etiqueta: 'Depreciación', tipo: 'moneda' },
          ]}
        />
      )}
      <TablaLibro
        titulo="Historial de corridas"
        filas={historial.data}
        columnas={[
          { clave: 'periodo', etiqueta: 'Periodo', tipo: 'fecha' },
          { clave: 'revision', etiqueta: 'Revisión', tipo: 'numero' },
          { clave: 'polizaId', etiqueta: 'Póliza', tipo: 'texto' },
          { clave: 'motivoAnulacion', etiqueta: 'Motivo de anulación', tipo: 'texto' },
        ]}
        accionFila={(c) =>
          c.anulada ? (
            <Badge variant="neutral">Anulada</Badge>
          ) : anular ? (
            <Button
              variant="outline"
              disabled={!motivo.trim() || guardar.isPending}
              title={
                !motivo.trim() ? 'Capture un motivo' : guardar.isPending ? 'Guardando' : undefined
              }
              onClick={() =>
                guardar.mutate({
                  ruta: `depreciacion/${c.id}/anular`,
                  version: c.version,
                  body: { motivo },
                })
              }
            >
              Anular corrida
            </Button>
          ) : (
            <Badge variant="success">Confirmada</Badge>
          )
        }
      />
      {historial.data
        ?.filter((c) => !c.anulada && c.periodo === `${periodo}-01`)
        .map((c) => (
          <TablaLibro
            key={c.id}
            titulo={`Depreciación contabilizada ${periodo}`}
            filas={c.detalles}
            columnas={columnasDepreciacion}
          />
        ))}
    </main>
  );
}
