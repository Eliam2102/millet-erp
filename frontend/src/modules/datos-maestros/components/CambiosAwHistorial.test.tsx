import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { CambiosAwHistorial } from '@/modules/datos-maestros/components/CambiosAwHistorial';
import type { CambioMaestroAw } from '@/modules/datos-maestros/api/sincronizacion-aw';

const cambio: CambioMaestroAw = {
  id: 'x-1', tipoEntidad: 'Cliente', entidadId: 'c-1', referenciaExterna: 'AW-1', campo: 'Nombre de origen',
  valorAntes: 'DEMO UNO', valorDespues: 'DEMO DOS', dueno: 'AW', tipo: 'Aplicado', estado: 'Informativo',
  causa: 'Cambio recibido de A+W.', versionEvento: 'abcdef0123456789', versionMatriz: '0-propuesta',
  ejecucionId: null, ocurridoEnUtc: '2026-10-17T10:00:00Z', resolucion: null, resueltoEnUtc: null, version: 1,
};

describe('<CambiosAwHistorial>', () => {
  it('sin cambios muestra el estado vacío', () => {
    render(<CambiosAwHistorial cambios={[]} isLoading={false} isError={false} />);
    expect(screen.getByText('Sin cambios registrados desde A+W.')).toBeInTheDocument();
  });

  it('una fila Aplicado muestra antes, después y la versión de 8 caracteres', () => {
    render(<CambiosAwHistorial cambios={[cambio]} isLoading={false} isError={false} />);
    expect(screen.getByText('DEMO UNO')).toBeInTheDocument();
    expect(screen.getByText('DEMO DOS')).toBeInTheDocument();
    expect(screen.getByText('abcdef01')).toBeInTheDocument();
  });

  it('el error se anuncia con role alert', () => {
    render(<CambiosAwHistorial cambios={undefined} isLoading={false} isError />);
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });
});
