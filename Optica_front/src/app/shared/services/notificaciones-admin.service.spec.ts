import { TestBed } from '@angular/core/testing';
import { NotificacionesAdminService } from './notificaciones-admin.service';

describe('NotificacionesAdminService', () => {
  let service: NotificacionesAdminService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [NotificacionesAdminService] });
    service = TestBed.inject(NotificacionesAdminService);
  });

  it('mantiene una notificación de reposición persistente e idempotente entre actualizaciones', () => {
    const aviso = {
      idAviso: 7,
      nombreCliente: 'Cliente de prueba',
      nombreProducto: 'Armazón agotado',
      estado: 'Abierta' as const
    };

    service.actualizarSolicitudesReposicion([aviso]);
    const primeraNotificacion = service.notificaciones().find(item => item.idAvisoReposicion === aviso.idAviso);

    expect(primeraNotificacion).toMatchObject({
      titulo: 'Solicitud de reposición: Armazón agotado',
      mensaje: 'Cliente de prueba solicitó un aviso cuando el producto vuelva a estar disponible.',
      tipo: 'advertencia',
      persistente: true
    });
    expect(primeraNotificacion).toBeDefined();

    service.actualizarSolicitudesReposicion([aviso]);
    expect(service.notificaciones()).toHaveLength(1);
    expect(service.notificaciones()[0]?.id).toBe(primeraNotificacion?.id);

    if (primeraNotificacion) service.eliminar(primeraNotificacion.id);
    expect(service.notificaciones()).toHaveLength(1);
  });

  it('usa el formato de advertencia tanto para stock bajo como para solicitudes de reposición', () => {
    service.actualizarAlertasStockBajo([{
      idProducto: 3,
      nombre: 'Armazón bajo',
      stock: 0,
      stockMinimo: 2
    }]);
    service.actualizarSolicitudesReposicion([{
      idAviso: 7,
      nombreCliente: 'Cliente de prueba',
      nombreProducto: 'Armazón agotado',
      estado: 'Abierta'
    }]);

    expect(service.notificaciones().map(item => item.tipo)).toEqual([
      'advertencia',
      'advertencia'
    ]);
  });

  it('retira la notificación cuando la solicitud se cierra sin perder las demás', () => {
    service.agregar({ titulo: 'Otra notificación', mensaje: 'Se conserva.' });
    service.actualizarSolicitudesReposicion([{
      idAviso: 7,
      nombreCliente: 'Cliente de prueba',
      nombreProducto: 'Armazón agotado',
      estado: 'Abierta'
    }]);

    service.actualizarSolicitudesReposicion([{
      idAviso: 7,
      nombreCliente: 'Cliente de prueba',
      nombreProducto: 'Armazón agotado',
      estado: 'Cerrada'
    }]);

    expect(service.notificaciones()).toHaveLength(1);
    expect(service.notificaciones()[0]?.titulo).toBe('Otra notificación');
  });
});
