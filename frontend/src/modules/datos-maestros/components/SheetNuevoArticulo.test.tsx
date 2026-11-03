import { describe, expect, it, vi } from 'vitest';
import { useEffect } from 'react';
import { render, screen } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevoArticuloProvider } from '@/modules/datos-maestros/components/SheetNuevoArticulo';
import { useNuevoArticulo } from '@/modules/datos-maestros/components/nuevo-articulo-context';

vi.mock('@tanstack/react-router', async () => {
  const actual =
    await vi.importActual<typeof import('@tanstack/react-router')>('@tanstack/react-router');
  return { ...actual, useNavigate: () => () => {} };
});

function AbrirAlMontar() {
  const { abrir } = useNuevoArticulo();
  useEffect(() => {
    abrir();
  }, [abrir]);
  return null;
}

describe('<SheetNuevoArticulo>', () => {
  it('las etiquetas quedan ligadas a sus controles', async () => {
    render(
      <NuevoArticuloProvider>
        <AbrirAlMontar />
      </NuevoArticuloProvider>,
      { wrapper: createQueryWrapper() },
    );

    expect(await screen.findByLabelText('Clave')).toBeInTheDocument();
    expect(screen.getByLabelText('Nombre')).toBeInTheDocument();
    expect(screen.getByLabelText('Unidad de medida')).toBeInTheDocument();
    expect(screen.getByLabelText('Naturaleza')).toBeInTheDocument();
  });
});
