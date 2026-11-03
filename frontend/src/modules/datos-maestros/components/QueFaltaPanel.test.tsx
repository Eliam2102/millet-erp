import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { QueFaltaPanel } from './QueFaltaPanel';

describe('QueFaltaPanel', () => {
  it('no pinta nada cuando no falta nada', () => {
    const { container } = render(<QueFaltaPanel items={[]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('lista lo que falta con el conteo y enlaza a secciones', () => {
    render(
      <QueFaltaPanel
        items={[{ etiqueta: 'Contrato', href: '#expediente-proveedor' }, { etiqueta: 'Constancia' }]}
        nota="Cargado desde SAP sin documentos."
      />,
    );
    const region = screen.getByRole('region', { name: 'Qué falta para completar' });
    expect(region).toHaveTextContent('Qué falta para completar (2)');
    expect(screen.getByRole('link', { name: 'Contrato' })).toHaveAttribute('href', '#expediente-proveedor');
    expect(region).toHaveTextContent('Constancia');
    expect(region).toHaveTextContent('Cargado desde SAP sin documentos.');
  });

  it('el botón de un campo lleva el foco a su control', () => {
    Element.prototype.scrollIntoView = () => {};
    render(
      <>
        <QueFaltaPanel items={[{ etiqueta: 'RFC', campo: 'rfc' }]} />
        <div id="campo-rfc">
          <label htmlFor="rfc-input">RFC</label>
          <input id="rfc-input" />
        </div>
      </>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'RFC' }));
    expect(screen.getByLabelText('RFC')).toHaveFocus();
  });
});
