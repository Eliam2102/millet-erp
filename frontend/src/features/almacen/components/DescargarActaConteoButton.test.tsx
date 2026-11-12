import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { DescargarActaConteoButton } from '@/features/almacen/components/DescargarActaConteoButton';

const descargar = vi.hoisted(() => vi.fn().mockResolvedValue(undefined));
vi.mock('@/features/almacen/api/descargarComprobanteSalida', () => ({ descargarActaConteo: descargar }));

describe('DescargarActaConteoButton', () => {
  it('pide el acta del conteo', async () => {
    render(<DescargarActaConteoButton conteoId="c-1" />);
    fireEvent.click(screen.getByRole('button', { name: 'Descargar acta' }));
    await waitFor(() => expect(descargar).toHaveBeenCalledWith('c-1'));
  });
});
