import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { UsuarioSelector } from './UsuarioSelector';

// Sin el catálogo cargado (o sin permiso para leerlo) el usuario precargado no está en la lista.
vi.mock('@/features/catalogos/api', () => ({
  useUsuarios: () => ({ data: { items: [] }, isLoading: false, error: null }),
}));

describe('<UsuarioSelector>', () => {
  it('muestra el nombre precargado en lugar del GUID (O4)', () => {
    const id = 'e022dcd0-0000-4000-8000-000000000001';
    render(<UsuarioSelector value={id} onChange={() => {}} initialLabel="Jorge Canul" />);
    expect(screen.getByRole('combobox')).toHaveTextContent('Jorge Canul');
    expect(screen.queryByText(id)).toBeNull();
  });
});
