import { useContext, type Context, type ReactNode, type ComponentType } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  useSearch: () => ({}),
  useLocation: () => ({ pathname: '/' }),
}));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => false }));
vi.mock('@/features/cxc/pages/NuevaPropuestaAplicacion', () => ({
  NuevaPropuestaAplicacion: () => null,
}));
vi.mock('@/features/facturacion/pages/NuevaCaja', () => ({ NuevaCaja: () => null }));
import { NuevaSerieProvider } from '@/modules/administracion/components/SheetNuevaSerie';
import { NuevaSerieContext } from '@/modules/administracion/components/nueva-serie-context';
import { NuevaMonedaProvider } from '@/modules/catalogos/components/SheetNuevaMoneda';
import { NuevaMonedaContext } from '@/modules/catalogos/components/nueva-moneda-context';
import { NuevoClienteProvider } from '@/modules/datos-maestros/components/SheetNuevoCliente';
import { NuevoClienteContext } from '@/modules/datos-maestros/components/nuevo-cliente-context';
import { NuevoRolProvider } from '@/modules/identidad/components/SheetNuevoRol';
import { NuevoRolContext } from '@/modules/identidad/components/nuevo-rol-context';
import { NuevaPropuestaProvider } from '@/features/cxc/components/NuevaPropuestaProvider';
import { NuevaPropuestaContext } from '@/features/cxc/components/nueva-propuesta-context';
import { NuevaCajaProvider } from '@/features/facturacion/components/NuevaCajaProvider';
import { NuevaCajaContext } from '@/features/facturacion/components/nueva-caja-context';

interface Api {
  abrir: () => void;
  cerrar: (opts?: { force?: boolean }) => void;
  setDirty: (v: boolean) => void;
}
function Controles({ contexto }: { contexto: Context<Api | null> }) {
  const api = useContext(contexto)!;
  return (
    <>
      <button onClick={() => api.abrir()}>Abrir prueba</button>
      <button onClick={() => api.setDirty(true)}>Editar prueba</button>
      <button onClick={() => api.cerrar({ force: true })}>Éxito prueba</button>
    </>
  );
}
const casos: [string, ComponentType<{ children: ReactNode }>, Context<Api | null>][] = [
  ['NuevaSerie', NuevaSerieProvider, NuevaSerieContext],
  ['NuevaMoneda', NuevaMonedaProvider, NuevaMonedaContext],
  ['NuevoCliente', NuevoClienteProvider, NuevoClienteContext],
  ['NuevoRol', NuevoRolProvider, NuevoRolContext],
  ['NuevaPropuesta', NuevaPropuestaProvider, NuevaPropuestaContext],
  ['NuevaCaja', NuevaCajaProvider, NuevaCajaContext],
];
describe.each(casos)('%s — cierre con diálogo DS', (_nombre, Provider, contexto) => {
  it('sin cambios cierra; con cambios permite seguir, descartar y volver a abrir limpio', () => {
    render(
      <Provider>
        <Controles contexto={contexto} />
      </Provider>,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByText('Abrir prueba'));
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    fireEvent.click(screen.getByText('Abrir prueba'));
    fireEvent.click(screen.getByText('Editar prueba'));
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Seguir editando' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cerrar' }));
    fireEvent.click(screen.getByRole('button', { name: 'Descartar' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    fireEvent.click(screen.getByText('Abrir prueba'));
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
  it('éxito cierra sin preguntar aunque haya cambios', () => {
    render(
      <Provider>
        <Controles contexto={contexto} />
      </Provider>,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByText('Abrir prueba'));
    fireEvent.click(screen.getByText('Editar prueba'));
    fireEvent.click(screen.getByText('Éxito prueba'));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
