import { useState } from 'react';
import { PageHeader } from '@/components/erp/PageHeader';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { useGuardarActivo } from '../api/hooks';
import { useLibroQuery } from '../api/libro';
import { TablaLibro } from '../components/TablaLibro';
export function InpcPage() {
  const [periodo, setPeriodo] = useState('');
  const [valor, setValor] = useState('');
  const [archivo, setArchivo] = useState<{ body: Blob; archivoHuella: string } | null>(null);
  const [errorArchivo, setErrorArchivo] = useState('');
  const q = useLibroQuery<{ id: string; anio: number; mes: number; valor: number }[]>('inpc', true);
  const guardar = useGuardarActivo();
  const importar = useGuardarActivo();
  return (
    <main className="space-y-5 text-sm">
      <PageHeader
        titulo="INPC mensual"
        descripcion="Capture los índices por empresa. Cada mes admite un registro. La carga no reemplaza índices existentes."
      />
      <form
        className="flex flex-wrap items-end gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
        onSubmit={(e) => {
          e.preventDefault();
          const [anio, mes] = periodo.split('-').map(Number);
          guardar.mutate(
            { ruta: 'inpc', body: { anio, mes, valor: Number(valor) } },
            {
              onSuccess: () => {
                setPeriodo('');
                setValor('');
              },
            },
          );
        }}
      >
        <label>
          Mes
          <Input
            required
            type="month"
            min="0001-01"
            max="9998-12"
            value={periodo}
            onChange={(e) => setPeriodo(e.target.value)}
          />
        </label>
        <label>
          Valor INPC
          <Input
            required
            type="number"
            min="0.000001"
            step="0.000001"
            value={valor}
            onChange={(e) => setValor(e.target.value)}
          />
        </label>
        <Button
          type="submit"
          disabled={guardar.isPending}
          title={guardar.isPending ? 'Guardando índice' : undefined}
        >
          Guardar índice
        </Button>
      </form>
      <form
        className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
        onSubmit={(e) => {
          e.preventDefault();
          if (!archivo) return;
          importar.mutate({ ruta: 'inpc/excel', ...archivo });
        }}
      >
        <label>
          Archivo Excel
          <Input
            required
            type="file"
            accept=".xlsx"
            onChange={async (e) => {
              setArchivo(null);
              setErrorArchivo('');
              const file = e.target.files?.[0];
              if (!file) return;
              if (file.size > 10 * 1024 * 1024) {
                setErrorArchivo('Máximo 10 MB.');
                return;
              }
              try {
                const form = new FormData();
                form.append('archivo', file);
                const body = await new Response(form).blob();
                const huella = await crypto.subtle.digest('SHA-256', await body.arrayBuffer());
                if (e.target.files?.[0] !== file) return;
                setArchivo({
                  body,
                  archivoHuella: Array.from(new Uint8Array(huella), (b) =>
                    b.toString(16).padStart(2, '0'),
                  ).join(''),
                });
              } catch {
                setErrorArchivo('No se pudo leer el archivo. Selecciónelo de nuevo.');
              }
            }}
          />
        </label>
        <p className="text-ink-muted">
          Dos columnas con encabezados: AAAA-MM (texto) y valor. Hasta 1200 meses y 10 MB, sin
          fórmulas.
        </p>
        <Button
          type="submit"
          disabled={importar.isPending || !archivo}
          title={
            importar.isPending
              ? 'Cargando índices'
              : !archivo
                ? 'Seleccione un Excel válido'
                : undefined
          }
        >
          Cargar Excel
        </Button>
        {importar.isSuccess && <p role="status">Índices cargados.</p>}
      </form>
      {errorArchivo && (
        <p role="alert" className="text-danger-fg">
          {errorArchivo}
        </p>
      )}
      {(guardar.error || importar.error || q.error) && (
        <p role="alert" className="text-danger-fg">
          {(guardar.error || importar.error || q.error)?.message}
        </p>
      )}
      <TablaLibro
        titulo="Índices capturados"
        filas={q.data}
        columnas={[
          { clave: 'anio', etiqueta: 'Año', tipo: 'numero' },
          { clave: 'mes', etiqueta: 'Mes', tipo: 'numero' },
          { clave: 'valor', etiqueta: 'INPC', tipo: 'numero', decimales: 6 },
        ]}
      />
    </main>
  );
}
