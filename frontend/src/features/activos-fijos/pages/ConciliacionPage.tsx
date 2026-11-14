import { hoyLocalISO } from '@/lib/datetime';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { useAuthStore } from '@/lib/auth/auth-store';
import { UsuarioSelector } from '@/components/erp/selectors/UsuarioSelector';
import { Button } from '@/components/ui/button';
import { useGuardarActivo } from '../api/hooks';
import { useState } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { Input } from '@/components/ui/input';
import { useLibroQuery, type Conciliacion, type FilaLibro } from '../api/libro';
import { TablaLibro, type ColumnaLibro } from '../components/TablaLibro';
import { columnasDepreciacion } from '../components/columnas-depreciacion';
const columnasLibro: ColumnaLibro[] = [
  { clave: 'numero', etiqueta: 'Activo', tipo: 'texto' },
  { clave: 'clase', etiqueta: 'Clase', tipo: 'texto' },
  { clave: 'marcaTasa', etiqueta: 'Tasa', tipo: 'texto' },
  { clave: 'sucursal', etiqueta: 'Sucursal', tipo: 'texto' },
  { clave: 'centroCosto', etiqueta: 'Centro de costo', tipo: 'texto' },
  { clave: 'valor', etiqueta: 'Valor', tipo: 'moneda' },
  { clave: 'acumulada', etiqueta: 'Acumulada', tipo: 'moneda' },
  { clave: 'neto', etiqueta: 'Neto', tipo: 'moneda' },
];
export function ConciliacionPage() {
  const [periodo, setPeriodo] = useState(() => hoyLocalISO().slice(0, 7));
  const actual = useAuthStore((s) => s.currentSucursalId);
  const [sucursal, setSucursal] = useState(actual ?? '');
  const alcance = sucursal ? `&sucursalId=${sucursal}` : '';
  const guardar = useGuardarActivo();
  const [clasificaciones, setClasificaciones] = useState<
    Record<string, { causa: string; responsableId: string }>
  >({});
  const historial = useLibroQuery<
    {
      id: string;
      estado: string;
      aceptadaPor: string;
      aceptadaEn: string;
      reporteJson: string;
      clasificacionesJson: string;
    }[]
  >(`conciliacion/historial?periodo=${periodo}-01${alcance}`, !!periodo);
  const q = useLibroQuery<Conciliacion>(`conciliacion?periodo=${periodo}-01${alcance}`, !!periodo);
  return (
    <main className="space-y-5 text-sm">
      <PageHeader
        titulo="Conciliación"
        descripcion="Compare el libro con los saldos contables y revise las diferencias por clase."
      />
      <label className="block">
        Periodo *
        <Input
          type="month"
          value={periodo}
          onChange={(e) => {
            setPeriodo(e.target.value);
            setClasificaciones({});
          }}
        />
      </label>
      <label>
        Sucursal de la conciliación
        <SucursalSelector
          value={sucursal || null}
          onChange={(id) => {
            setSucursal(id ?? '');
            setClasificaciones({});
          }}
        />
      </label>
      {(q.error || guardar.error || historial.error) && (
        <p role="alert" className="text-danger-fg">
          {(q.error || guardar.error || historial.error)?.message}
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
          <TablaLibro
            titulo="Detalle de activos"
            filas={q.data.activos.map((a) => ({
              ...a,
              marcaTasa: a.tasaPropia ? 'Tasa propia' : 'Clase',
            }))}
            columnas={columnasLibro}
          />
          <TablaLibro
            titulo="Conciliación de depreciación por activo"
            filas={q.data.depreciacionPorActivo ?? []}
            columnas={[
              { clave: 'numero', etiqueta: 'Activo', tipo: 'texto' },
              { clave: 'importeLibro', etiqueta: 'Depreciación libro', tipo: 'moneda' },
              { clave: 'importePoliza', etiqueta: 'Depreciación en póliza', tipo: 'moneda' },
              { clave: 'diferencia', etiqueta: 'Diferencia del activo', tipo: 'moneda' },
              {
                clave: 'diferenciaGrupo',
                etiqueta: 'Diferencia de clase y custodia',
                tipo: 'moneda',
              },
              { clave: 'causaSugerida', etiqueta: 'Causa sugerida', tipo: 'texto' },
            ]}
          />
          <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
            <h2 className="font-semibold">Registrar conciliación del periodo</h2>
            {q.data.clases
              .flatMap((c) =>
                (['Valor', 'Acumulada'] as const)
                  .filter((tipo) =>
                    tipo === 'Valor' ? c.diferenciaValor !== 0 : c.diferenciaAcumulada !== 0,
                  )
                  .map((tipo) => ({ ...c, tipo, llave: `${c.claseId}:${tipo}` })),
              )
              .map((c) => (
                <div key={c.llave} className="grid gap-3 md:grid-cols-3">
                  <span>
                    Clase {c.clase} · {c.tipo}
                  </span>
                  <label>
                    Causa de la diferencia
                    <select
                      className="h-ctl-xl w-full rounded-md border border-line-control bg-surface-card px-3"
                      value={clasificaciones[c.llave]?.causa ?? ''}
                      onChange={(e) =>
                        setClasificaciones({
                          ...clasificaciones,
                          [c.llave]: {
                            responsableId: clasificaciones[c.llave]?.responsableId ?? '',
                            causa: e.target.value,
                          },
                        })
                      }
                    >
                      <option value="">Seleccione causa</option>
                      {[
                        'Carga inicial',
                        'Corrida',
                        'Disposición',
                        'Asiento manual',
                        'Póliza faltante',
                      ].map((causa) => (
                        <option key={causa}>{causa}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Responsable
                    <UsuarioSelector
                      value={clasificaciones[c.llave]?.responsableId || null}
                      onChange={(id) =>
                        setClasificaciones({
                          ...clasificaciones,
                          [c.llave]: {
                            causa: clasificaciones[c.llave]?.causa ?? '',
                            responsableId: id ?? '',
                          },
                        })
                      }
                    />
                  </label>
                </div>
              ))}
            <Button
              disabled={guardar.isPending}
              onClick={() =>
                guardar.mutate({
                  ruta: 'conciliacion',
                  body: {
                    periodo: `${periodo}-01`,
                    sucursalId: sucursal || null,
                    diferencias: Object.entries(clasificaciones).map(([llave, d]) => {
                      const [claseId, tipo] = llave.split(':');
                      return { claseId, tipo, ...d };
                    }),
                  },
                })
              }
            >
              Registrar y aceptar conciliación
            </Button>
          </section>
          <section className="space-y-3">
            <h2 className="font-semibold">Conciliaciones guardadas</h2>
            {historial.data?.map((c) => (
              <details key={c.id}>
                <summary>
                  {c.estado} · {c.aceptadaEn} · Aceptada por {c.aceptadaPor}
                </summary>
                <p>La conciliación aceptada no se edita; registre otra si los saldos cambiaron.</p>
                <TablaLibro
                  titulo="Saldos aceptados"
                  filas={JSON.parse(c.reporteJson).Clases as Record<string, unknown>[]}
                  columnas={[
                    { clave: 'Clase', etiqueta: 'Clase', tipo: 'texto' },
                    { clave: 'ValorLibro', etiqueta: 'Valor libro', tipo: 'moneda' },
                    { clave: 'AcumuladaLibro', etiqueta: 'Acumulada', tipo: 'moneda' },
                    { clave: 'NetoLibro', etiqueta: 'Neto', tipo: 'moneda' },
                    { clave: 'SaldoActivo', etiqueta: 'Saldo contable activo', tipo: 'moneda' },
                    {
                      clave: 'SaldoDepreciacion',
                      etiqueta: 'Saldo contable acumulada',
                      tipo: 'moneda',
                    },
                    { clave: 'DiferenciaValor', etiqueta: 'Diferencia valor', tipo: 'moneda' },
                    {
                      clave: 'DiferenciaAcumulada',
                      etiqueta: 'Diferencia acumulada',
                      tipo: 'moneda',
                    },
                  ]}
                />
                <TablaLibro
                  titulo="Causas y responsables aceptados"
                  filas={JSON.parse(c.clasificacionesJson) as Record<string, unknown>[]}
                  columnas={[
                    { clave: 'Clase', etiqueta: 'Clase', tipo: 'texto' },
                    { clave: 'Tipo', etiqueta: 'Diferencia', tipo: 'texto' },
                    { clave: 'Causa', etiqueta: 'Causa', tipo: 'texto' },
                    { clave: 'Responsable', etiqueta: 'Responsable', tipo: 'texto' },
                  ]}
                />
              </details>
            ))}
          </section>
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
  const [corte, setCorte] = useState(hoyLocalISO);
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
      <TablaLibro
        titulo={`Libro de activos al ${corte}`}
        filas={q.data?.map((a) => ({ ...a, marcaTasa: a.tasaPropia ? 'Tasa propia' : 'Clase' }))}
        columnas={columnasLibro}
      />
    </main>
  );
}
