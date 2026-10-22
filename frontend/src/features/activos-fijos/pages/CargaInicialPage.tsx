import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest, useBodyScopedIdempotencyKey } from '@/lib/api';
import { PageHeader } from '@/components/erp/PageHeader';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { AF_BASE, afKeys, useClasesActivo } from '../api/hooks';
import { descargarCsv, type CargaResultado } from '../api/libro';
import { TablaLibro } from '../components/TablaLibro';
const plantilla =
  'numero,clase,descripcion,fechaAdquisicion,inicioUso,valorOriginal,depreciacionAcumulada,responsableId,sucursalId,ubicacion,centroCostoId,equipoId,importado,pedimento,arrendado\n';
export function CargaInicialPage() {
  const [csv, setCsv] = useState('');
  const [corte, setCorte] = useState('2026-10-31');
  const [versionArchivo, setVersion] = useState('1');
  const [totales, setTotales] = useState<Record<string, { valor: number; acumulada: number }>>({});
  const [resultado, setResultado] = useState<CargaResultado>();
  const [errorArchivo, setErrorArchivo] = useState('');
  const clases = useClasesActivo();
  const key = useBodyScopedIdempotencyKey();
  const qc = useQueryClient();
  const cargar = useMutation({
    mutationFn: async (confirmar: boolean) => {
      const body = {
        csv,
        corte,
        versionArchivo,
        totales: Object.entries(totales).map(([clase, t]) => ({ clase, ...t })),
      };
      return (
        await apiRequest<CargaResultado>(`${AF_BASE}/carga/${confirmar ? 'confirmar' : 'previa'}`, {
          method: 'POST',
          body,
          idempotencyKey: confirmar ? key(body) : undefined,
        })
      ).data;
    },
    onSuccess: (data) => {
      setResultado(data);
      void qc.invalidateQueries({ queryKey: afKeys.all });
    },
  });
  return (
    <main className="space-y-5 text-sm">
      <PageHeader
        titulo="Carga inicial"
        descripcion="Valide el libro SAP y compruebe sus saldos antes de incorporar los activos."
        acciones={
          <Button variant="outline" onClick={() => descargarCsv('libro-sap.csv', plantilla)}>
            Descargar plantilla CSV
          </Button>
        }
      />
      <p className="text-ink-muted">
        Fechas: AAAA-MM-DD; importes en MXN con punto decimal. Clase: clave del catálogo.
        Responsable, sucursal, centro y equipo: identificadores del catálogo. Importado: no,
        temporal o definitivo. Arrendado: si o no. La carga no crea póliza.
      </p>
      <div className="grid gap-3 md:grid-cols-3">
        <label>
          Archivo CSV *
          <Input
            type="file"
            accept=".csv,text/csv"
            onChange={async (e) => {
              setResultado(undefined);
              const f = e.target.files?.[0];
              setErrorArchivo('');
              if (!f) {
                setCsv('');
                return;
              }
              if (f.size > 5_000_000) {
                setErrorArchivo('El archivo debe pesar menos de 5 MB.');
                setCsv('');
                return;
              }
              setCsv(await f.text());
            }}
          />
        </label>
        <label>
          Fecha de corte *
          <Input
            type="date"
            value={corte}
            onChange={(e) => {
              setCorte(e.target.value);
              setResultado(undefined);
            }}
          />
        </label>
        <label>
          Versión del archivo *
          <Input
            value={versionArchivo}
            onChange={(e) => {
              setVersion(e.target.value);
              setResultado(undefined);
            }}
          />
        </label>
      </div>
      <section className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
        <h2 className="text-lg font-semibold">Totales de la balanza SAP por clase</h2>
        {clases.data?.map((c) => (
          <div key={c.id} className="grid items-center gap-3 md:grid-cols-3">
            <span>
              {c.clave} · {c.nombre}
            </span>
            {(['valor', 'acumulada'] as const).map((k) => (
              <label key={k}>
                {k === 'valor' ? 'Valor original' : 'Depreciación acumulada'}
                <Input
                  type="number"
                  min="0"
                  step="0.01"
                  value={totales[c.clave]?.[k] ?? ''}
                  onChange={(e) => {
                    setResultado(undefined);
                    setTotales({
                      ...totales,
                      [c.clave]: {
                        valor: totales[c.clave]?.valor ?? 0,
                        acumulada: totales[c.clave]?.acumulada ?? 0,
                        [k]: Number(e.target.value),
                      },
                    });
                  }}
                />
              </label>
            ))}
          </div>
        ))}
      </section>
      <div className="flex gap-3">
        <Button
          variant="outline"
          disabled={!csv || !corte || !versionArchivo || cargar.isPending}
          title={
            !csv
              ? 'Seleccione el archivo'
              : cargar.isPending
                ? 'Procesando archivo'
                : !corte || !versionArchivo
                  ? 'Complete corte y versión'
                  : undefined
          }
          onClick={() => cargar.mutate(false)}
        >
          Prueba en seco
        </Button>
        <Button
          disabled={!resultado?.valida || !!resultado.cargaId || cargar.isPending}
          title={
            !resultado?.valida
              ? 'Ejecute una prueba sin errores primero'
              : resultado.cargaId
                ? 'El archivo ya fue cargado'
                : cargar.isPending
                  ? 'Procesando archivo'
                  : undefined
          }
          onClick={() => cargar.mutate(true)}
        >
          Confirmar carga
        </Button>
      </div>
      {(cargar.error || errorArchivo) && (
        <p role="alert" className="text-danger-fg">
          {cargar.error?.message || errorArchivo}
        </p>
      )}
      {resultado && (
        <>
          <Badge variant={resultado.valida ? 'success' : 'danger'}>
            {resultado.cargaId
              ? resultado.idempotente
                ? 'Archivo ya cargado'
                : 'Carga confirmada'
              : resultado.valida
                ? 'Validación correcta'
                : 'Corrija los errores antes de cargar'}
          </Badge>
          <TablaLibro
            titulo="Errores por renglón"
            filas={resultado.errores}
            columnas={[
              { clave: 'renglon', etiqueta: 'Renglón', tipo: 'numero' },
              { clave: 'mensaje', etiqueta: 'Error', tipo: 'texto' },
            ]}
          />
          <TablaLibro
            titulo="Cuadre del libro SAP"
            filas={resultado.cuadre}
            columnas={[
              { clave: 'clase', etiqueta: 'Clase', tipo: 'texto' },
              ...[
                'valor',
                'acumulada',
                'neto',
                'valorSap',
                'acumuladaSap',
                'diferenciaValor',
                'diferenciaAcumulada',
              ].map((clave) => ({
                clave,
                etiqueta: (
                  {
                    valor: 'Valor',
                    acumulada: 'Acumulada',
                    neto: 'Neto',
                    valorSap: 'Valor SAP',
                    acumuladaSap: 'Acumulada SAP',
                    diferenciaValor: 'Diferencia de valor',
                    diferenciaAcumulada: 'Diferencia de acumulada',
                  } as Record<string, string>
                )[clave],
                tipo: 'moneda' as const,
              })),
            ]}
          />
        </>
      )}
    </main>
  );
}
