import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { NecesidadRqAppPanel } from './NecesidadRqAppPanel';
import type { NecesidadRqAppLinea } from '@/features/compras/api/types';

const linea = (o: Partial<NecesidadRqAppLinea> = {}): NecesidadRqAppLinea => ({
  lineaId: 'l1', posicion: 1, articuloId: 'a1', articuloClave: 'VID-6', articuloNombre: 'Vidrio 6 mm', unidadMedida: 'PZA',
  piezas: 10, necesidad: 10, disponible: 4, convertidoACompra: 10, reducido: 0, pendiente: 0, disponibleCubre: false, ...o,
});

describe('<NecesidadRqAppPanel>', () => {
  it('muestra necesidad, disponible, convertido y pendiente sin aviso si el disponible no cubre', () => {
    render(<NecesidadRqAppPanel lineas={[linea()]} />);
    expect(screen.getByText('VID-6 · Vidrio 6 mm')).toBeInTheDocument();
    expect(screen.queryByText('Hay existencia suficiente; Compras decide.')).not.toBeInTheDocument();
  });

  it('avisa que hay existencia suficiente cuando el disponible cubre la necesidad', () => {
    render(<NecesidadRqAppPanel lineas={[linea({ disponible: 10, convertidoACompra: 0, pendiente: 10, disponibleCubre: true })]} />);
    expect(screen.getByText('Hay existencia suficiente; Compras decide.')).toBeInTheDocument();
  });
});
