import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ImagenesProducto } from './imagenes-producto';

describe('ImagenesProducto', () => {
  afterEach(() => { TestBed.resetTestingModule(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });

  function crear(rutas: string[] = []) {
    const fixture = TestBed.createComponent(ImagenesProducto);
    fixture.componentRef.setInput('rutas', rutas);
    fixture.detectChanges();
    return fixture;
  }

  it('mantiene subir imágenes y tomar foto como acciones separadas', () => {
    const fixture = crear();
    const textos = [...fixture.nativeElement.querySelectorAll('button')].map((b: unknown) => (b as HTMLElement).textContent);
    expect(textos).toContain('Subir imágenes');
    expect(textos).toContain('Tomar foto');
    expect(fixture.nativeElement.querySelector('input').multiple).toBe(true);
  });

  it('agrega varias fotos, cambia la principal y libera las vistas previas', () => {
    vi.stubGlobal('URL', class extends URL {
      static override createObjectURL = vi.fn().mockReturnValueOnce('blob:1').mockReturnValueOnce('blob:2');
      static override revokeObjectURL = vi.fn();
    });
    const fixture = crear();
    const cambio = vi.fn();
    fixture.componentInstance.cambio.subscribe(cambio);
    const archivos = [new File(['a'], 'uno.jpg', { type: 'image/jpeg' }), new File(['b'], 'dos.png', { type: 'image/png' })];
    fixture.componentInstance['seleccionar']({ target: { files: archivos, value: 'archivo' } } as unknown as Event);
    fixture.componentInstance['elegirPrincipal'](1);
    expect(cambio).toHaveBeenLastCalledWith({ archivos, rutas: [], indicePrincipal: 1 });
    fixture.componentInstance['quitar'](0);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:1');
    fixture.destroy();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:2');
  });

  it('rechaza formatos inválidos y no agrega más de cinco fotos', () => {
    const fixture = crear(['/1', '/2', '/3', '/4', '/5']);
    const archivo = new File(['x'], 'foto.jpg', { type: 'image/jpeg' });
    fixture.componentInstance['seleccionar']({ target: { files: [archivo], value: '' } } as unknown as Event);
    expect(fixture.componentInstance['fotos']()).toHaveLength(5);
    expect(fixture.componentInstance['error']()).toContain('5 fotos');
    fixture.componentInstance['quitar'](0);
    fixture.componentInstance['seleccionar']({ target: { files: [new File(['x'], 'x.txt')], value: '' } } as unknown as Event);
    expect(fixture.componentInstance['error']()).toContain('JPG');
  });

  it('apaga la cámara si el permiso llega después de abandonar el editor', async () => {
    let resolver!: (stream: MediaStream) => void;
    const stop = vi.fn();
    vi.stubGlobal('navigator', { mediaDevices: { getUserMedia: () => new Promise<MediaStream>(resolve => resolver = resolve) } });
    const fixture = crear();
    const pendiente = fixture.componentInstance['abrirCamara']();
    fixture.destroy();
    resolver({ getTracks: () => [{ stop }] } as unknown as MediaStream);
    await pendiente;
    expect(stop).toHaveBeenCalledOnce();
  });

  it('explica el permiso rechazado y permite volver a intentar', async () => {
    vi.stubGlobal('navigator', { mediaDevices: { getUserMedia: vi.fn().mockRejectedValue(new Error('Permiso')) } });
    const fixture = crear();
    await fixture.componentInstance['abrirCamara']();
    expect(fixture.componentInstance['error']()).toContain('permiso');
    expect(fixture.componentInstance['camaraAbierta']()).toBe(false);
    expect(fixture.componentInstance['esperando']()).toBe(false);
  });
});
