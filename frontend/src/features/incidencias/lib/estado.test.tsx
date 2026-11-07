import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { EstadoIncidenciaBadge } from './estado';
import { fechaCompromisoTexto, folioIncidencia, origenIncidencia, rutaInterna } from './formato';
import { IncidenciasSearchSchema, estadosDeVista } from './search-schema';
import { nombreModulo } from './modulos';

describe('utilidades de incidencias', () => {
  it('badge con el texto y tono de cada estado', () => {
    render(<EstadoIncidenciaBadge estado="EnAtencion" />);
    const badge = screen.getByText('En atención');
    expect(badge).toHaveAttribute('data-estado', 'EnAtencion');
    expect(badge.className).toContain('bg-info-bg');
  });

  it('folio, origen y fecha compromiso sin desfase de zona', () => {
    expect(folioIncidencia(45)).toBe('INC-45');
    expect(origenIncidencia({ automatica: true, abiertaPorNombre: 'Sistema' })).toBe('Automática');
    expect(origenIncidencia({ automatica: false, abiertaPorNombre: 'Ana' })).toBe('Registrada por Ana');
    expect(fechaCompromisoTexto('2026-11-07')).toBe('07/11/2026');
    expect(fechaCompromisoTexto(null)).toBe('—');
  });

  it('solo sigue rutas internas de la app', () => {
    expect(rutaInterna('/almacen/recepciones/1')).toBe('/almacen/recepciones/1');
    expect(rutaInterna('https://otro.sitio')).toBeNull();
    expect(rutaInterna('//otro.sitio')).toBeNull();
    expect(rutaInterna(null)).toBeNull();
  });

  it('vistas de la bandeja y search inválido', () => {
    expect(estadosDeVista(undefined)).toBeUndefined();
    expect(estadosDeVista('EnAtencion')).toEqual(['EnAtencion']);
    expect(estadosDeVista('todas')).toHaveLength(4);
    expect(IncidenciasSearchSchema.parse({ vista: 'x', modulo: 'Almacén!' })).toEqual({});
  });

  it('nombre visible del módulo', () => {
    expect(nombreModulo('cuentas_por_pagar')).toBe('CxP');
    expect(nombreModulo('nuevo_modulo')).toBe('Nuevo modulo');
  });
});
