import { FormBuilder } from '@angular/forms';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, Subject, throwError } from 'rxjs';
import { BuscarProductoComponent } from './buscar-producto';
import { BuscarProductoService, Producto } from './buscar-producto.service';
import { ResenasProductoService } from './resenas-producto.service';
import { AutenticacionClienteService } from '../../shared/services/autenticacion-cliente.service';

const productos: Producto[] = [
  {
    id: 1, codigo: 'SOL-001', nombre: 'Lentes de sol', marca: 'Vogue', modelo: 'V1',
    color: 'Negro', categoria: 'Lentes de sol', precio: 30000, stock: 4, stockMinimo: 1,
    estado: 'Disponible', tieneVentas: false, rutaImagen: '/uploads/productos/sol.jpg'
  },
  {
    id: 2, codigo: 'OPT-002', nombre: 'Armazon clasico', marca: 'Ray-Ban', modelo: 'R2',
    color: 'Dorado', categoria: 'Armazones', precio: 15000, stock: 8, stockMinimo: 2,
    estado: 'Disponible', tieneVentas: false
  }
];

const productoAgotado: Producto = {
  id: 3, codigo: 'OPT-003', nombre: 'Armazón agotado', marca: 'Vogue', modelo: 'V3',
  color: 'Negro', categoria: 'Armazones', precio: 18000, stock: 0, stockMinimo: 1,
  estado: 'Agotado', tieneVentas: false
};

describe('BuscarProductoComponent', () => {
  let component: BuscarProductoComponent;
  let fixture: ComponentFixture<BuscarProductoComponent>;
  let service: {
    buscarPagina: ReturnType<typeof vi.fn>;
    actualizar: ReturnType<typeof vi.fn>;
    solicitarAvisoReposicion: ReturnType<typeof vi.fn>;
  };
  let resenasService: {
    obtenerPorProducto: ReturnType<typeof vi.fn>;
    obtenerMisProductos: ReturnType<typeof vi.fn>;
    publicar: ReturnType<typeof vi.fn>;
  };
  let clienteActual: { idCliente: number; nombre: string; correo: string } | null;

  beforeEach(async () => {
    clienteActual = null;
    service = {
      buscarPagina: vi.fn(({ estado, categoria, color, marca, ordenPrecio }) => {
        const filtrados = productos.filter(p => (!estado || p.estado === estado) &&
          (!categoria || p.categoria === categoria) && (!color || p.color === color) && (!marca || p.marca === marca));
        if (ordenPrecio) filtrados.sort((a, b) => ordenPrecio === 'menor' ? a.precio - b.precio : b.precio - a.precio);
        return of({ productos: filtrados, haySiguiente: false, categorias: ['Armazones', 'Lentes de sol'],
          colores: ['Dorado', 'Negro'], marcas: ['Ray-Ban', 'Vogue'] });
      }),
      actualizar: vi.fn((id: number, producto: Producto) => of({ ...productos[0], ...producto, id })),
      solicitarAvisoReposicion: vi.fn(() => of({
        idAviso: 5,
        idProducto: productoAgotado.id,
        nombreProducto: productoAgotado.nombre,
        fecha: '2026-11-23T12:00:00Z',
        estado: 'Abierta'
      }))
    };
    resenasService = {
      obtenerPorProducto: vi.fn(() => of({ resumen: { promedio: 0, cantidad: 0 }, resenas: [] })),
      obtenerMisProductos: vi.fn(() => of([])),
      publicar: vi.fn(() => of({ id: 5, idProducto: 1, calificacion: 5, comentario: 'Muy cómodos', nombreCliente: 'Ana P.', fechaCreacionUtc: '2026-10-01T12:00:00Z' }))
    };

    await TestBed.configureTestingModule({
      imports: [BuscarProductoComponent],
      providers: [
        FormBuilder,
        provideRouter([]),
        { provide: BuscarProductoService, useValue: service },
        { provide: ResenasProductoService, useValue: resenasService },
        { provide: AutenticacionClienteService, useValue: {
          cliente: () => clienteActual,
          verificarSesion: () => throwError(() => new Error('sin sesión'))
        } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(BuscarProductoComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('carga productos y crea opciones unicas para categoria y color', () => {
    expect(component['productosVisibles']()).toEqual(productos);
    expect(component['filtrosForm'].controls.estado.value).toBe('');
    expect(component['categorias']()).toEqual(['Armazones', 'Lentes de sol']);
    expect(component['colores']()).toEqual(['Dorado', 'Negro']);
    expect(service.buscarPagina).toHaveBeenCalledWith(expect.objectContaining({ termino: '', pagina: 1 }));
  });

  it('filtra por categoria y color y ordena por precio ascendente', () => {
    component['filtrosForm'].setValue({ estado: 'Disponible', categoria: 'Armazones', color: 'Dorado', marca: 'Ray-Ban', ordenPrecio: 'menor' });
    component['aplicarFiltros']();

    expect(component['productosVisibles']().map((producto: Producto) => producto.id)).toEqual([2]);
    expect(component['mensaje']()).toBe('');
  });

  it('ordena los resultados de mayor a menor precio', () => {
    component['filtrosForm'].patchValue({ ordenPrecio: 'mayor' });
    component['aplicarFiltros']();

    expect(component['productosVisibles']().map((producto: Producto) => producto.precio)).toEqual([30000, 15000]);
  });

  it('informa cuando los filtros no encuentran productos', () => {
    component['filtrosForm'].patchValue({ categoria: 'Accesorios' });
    component['aplicarFiltros']();

    expect(component['productosVisibles']()).toEqual([]);
    expect(component['mensaje']()).toContain('No hay existencias');
  });

  it('normaliza rutas relativas y conserva URLs absolutas', () => {
    expect(component['imageUrl']('/uploads/a.jpg')).toBe('http://localhost:8080/uploads/a.jpg');
    expect(component['imageUrl']('uploads/a.jpg')).toBe('http://localhost:8080/uploads/a.jpg');
    expect(component['imageUrl']('https://cdn.example/a.jpg')).toBe('https://cdn.example/a.jpg');
    expect(component['imageUrl']()).toBe('');
  });

  it('guarda los cambios del popup con la imagen seleccionada', () => {
    const image = new File(['image'], 'nuevo.jpg', { type: 'image/jpeg' });
    component['abrirDetalle'](productos[0]);
    component['activarEdicion']();
    component['productoForm'].patchValue({ nombre: 'Lentes actualizados' });
    component['galeria'] = { archivos: [image], rutas: [], indicePrincipal: 0 };
    component['guardarCambios']();

    expect(service.actualizar).toHaveBeenCalledWith(1, expect.objectContaining({
      nombre: 'Lentes actualizados',
      galeria: { archivos: [image], rutas: [], indicePrincipal: 0 }
    }));
    expect(component['editando']()).toBe(false);
    expect(component['guardando']()).toBe(false);
  });

  it('no permite editar stock en el formulario administrativo y conserva sus valores', () => {
    fixture.componentRef.setInput('esAdministrador', true);
    component['abrirDetalle'](productos[0]);
    component['activarEdicion']();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[formControlName="stock"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[formControlName="stockMinimo"]')).toBeNull();

    component['guardarCambios']();

    expect(service.actualizar).toHaveBeenCalledWith(1, expect.objectContaining({ stock: 4, stockMinimo: 1 }));
  });

  it('muestra un error cuando falla la consulta', () => {
    service.buscarPagina.mockReturnValueOnce(throwError(() => new Error('offline')));
    component['buscar']();

    expect(component['error']()).toBe('No se pudo consultar el catálogo.');
    expect(component['cargando']()).toBe(false);
  });

  it('carga reseñas al abrir el detalle de un producto', () => {
    component['abrirDetalle'](productos[0]);

    expect(resenasService.obtenerPorProducto).toHaveBeenCalledWith(1);
    expect(component['resumenResenas']()).toEqual({ promedio: 0, cantidad: 0 });
  });

  it('permite solicitar aviso para un producto agotado y evita solicitudes duplicadas mientras espera', () => {
    clienteActual = { idCliente: 4, nombre: 'Cliente de prueba', correo: 'cliente@example.invalid' };
    const respuesta = new Subject<{
      idAviso: number;
      idProducto: number;
      nombreProducto: string;
      fecha: string;
      estado: string;
    }>();
    service.solicitarAvisoReposicion.mockReturnValueOnce(respuesta);

    component['abrirDetalle'](productoAgotado);
    fixture.detectChanges();
    const boton = fixture.nativeElement.querySelector('.restock-notice button') as HTMLButtonElement;
    expect(boton).toBeTruthy();

    boton.click();
    fixture.detectChanges();
    expect(service.solicitarAvisoReposicion).toHaveBeenCalledTimes(1);
    expect(service.solicitarAvisoReposicion).toHaveBeenCalledWith(productoAgotado.id);
    expect(boton.disabled).toBe(true);
    expect(boton.textContent).toContain('Registrando solicitud');

    component['solicitarAvisoReposicion'](productoAgotado.id);
    expect(service.solicitarAvisoReposicion).toHaveBeenCalledTimes(1);

    respuesta.next({
      idAviso: 5,
      idProducto: productoAgotado.id,
      nombreProducto: productoAgotado.nombre,
      fecha: '2026-11-23T12:00:00Z',
      estado: 'Abierta'
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.restock-notice button')).toBeNull();
    expect(fixture.nativeElement.querySelector('.restock-notice [role="status"]')?.textContent)
      .toContain('Solicitud registrada');
  });

  it('muestra el error de la API y permite reintentar la solicitud', () => {
    clienteActual = { idCliente: 4, nombre: 'Cliente de prueba', correo: 'cliente@example.invalid' };
    service.solicitarAvisoReposicion.mockReturnValueOnce(
      throwError(() => ({ error: { mensaje: 'Ya tienes una solicitud abierta para este producto.' } }))
    );

    component['abrirDetalle'](productoAgotado);
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.restock-notice button') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.restock-notice [role="alert"]')?.textContent)
      .toContain('Ya tienes una solicitud abierta');
    const botonReintentar = fixture.nativeElement.querySelector('.restock-notice button') as HTMLButtonElement;
    expect(botonReintentar).toBeTruthy();
    expect(component['enviandoAvisoReposicion']()).toBe(false);

    botonReintentar.click();
    fixture.detectChanges();

    expect(service.solicitarAvisoReposicion).toHaveBeenCalledTimes(2);
    expect(fixture.nativeElement.querySelector('.restock-notice [role="status"]')?.textContent)
      .toContain('Solicitud registrada');
  });

  it('no ofrece ni envía una solicitud de reposición a una sesión no autenticada', () => {
    component['abrirDetalle'](productoAgotado);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.restock-notice button')).toBeNull();
    expect(fixture.nativeElement.querySelector('.restock-notice a')?.getAttribute('href'))
      .toBe('/cliente/login');

    component['solicitarAvisoReposicion'](productoAgotado.id);
    expect(service.solicitarAvisoReposicion).not.toHaveBeenCalled();
  });
});
