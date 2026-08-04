import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import {
  buildNuevaRequisicionDraftKey,
  clearDraft,
  readDraft,
  useDraftPersist,
  useDraftRecovery,
} from '@/features/compras/lib/draft-storage';

beforeEach(() => {
  window.localStorage.clear();
});

afterEach(() => {
  window.localStorage.clear();
  vi.useRealTimers();
});

describe('buildNuevaRequisicionDraftKey', () => {
  it('construye la key estándar con userId + empresaId', () => {
    expect(buildNuevaRequisicionDraftKey('u-1', 'e-1')).toBe(
      'compras:rq:draft:nueva:u-1:e-1',
    );
  });

  it('null si falta cualquier id', () => {
    expect(buildNuevaRequisicionDraftKey(null, 'e-1')).toBeNull();
    expect(buildNuevaRequisicionDraftKey('u-1', null)).toBeNull();
    expect(buildNuevaRequisicionDraftKey(undefined, undefined)).toBeNull();
  });
});

describe('readDraft / clearDraft', () => {
  it('readDraft devuelve null cuando no hay draft', () => {
    expect(readDraft('test-key')).toBeNull();
  });

  it('readDraft parsea el shape { values, meta }', () => {
    window.localStorage.setItem(
      'k',
      JSON.stringify({
        values: { foo: 'bar' },
        meta: { savedAt: '2026-05-09T10:00:00Z' },
      }),
    );
    const draft = readDraft<{ foo: string }>('k');
    expect(draft).toEqual({
      values: { foo: 'bar' },
      meta: { savedAt: '2026-05-09T10:00:00Z' },
    });
  });

  it('readDraft devuelve null si el JSON no tiene meta.savedAt', () => {
    window.localStorage.setItem('k', JSON.stringify({ values: {} }));
    expect(readDraft('k')).toBeNull();
  });

  it('readDraft devuelve null si el JSON está corrupto', () => {
    window.localStorage.setItem('k', '{not-json');
    expect(readDraft('k')).toBeNull();
  });

  it('clearDraft borra la key del storage', () => {
    window.localStorage.setItem('k', '{"a":1}');
    clearDraft('k');
    expect(window.localStorage.getItem('k')).toBeNull();
  });
});

describe('useDraftPersist', () => {
  it('persiste con debounce 500ms cuando enabled', () => {
    vi.useFakeTimers();
    const { rerender } = renderHook(
      ({ values, enabled }) =>
        useDraftPersist('k-test', values, enabled),
      { initialProps: { values: { x: 1 }, enabled: true } },
    );

    act(() => vi.advanceTimersByTime(400));
    expect(window.localStorage.getItem('k-test')).toBeNull();

    act(() => vi.advanceTimersByTime(200));
    const stored = JSON.parse(window.localStorage.getItem('k-test')!);
    expect(stored.values).toEqual({ x: 1 });

    rerender({ values: { x: 2 }, enabled: true });
    act(() => vi.advanceTimersByTime(500));
    const updated = JSON.parse(window.localStorage.getItem('k-test')!);
    expect(updated.values).toEqual({ x: 2 });
  });

  it('NO persiste cuando enabled=false', () => {
    vi.useFakeTimers();
    renderHook(() =>
      useDraftPersist('k-test', { x: 1 }, /* enabled */ false),
    );
    act(() => vi.advanceTimersByTime(1000));
    expect(window.localStorage.getItem('k-test')).toBeNull();
  });

  it('cancela el timer al desmontar', () => {
    vi.useFakeTimers();
    const { unmount } = renderHook(() =>
      useDraftPersist('k-test', { x: 1 }, true),
    );
    unmount();
    act(() => vi.advanceTimersByTime(1000));
    expect(window.localStorage.getItem('k-test')).toBeNull();
  });
});

describe('useDraftRecovery', () => {
  it('lee el draft existente al montar', () => {
    window.localStorage.setItem(
      'k',
      JSON.stringify({
        values: { hola: 'mundo' },
        meta: { savedAt: '2026-05-09T10:00:00Z' },
      }),
    );
    const { result } = renderHook(() =>
      useDraftRecovery<{ hola: string }>('k'),
    );
    expect(result.current.draft?.values).toEqual({ hola: 'mundo' });
  });

  it('acknowledge limpia el state pero NO el storage', () => {
    window.localStorage.setItem(
      'k',
      JSON.stringify({
        values: { x: 1 },
        meta: { savedAt: '2026-05-09T10:00:00Z' },
      }),
    );
    const { result } = renderHook(() =>
      useDraftRecovery<{ x: number }>('k'),
    );
    act(() => result.current.acknowledge());
    expect(result.current.draft).toBeNull();
    expect(window.localStorage.getItem('k')).not.toBeNull();
  });

  it('discard limpia state Y storage', () => {
    window.localStorage.setItem(
      'k',
      JSON.stringify({
        values: { x: 1 },
        meta: { savedAt: '2026-05-09T10:00:00Z' },
      }),
    );
    const { result } = renderHook(() =>
      useDraftRecovery<{ x: number }>('k'),
    );
    act(() => result.current.discard());
    expect(result.current.draft).toBeNull();
    expect(window.localStorage.getItem('k')).toBeNull();
  });

  it('key=null devuelve draft=null sin tocar storage', () => {
    const { result } = renderHook(() => useDraftRecovery(null));
    expect(result.current.draft).toBeNull();
  });
});
