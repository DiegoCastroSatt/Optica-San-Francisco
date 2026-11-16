import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

const { documentoMock, jsPdfMock, textosPdf } = vi.hoisted(() => {
  const textos: string[] = [];
  const documento = {
    internal: {
      pageSize: {
        getWidth: vi.fn(() => 595),
        getHeight: vi.fn(() => 842)
      }
    },
    setProperties: vi.fn(),
    setFont: vi.fn(),
    setFontSize: vi.fn(),
    setTextColor: vi.fn(),
    text: vi.fn((valor: string) => textos.push(valor)),
    setFillColor: vi.fn(),
    rect: vi.fn(),
    getTextWidth: vi.fn(() => 10),
    addPage: vi.fn(),
    getNumberOfPages: vi.fn(() => 1),
    setPage: vi.fn(),
    setDrawColor: vi.fn(),
    line: vi.fn(),
    output: vi.fn(() => new Blob(['PDF']))
  };

  return {
    documentoMock: documento,
    jsPdfMock: vi.fn(function () {
      return documento;
    }),
    textosPdf: textos
  };
});

vi.mock('jspdf', () => ({ jsPDF: jsPdfMock }));

import { ConsultarStock } from './consultar-stock';
import { BuscarProductoService, Producto } from '../buscar-producto/buscar-producto.service';

describe('ConsultarStock', () => {
  let component: ConsultarStock;
  let fixture: ComponentFixture<ConsultarStock>;
  let actualizarStock: ReturnType<typeof vi.fn>;
  const productos: Producto[] = [
    {
      id: 1, codigo: 'AR-001', nombre: 'Armazón clásico', marca: 'Óptica', modelo: 'C1',
      color: 'Negro', categoria: 'Armazones', precio: 25000, stock: 4, stockMinimo: 1,
      estado: 'Disponible', tieneVentas: false
    },
    {
      id: 2, codigo: 'LE-002', nombre: 'Lentes de sol', marca: 'Óptica', modelo: 'S2',
      color: 'Negro', categoria: 'Lentes de sol', precio: 32000, stock: 0, stockMinimo: 1,
      estado: 'Agotado', tieneVentas: false
    }
  ];

  beforeEach(async () => {
    textosPdf.length = 0;
    vi.clearAllMocks();
    documentoMock.output.mockImplementation(() => new Blob(['PDF']));
    actualizarStock = vi.fn((id: number, stock: number, stockMinimo: number) =>
      of({ ...productos.find(producto => producto.id === id)!, stock, stockMinimo })
    );
    await TestBed.configureTestingModule({
      imports: [ConsultarStock],
      providers: [{
        provide: BuscarProductoService,
        useValue: { buscar: () => of(productos), actualizarStock }
      }]
    }).compileComponents();

    fixture = TestBed.createComponent(ConsultarStock);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('muestra las existencias y señala productos sin stock', () => {
    const tabla = fixture.nativeElement as HTMLElement;

    expect(tabla.textContent).toContain('4 unidades');
    expect(tabla.textContent).toContain('Sin stock');
    expect(tabla.textContent).toContain('AR-001');
    expect(tabla.textContent).toContain('LE-002');
  });

  it('filtra productos por nombre o código sin ocultar el estado de stock', () => {
    const input = fixture.nativeElement.querySelector('#buscar-stock') as HTMLInputElement;
    input.value = 'LE-002';
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const texto = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(texto).toContain('Sin stock');
    expect(texto).not.toContain('AR-001');
  });

  it('actualiza existencias y stock mínimo desde el listado', () => {
    component['editarStock'](productos[0]);
    component['borradorStock'].set({ stock: 7, stockMinimo: 2 });
    component['motivoCambio'].set('Ajuste por inventario');
    component['guardarStock'](productos[0]);
    fixture.detectChanges();

    expect(actualizarStock).toHaveBeenCalledWith(1, 7, 2, 'Ajuste por inventario');
    expect(component['productos']()[0]).toMatchObject({ stock: 7, stockMinimo: 2 });
    expect(component['productoEditando']()).toBeNull();
  });

  it('genera la vista previa y clasifica el inventario según el stock mínimo', async () => {
    const inventario: Producto[] = [
      { ...productos[0], id: 11, codigo: 'DIS-011', nombre: 'Disponible', stock: 5, stockMinimo: 2 },
      { ...productos[0], id: 12, codigo: 'BAJ-012', nombre: 'Stock igual al mínimo', stock: 2, stockMinimo: 2 },
      { ...productos[0], id: 13, codigo: 'BAJ-013', nombre: 'Stock bajo', stock: 1, stockMinimo: 2 },
      { ...productos[0], id: 14, codigo: 'AGO-014', nombre: 'Agotado', stock: 0, stockMinimo: 2 },
      { ...productos[0], id: 15, codigo: 'AGO-015', nombre: 'Stock negativo', stock: -1, stockMinimo: 2 }
    ];
    component['productos'].set(inventario);
    const ventana = {
      opener: window,
      location: { href: '' },
      close: vi.fn()
    } as unknown as Window;
    vi.spyOn(window, 'open').mockReturnValue(ventana);
    const crearObjectUrl = vi.fn(() => 'blob:reporte-stock');
    vi.stubGlobal('URL', {
      createObjectURL: crearObjectUrl,
      revokeObjectURL: vi.fn()
    });

    component['mostrarInforme']();
    await vi.waitFor(() => expect(ventana.location.href).toBe('blob:reporte-stock'));

    expect(ventana.opener).toBeNull();
    expect(documentoMock.setProperties).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Reporte de inventario - Óptica San Francisco'
    }));
    expect(textosPdf).toEqual(expect.arrayContaining([
      'PRODUCTOS', '5', 'DISPONIBLES', '1', 'STOCK BAJO', '2', 'AGOTADOS', '2',
      'Productos disponibles (1)', 'Productos con stock bajo (2)', 'Productos agotados (2)',
      'Disponible', 'Stock igual al mínimo', 'Stock bajo', 'Agotado', 'Stock negativo'
    ]));
    expect(textosPdf.indexOf('Disponible')).toBeLessThan(textosPdf.indexOf('Stock igual al mínimo'));
    expect(textosPdf.indexOf('Stock igual al mínimo')).toBeLessThan(textosPdf.indexOf('Agotado'));
    expect(textosPdf.indexOf('Agotado')).toBeLessThan(textosPdf.indexOf('Stock negativo'));
    expect(crearObjectUrl).toHaveBeenCalledWith(expect.any(Blob));
  });

  it('descarga el PDF con un nombre de archivo que incluye la fecha', async () => {
    const crearObjectUrl = vi.fn(() => 'blob:reporte-stock');
    vi.stubGlobal('URL', {
      createObjectURL: crearObjectUrl,
      revokeObjectURL: vi.fn()
    });
    let enlaceDescarga: HTMLAnchorElement | undefined;
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      enlaceDescarga = this;
    });

    component['descargarInforme']();
    await vi.waitFor(() => expect(enlaceDescarga).toBeDefined());

    expect(enlaceDescarga?.href).toBe('blob:reporte-stock');
    expect(enlaceDescarga?.download).toMatch(/^reporte-inventario-\d{4}-\d{2}-\d{2}\.pdf$/);
    expect(crearObjectUrl).toHaveBeenCalledWith(expect.any(Blob));
  });

  it('muestra un error cuando el navegador bloquea la ventana emergente', () => {
    vi.spyOn(window, 'open').mockReturnValue(null);

    component['mostrarInforme']();

    expect(component['errorPdf']()).toBe(
      'No se pudo abrir el PDF. Permite las ventanas emergentes e inténtalo nuevamente.'
    );
    expect(component['generandoPdf']()).toBe(false);
    expect(jsPdfMock).not.toHaveBeenCalled();
  });

  it('cierra la ventana y muestra un error si no puede generar el PDF', async () => {
    const ventana = {
      opener: window,
      location: { href: '' },
      close: vi.fn()
    } as unknown as Window;
    vi.spyOn(window, 'open').mockReturnValue(ventana);
    vi.mocked(documentoMock.output).mockImplementation(() => {
      throw new Error('Error de generación');
    });

    component['mostrarInforme']();
    await vi.waitFor(() => expect(component['errorPdf']()).toBe(
      'No se pudo generar el reporte PDF. Intenta nuevamente.'
    ));

    expect(ventana.close).toHaveBeenCalled();
    expect(component['generandoPdf']()).toBe(false);
  });
});
