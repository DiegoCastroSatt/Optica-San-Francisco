-- Migración para bases existentes: token de idempotencia para pedidos de cotización.
-- Las bases nuevas ya obtienen esta columna desde database/schema.sql.

USE optica_db;

-- Agregar token_cotizacion con restricción UNIQUE a pedidos si no existe
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
