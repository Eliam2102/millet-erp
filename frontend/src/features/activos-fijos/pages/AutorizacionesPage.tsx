import { Link } from '@tanstack/react-router';
import { PageHeader } from '@/components/erp/PageHeader';
import { useLibroQuery, nombreOperacion, type Operacion } from '../api/libro';
import { ResolverOperacion } from '../components/ResolverOperacion';
import { TablaLibro } from '../components/TablaLibro';
export function AutorizacionesPage() {
  const q =
    useLibroQuery<{ operacion: Operacion; numero: string; descripcion: string }[]>(
      'autorizaciones',
    );
  const filas = q.data?.map((f) => ({
    ...f.operacion,
    numero: f.numero,
    descripcion: f.descripcion,
    tipoNombre: nombreOperacion[f.operacion.tipo] ?? f.operacion.tipo,
  }));
  return (
    <main className="flex flex-col gap-5 text-sm">
      <PageHeader
        titulo="Autorizaciones de activos"
        descripcion="Revise bajas, reversas y correcciones de capitalización pendientes."
      />
      {q.error && <p role="alert">{q.error.message}</p>}
      {q.data?.length === 0 && <p>No hay solicitudes pendientes.</p>}
      <TablaLibro
        titulo="Por autorizar"
        filas={filas}
        columnas={[
          { clave: 'numero', etiqueta: 'Activo', tipo: 'texto' },
          { clave: 'descripcion', etiqueta: 'Descripción', tipo: 'texto' },
          { clave: 'tipoNombre', etiqueta: 'Solicitud', tipo: 'texto' },
          { clave: 'fecha', etiqueta: 'Fecha', tipo: 'fecha' },
          { clave: 'motivo', etiqueta: 'Motivo', tipo: 'texto' },
          { clave: 'solicitadoPor', etiqueta: 'Área solicitante', tipo: 'texto' },
        ]}
        accionFila={(o) => (
          <div className="space-y-2">
            <Link
              to="/activos-fijos/activos/$id"
              params={{ id: o.activoId ?? '' }}
              className="text-brand"
            >
              Ver activo
            </Link>
            <ResolverOperacion operacion={o} />
          </div>
        )}
      />
    </main>
  );
}
