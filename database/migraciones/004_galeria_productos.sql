-- Aplicar una vez a bases existentes. Las imágenes anteriores siguen en ruta_imagen.
USE optica_db;
ALTER TABLE productos ADD COLUMN imagenes_json JSON NULL, ALGORITHM=INSTANT;
