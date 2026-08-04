import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { UbicacionSelector } from '@/components/erp/selectors/UbicacionSelector';
import type { UbicacionListItem } from '@/features/almacen/api/types';

// Mock del hook de datos: aísla el test del react-query.
const ubicaciones: UbicacionListItem[] = [
  {
    id: 'ub-a',
    subAlmacenId: 'sa-a',
    clave: 'ÚNICA',
    nombre: 'Ubicación única',
    estatus: 0,
    esDefault: true,
    subAlmacenClave: 'INS-A',
    subAlmacenNombre: 'Insumos A',
    almacenClave: 'ALM-A',
    almacenNombre: 'Almacén A',
  },
  {
    id: 'ub-b',
    subAlmacenId: 'sa-b',
    clave: 'ÚNICA',
    nombre: 'Ubicación única',
    estatus: 0,
    esDefault: true,
    subAlmacenClave: 'INS-B',
    subAlmacenNombre: 'Insumos B',
    almacenClave: 'ALM-B',
    almacenNombre: 'Almacén B',
  },
];

vi.mock('@/features/almacen/api/useUbicaciones', () => ({
  useUbicaciones: () => ({ data: { items: ubicaciones }, isLoading: false }),
}));

describe('<UbicacionSelector>', () => {
  it('el trigger muestra la ruta del padre para distinguir las "ÚNICA"', () => {
    // Dos ubicaciones con la MISMA clave "ÚNICA"; el value apunta a la de ALM-A.
    render(<UbicacionSelector value="ub-a" onChange={() => {}} />);

    const trigger = screen.getByRole('combobox', {
      name: /Seleccionar ubicación/i,
    });
    // Se distingue por almacén › sub-almacén, no solo "ÚNICA".
    expect(trigger).toHaveTextContent('ALM-A › INS-A · ÚNICA');
    expect(trigger).not.toHaveTextContent('ALM-B');
  });

  it('sin selección muestra el placeholder', () => {
    render(<UbicacionSelector value={null} onChange={() => {}} />);
    expect(
      screen.getByRole('combobox', { name: /Seleccionar ubicación/i }),
    ).toHaveTextContent(/Selecciona ubicación/i);
  });
});
