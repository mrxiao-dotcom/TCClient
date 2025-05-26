-- 检查 daily_ranking 表中的数据

-- 1. 检查表是否存在
SELECT 
    TABLE_NAME,
    TABLE_ROWS,
    CREATE_TIME,
    UPDATE_TIME
FROM information_schema.tables 
WHERE table_schema = DATABASE() 
AND table_name = 'daily_ranking';

-- 2. 查看表结构
DESCRIBE daily_ranking;

-- 3. 查看所有数据
SELECT 
    id,
    date,
    SUBSTRING(top_gainers, 1, 100) as top_gainers_preview,
    SUBSTRING(top_losers, 1, 100) as top_losers_preview,
    created_at,
    updated_at
FROM daily_ranking 
ORDER BY date DESC;

-- 4. 检查最近30天的数据
SELECT 
    date,
    LENGTH(top_gainers) as gainers_length,
    LENGTH(top_losers) as losers_length
FROM daily_ranking 
WHERE date >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
ORDER BY date DESC;

-- 5. 检查今天的数据
SELECT 
    id,
    date,
    top_gainers,
    top_losers
FROM daily_ranking 
WHERE date = CURDATE(); 