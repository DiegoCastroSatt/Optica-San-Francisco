-- Migración para bases existentes: token de idempotencia para pedidos de cotización.
-- Las bases nuevas ya obtienen esta columna desde database/schema.sql.

USE optica_db;

-- 1. Limpieza de columnas/restricciones previas si existieran
SET @fk_existe = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS 
    WHERE TABLE_SCHEMA = DATABASE() 
      AND TABLE_NAME = 'pedidos' 
      AND CONSTRAINT_NAME = 'fk_pedidos_cotizacion'
);

SET @sql_drop_fk = IF(@fk_existe > 0, 'ALTER TABLE pedidos DROP FOREIGN KEY fk_pedidos_cotizacion;', 'SELECT 1;');
PREPARE stmt_fk FROM @sql_drop_fk;
EXECUTE stmt_fk;
DEALLOCATE PREPARE stmt_fk;

SET @uq_existe = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.STATISTICS 
    WHERE TABLE_SCHEMA = DATABASE() 
      AND TABLE_NAME = 'pedidos' 
      AND INDEX_NAME = 'uq_pedidos_cotizacion'
);

SET @sql_drop_uq = IF(@uq_existe > 0, 'ALTER TABLE pedidos DROP INDEX uq_pedidos_cotizacion;', 'SELECT 1;');
PREPARE stmt_uq FROM @sql_drop_uq;
EXECUTE stmt_uq;
DEALLOCATE PREPARE stmt_uq;

SET @col_id_cot_existe = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_SCHEMA = DATABASE() 
      AND TABLE_NAME = 'pedidos' 
      AND COLUMN_NAME = 'id_cotizacion'
);

SET @sql_drop_col = IF(@col_id_cot_existe > 0, 'ALTER TABLE pedidos DROP COLUMN id_cotizacion;', 'SELECT 1;');
PREPARE stmt_col FROM @sql_drop_col;
EXECUTE stmt_col;
DEALLOCATE PREPARE stmt_col;

DROP TABLE IF EXISTS cotizaciones;

-- 2. Agregar token_cotizacion con restricción UNIQUE a pedidos si no existe
SET @col_token_existe = (
    SELECT COUNT(*) 
    FROM INFORMATION_SCHEMA.COLUMNS 
    WHERE TABLE_SCHEMA = DATABASE() 
      AND TABLE_NAME = 'pedidos' 
      AND COLUMN_NAME = 'token_cotizacion'
);

SET @sql_add_token = IF(
    @col_token_existe = 0,
    'ALTER TABLE pedidos ADD COLUMN token_cotizacion VARCHAR(36) NULL, ADD CONSTRAINT uq_pedidos_token_cotizacion UNIQUE (token_cotizacion);',
    'SELECT "Columna token_cotizacion ya existe en pedidos";'
);

PREPARE stmt_token FROM @sql_add_token;
EXECUTE stmt_token;
DEALLOCATE PREPARE stmt_token;
