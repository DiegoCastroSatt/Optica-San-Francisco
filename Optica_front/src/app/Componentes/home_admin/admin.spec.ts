import { AutenticacionAdminService } from '../../shared/services/autenticacion-admin.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AdminLayoutComponent } from './admin';
import { NotificacionesAdminService } from '../../shared/services/notificaciones-admin.service';

describe('AdminLayoutComponent', () => {
  let component: AdminLayoutComponent;
  let fixture: ComponentFixture<AdminLayoutComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminLayoutComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
    TestBed.inject(AutenticacionAdminService).administrador.set({ idAdministrador: 1, nombre: 'Administrador', tipoUsuario: 'Administrador' });
    fixture = TestBed.createComponent(AdminLayoutComponent);
    component = fixture.componentInstance;
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://localhost:8080/api/Productos/stock-bajo').flush([]);
    http.expectOne('http://localhost:8080/api/AvisosReposicion').flush([]);
    await fixture.whenStable();
  });

  afterEach(() => {
    fixture.destroy();
    TestBed.inject(HttpTestingController).verify();
    vi.useRealTimers();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('ofrece Crear Pedido, Lista de Pedidos e Historial desde el menú Pedidos', () => {
    const menus = fixture.nativeElement.querySelectorAll('.inventory-menu') as NodeListOf<HTMLElement>;
    const menu = Array.from(menus)
      .find(element => element.querySelector('button')?.textContent?.includes('Pedidos'));
    expect(menu).toBeDefined();
    const enlaces = Array.from(menu!.querySelectorAll('.inventory-submenu a')) as HTMLAnchorElement[];

    expect(enlaces.map(enlace => enlace.textContent?.trim())).toEqual([
      'Crear Pedido',
      'Lista de Pedidos',
      'Historial'
    ]);
    expect(enlaces.map(enlace => enlace.getAttribute('href'))).toEqual([
      '/admin/panel/crear-pedido',
      '/admin/panel/pedido',
      '/admin/panel/pedidos/historial'
    ]);
  });

  it('mantiene alertas de stock bajo hasta que la API informa una reposición', async () => {
    await vi.advanceTimersByTimeAsync(15_000);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('http://localhost:8080/api/Productos/stock-bajo').flush([
      { idProducto: 1, nombre: 'Armazón clásico', stock: 3, stockMinimo: 3 },
    ]);
    http.expectOne('http://localhost:8080/api/AvisosReposicion').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const boton = fixture.nativeElement.querySelector('.boton-notificaciones') as HTMLButtonElement;
    expect(boton.getAttribute('aria-label')).toBe('Notificaciones: 1');
    boton.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.panel-notificaciones').textContent).toContain('Armazón clásico');
    expect(fixture.nativeElement.querySelector('.panel-notificaciones').textContent).toContain('Quedan 3; mínimo 3.');
    expect(fixture.nativeElement.querySelector('.boton-eliminar-notificacion')).toBeNull();

    await vi.advanceTimersByTimeAsync(15_000);
    http.expectOne('http://localhost:8080/api/Productos/stock-bajo').flush([]);
    http.expectOne('http://localhost:8080/api/AvisosReposicion').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.contador-notificaciones')).toBeNull();
    expect(fixture.nativeElement.querySelector('.panel-notificaciones').textContent).toContain('No hay notificaciones');
  });

  it('muestra notificaciones publicadas desde otras funcionalidades', async () => {
    TestBed.inject(NotificacionesAdminService).agregar({
      titulo: 'Nueva reserva',
      mensaje: 'Se recibió una solicitud de reserva.',
      tipo: 'informacion'
    });
    fixture.detectChanges();

    const boton = fixture.nativeElement.querySelector('.boton-notificaciones') as HTMLButtonElement;
    expect(boton.getAttribute('aria-label')).toBe('Notificaciones: 1');
    boton.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.panel-notificaciones').textContent).toContain('Nueva reserva');
    expect(fixture.nativeElement.querySelector('.panel-notificaciones').textContent).toContain('Se recibió una solicitud de reserva.');
  });

  it('muestra las solicitudes abiertas como notificaciones persistentes hasta que se cierran', async () => {
    const http = TestBed.inject(HttpTestingController);
    const solicitudes = [{
      idAviso: 7,
      rutCliente: '12345678-5',
      nombreCliente: 'Cliente E2E',
      correoCliente: 'cliente@example.invalid',
      idProducto: 12,
      nombreProducto: 'Armazón solicitado',
      fecha: '2026-11-23T12:00:00Z',
      estado: 'Abierta',
    }];

    await vi.advanceTimersByTimeAsync(15_000);
    http.expectOne('http://localhost:8080/api/Productos/stock-bajo').flush([]);
    http.expectOne('http://localhost:8080/api/AvisosReposicion').flush(solicitudes);
    await fixture.whenStable();
    fixture.detectChanges();

    const boton = fixture.nativeElement.querySelector('.boton-notificaciones') as HTMLButtonElement;
    boton.click();
    fixture.detectChanges();
    const panel = fixture.nativeElement.querySelector('.panel-notificaciones') as HTMLElement;
    expect(panel.textContent).toContain('Solicitud de reposición: Armazón solicitado');
    expect(panel.textContent).toContain('Cliente E2E solicitó un aviso');
    const solicitud = fixture.nativeElement.querySelector(
      '.panel-notificaciones [data-tipo="advertencia"]'
    ) as HTMLElement;
    expect(solicitud).not.toBeNull();
    expect(solicitud.classList.contains('notificacion-sistema')).toBe(true);
    expect(fixture.nativeElement.querySelector('.boton-eliminar-notificacion')).toBeNull();

    await vi.advanceTimersByTimeAsync(15_000);
    http.expectOne('http://localhost:8080/api/Productos/stock-bajo').flush([]);
    http.expectOne('http://localhost:8080/api/AvisosReposicion').flush([
      { ...solicitudes[0], estado: 'Cerrada' },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.contador-notificaciones')).toBeNull();
    expect(panel.textContent).toContain('No hay notificaciones');
  });

  it('reanuda la detección después de un error al consultar el inventario', async () => {
    const http = TestBed.inject(HttpTestingController);
    await vi.advanceTimersByTimeAsync(15_000);
    http.expectOne('http://localhost:8080/api/Productos/stock-bajo').flush({}, { status: 500, statusText: 'Error' });
    http.expectOne('http://localhost:8080/api/AvisosReposicion').flush([]);
    await fixture.whenStable();

    await vi.advanceTimersByTimeAsync(15_000);
    http.expectOne('http://localhost:8080/api/Productos/stock-bajo').flush([]);
    http.expectOne('http://localhost:8080/api/AvisosReposicion').flush([]);
    await fixture.whenStable();

    (fixture.nativeElement.querySelector('.boton-notificaciones') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('.panel-notificaciones').textContent).toContain('No hay notificaciones');
  });
  it('muestra Trabajadores y Cerrar sesión en el desplegable del administrador', () => {
    fixture.detectChanges();
    const menu = fixture.nativeElement.querySelector('.menu-cuenta') as HTMLDetailsElement;
    expect(menu.querySelector('summary')?.textContent).toContain('Administrador');
    (menu.querySelector('summary') as HTMLElement).click();
    expect(menu.open).toBe(true);
    expect(menu.querySelector('a')?.getAttribute('href')).toBe('/admin/panel/trabajadores');
    expect(menu.querySelector('button')?.textContent).toContain('Cerrar sesión');
  });
  it('muestra el nombre del trabajador y oculta la gestión de trabajadores', async () => {
    TestBed.inject(AutenticacionAdminService).administrador.set({ idAdministrador: 0, idTrabajador: 1, nombre: 'Equipo de ventas', tipoUsuario: 'Trabajador', permisos: ['ventas'] });
    fixture.detectChanges();
    const menu = fixture.nativeElement.querySelector('.menu-cuenta') as HTMLDetailsElement;
    expect(menu.querySelector('summary')?.textContent).toContain('Equipo de ventas');
    expect(menu.querySelector('a[href="/admin/panel/trabajadores"]')).toBeNull();
    expect(menu.querySelector('a[href="/admin/panel/cambiar-contrasena"]')).toBeTruthy();
    expect(menu.querySelector('button')?.textContent).toContain('Cerrar sesión');
    expect(fixture.nativeElement.querySelector('a[href="/admin/panel/clientes"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/admin/panel/ventas"]')).toBeTruthy();
    await vi.advanceTimersByTimeAsync(15_000);
    const http = TestBed.inject(HttpTestingController);
    http.expectNone('http://localhost:8080/api/Productos/stock-bajo');
    http.expectNone('http://localhost:8080/api/AvisosReposicion');
  });

});
