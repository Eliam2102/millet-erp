import { useState } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { Input } from '@/components/ui/input';
import { useLibroQuery, type Conciliacion, type FilaLibro } from '../api/libro';
import { TablaLibro, type ColumnaLibro } from '../components/TablaLibro';
import { columnasDepreciacion } from '../components/columnas-depreciacion';
const columnasLibro: ColumnaLibro[] = [
  { clave: 'numero', etiqueta: 'Activo', tipo: 'texto' },
  { clave: 'clase', etiqueta: 'Clase', tipo: 'texto' },
  { clave: 'sucursal', etiqueta: 'Sucursal', tipo: 'texto' },
  { clave: 'centroCosto', etiqueta: 'Centro de costo', tipo: 'texto' },
  { clave: 'valor', etiqueta: 'Valor', tipo: 'moneda' },
  { clave: 'acumulada', etiqueta: 'Acumulada', tipo: 'moneda' },
  { clave: 'neto', etiqueta: 'Neto', tipo: 'moneda' },
];
export function ConciliacionPage() {
  const [periodo, setPeriodo] = useState('2026-02');
  const q = useLibroQuery<Conciliacion>(`conciliacion?periodo=${periodo}-01`, !!periodo);
  return (
    <main className="space-y-5 text-sm">
      <PageHeader
        titulo="Conciliación"
        descripcion="Compare el libro con los saldos contables y revise las diferencias por clase."
      />
      <label className="block">
        Periodo *<Input type="month" value={periodo} onChange={(e) => setPeriodo(e.target.value)} />
      </label>
      {q.error && (
        <p role="alert" className="text-danger-fg">
          {q.error.message}
        </p>
      )}
      <TablaLibro
        titulo={`Conciliación ${periodo}`}
        filas={q.data?.clases}
        columnas={[
          { clave: 'clase', etiqueta: 'Clase', tipo: 'texto' },
          ...[
            ['valorLibro', 'Valor libro'],
            ['saldoActivo', 'Saldo activo'],
            ['diferenciaValor', 'Diferencia valor'],
            ['acumuladaLibro', 'Acumulada libro'],
            ['saldoDepreciacion', 'Saldo depreciación'],
            ['diferenciaAcumulada', 'Diferencia acumulada'],
          ].map(([clave, etiqueta]) => ({ clave, etiqueta, tipo: 'moneda' as const })),
          { clave: 'observacion', etiqueta: 'Observación', tipo: 'texto' },
        ]}
      />
      {q.data && (
        <>
          <TablaLibro titulo="Detalle de activos" filas={q.data.activos} columnas={columnasLibro} />
          <TablaLibro
            titulo="Corridas del periodo"
            filas={q.data.corridas.flatMap((c) =>
              c.detalles.map((d) => ({ ...d, estado: c.anulada ? 'Anulada' : 'Confirmada' })),
            )}
            columnas={[
              ...columnasDepreciacion,
              { clave: 'estado', etiqueta: 'Estado', tipo: 'texto' },
            ]}
          />
          <TablaLibro
            titulo="Ajustes y bajas del periodo"
            filas={q.data.ajustes}
            columnas={[
              { clave: 'tipo', etiqueta: 'Tipo', tipo: 'texto' },
              { clave: 'fecha', etiqueta: 'Fecha', tipo: 'fecha' },
              { clave: 'motivo', etiqueta: 'Motivo', tipo: 'texto' },
              { clave: 'importe', etiqueta: 'Importe', tipo: 'moneda' },
              { clave: 'polizaId', etiqueta: 'Póliza', tipo: 'texto' },
            ]}
          />
        </>
      )}
    </main>
  );
}
export function LibroActivosPage() {
  const [corte, setCorte] = useState('2026-02-28');
  const q = useLibroQuery<FilaLibro[]>(`reportes/libro?corte=${corte}`, !!corte);
  return (
    <main className="space-y-5 text-sm">
      <PageHeader
        titulo="Libro de activos"
        descripcion="Consulte y exporte el valor, la depreciación y el neto a una fecha."
      />
      <label className="block">
        Fecha de corte *
        <Input type="date" value={corte} onChange={(e) => setCorte(e.target.value)} />
      </label>
      {q.error && <p role="alert">{q.error.message}</p>}
      <TablaLibro titulo={`Libro de activos al ${corte}`} filas={q.data} columnas={columnasLibro} />
    </main>
  );
}
