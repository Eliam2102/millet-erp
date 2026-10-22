import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { apiFetch } from '@/lib/auth/api-client';
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
  const [archivo, setArchivo] = useState<File>();
  const [hoja, setHoja] = useState('');
  const [corte, setCorte] = useState('2026-10-31');
  const [versionArchivo, setVersion] = useState('1');
  const [totales, setTotales] = useState<Record<string, { valor: number; acumulada: number }>>({});
  const [resultado, setResultado] = useState<CargaResultado>();
  const [errorArchivo, setErrorArchivo] = useState('');
  const clases = useClasesActivo();
  const qc = useQueryClient();
  const cargar = useMutation({
    mutationFn: async (confirmar: boolean) => {
      const form = new FormData();
      form.set('archivo', archivo!);
      form.set('corte', corte);
      form.set('versionArchivo', versionArchivo);
      form.set('hoja', hoja);
      form.set(
        'totales',
        JSON.stringify(Object.entries(totales).map(([clase, t]) => ({ clase, ...t }))),
      );
      // Fija el boundary para que los reintentos HTTP conserven el mismo cuerpo.
      const body = await new Response(form).blob();
      return (
        await apiRequest<CargaResultado>(`${AF_BASE}/carga/${confirmar ? 'confirmar' : 'previa'}`, {
          method: 'POST',
          body,
          idempotencyKey: confirmar ? crypto.randomUUID() : undefined,
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
          <div className="flex gap-2">
            <Button
              variant="outline"
              onClick={async () => {
                try {
                  const response = await apiFetch(`${AF_BASE}/carga/plantilla.xlsx`);
                  if (!response.ok) throw new Error('No se pudo descargar la plantilla Excel.');
                  const url = URL.createObjectURL(await response.blob());
                  const enlace = document.createElement('a');
                  enlace.href = url;
                  enlace.download = 'libro-sap.xlsx';
                  enlace.click();
                  URL.revokeObjectURL(url);
                } catch {
                  setErrorArchivo('No se pudo descargar la plantilla Excel. Intente de nuevo.');
                }
              }}
            >
              Descargar plantilla Excel
            </Button>
            <Button variant="outline" onClick={() => descargarCsv('libro-sap.csv', plantilla)}>
              Descargar plantilla CSV
            </Button>
          </div>
        }
      />
      <p className="text-ink-muted">
        Fechas: DD/MM/AAAA o AAAA-MM-DD; importes en MXN, por ejemplo $1,234.56. Clase y sucursal:
        clave o nombre. Centro de costo y equipo: código. Responsable opcional: correo o nombre de
        usuario. Importado: no, temporal o definitivo. Arrendado: sí o no. Elimine la fila EJEMPLO
        de la plantilla. La carga no crea póliza.
      </p>
      <div className="grid gap-3 md:grid-cols-3">
        <label>
          Archivo Excel o CSV *
          <Input
            disabled={cargar.isPending}
            type="file"
            accept=".xlsx,.csv"
            onChange={(e) => {
              setResultado(undefined);
              const f = e.target.files?.[0];
              setErrorArchivo('');
              if (!f) {
                setArchivo(undefined);
                return;
              }
              if (f.size > 10 * 1024 * 1024 || !/\.(xlsx|csv)$/i.test(f.name)) {
                setErrorArchivo('Seleccione un archivo .xlsx o .csv de hasta 10 MB.');
                setArchivo(undefined);
                return;
              }
              setArchivo(f);
            }}
          />
        </label>
        <label>
          Fecha de corte *
          <Input
            disabled={cargar.isPending}
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
            disabled={cargar.isPending}
            value={versionArchivo}
            onChange={(e) => {
              setVersion(e.target.value);
              setResultado(undefined);
            }}
          />
        </label>
      </div>
      <label className="block">
        Nombre de hoja (opcional; se usa la primera con datos)
        <Input
          value={hoja}
          disabled={cargar.isPending}
          onChange={(e) => {
            setHoja(e.target.value);
            setResultado(undefined);
          }}
        />
      </label>
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
                  disabled={cargar.isPending}
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
          disabled={!archivo || !corte || !versionArchivo || cargar.isPending}
          title={
            !archivo
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
              { clave: 'renglon', etiqueta: 'Fila', tipo: 'numero' },
              { clave: 'columna', etiqueta: 'Columna', tipo: 'texto' },
              { clave: 'mensaje', etiqueta: 'Error', tipo: 'texto' },
            ]}
          />
          <TablaLibro
            titulo="Avisos"
            filas={resultado.avisos ?? []}
            columnas={[
              { clave: 'renglon', etiqueta: 'Fila', tipo: 'numero' },
              { clave: 'columna', etiqueta: 'Columna', tipo: 'texto' },
              { clave: 'mensaje', etiqueta: 'Motivo', tipo: 'texto' },
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
