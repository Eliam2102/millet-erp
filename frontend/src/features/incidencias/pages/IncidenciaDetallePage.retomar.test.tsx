import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { IncidenciaDetallePage } from './IncidenciaDetallePage';
import { cerrarSesion, detalle, sesion } from '@/features/incidencias/test-utils';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useParams: () => ({ id: 'inc-1' }),
}));
vi.mock('@/components/erp/adjuntos/DocumentoAdjuntosSection', () => ({ DocumentoAdjuntosSection: () => null }));

const rutaProveedor = '/admin/datos-maestros/proveedores/prov-1';

function conIncidencia(rutaReanudacion: string) {
  mswServer.use(
    http.get('*/api/v1/incidencias/inc-1', () =>
      HttpResponse.json(
        detalle({
          modulo: 'datos_maestros',
          rutaReanudacion,
          pasoReanudacionNombre: 'Expediente del proveedor',
        }),
      ),
    ),
  );
}

afterEach(cerrarSesion);

// B10b · U-4: el responsable sin permiso de la pantalla del paso no recibe un botón que lo regresa a Inicio.
describe('<IncidenciaDetallePage> · Retomar en el paso', () => {
  it('sin permiso de Proveedores no ofrece «Retomar» y explica por qué', async () => {
    sesion([]);
    conIncidencia(rutaProveedor);
    render(<IncidenciaDetallePage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('No tienes acceso a la pantalla del paso')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Retomar en el paso/ })).not.toBeInTheDocument();
    expect(screen.getByRole('note')).toHaveTextContent('«Expediente del proveedor»');
  });

  it('con permiso de Proveedores lleva al expediente', async () => {
    sesion([P.DatosMaestrosProveedoresGestionar]);
    conIncidencia(rutaProveedor);
    render(<IncidenciaDetallePage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('link', { name: /Retomar en el paso/ })).toHaveAttribute('href', rutaProveedor);
    expect(screen.queryByText('No tienes acceso a la pantalla del paso')).not.toBeInTheDocument();
  });
});
