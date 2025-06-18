# 数据库设计文档

## 1. 数据库配置

系统使用多数据库架构，包含以下数据库：

### 1.1 主数据库
- 主机：45.153.131.217
- 端口：3306
- 数据库：localdb
- 用途：核心业务数据

### 1.2 数据库1
- 主机：45.153.131.230
- 端口：3306
- 数据库：localdb
- 用途：扩展业务数据

### 1.3 数据库2
- 主机：185.242.235.85
- 端口：3306
- 数据库：autotrader
- 用途：交易数据

### 1.4 数据库3
- 主机：45.153.131.217
- 端口：3306
- 数据库：localdb
- 用途：备份数据

## 2. 表结构

### 2.1 产品模块 (Product)

#### 2.1.1 products
基础产品信息表
```sql
CREATE TABLE `gate_price_30m` (
	`id` INT(10) NOT NULL AUTO_INCREMENT,
	`product_code` VARCHAR(255) NOT NULL COLLATE 'gb2312_chinese_ci',
	`close` DECIMAL(30,15) NULL DEFAULT NULL,
	`high` DECIMAL(30,15) NULL DEFAULT NULL,
	`low` DECIMAL(30,15) NULL DEFAULT NULL,
	`open` DECIMAL(30,15) NULL DEFAULT NULL,
	`price_time` DATETIME NOT NULL,
	`frame` VARCHAR(255) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
	PRIMARY KEY (`id`) USING BTREE,
	UNIQUE INDEX `uk_product_time` (`product_code`, `price_time`) USING BTREE,
	INDEX `idx_price_time` (`price_time`) USING BTREE,
	INDEX `idx_product_time` (`product_code`, `price_time`) USING BTREE
)
```

#### 2.1.2 stratage_latest_gate
策略状态表
```sql
CREATE TABLE `stratage_latest_gate` (
	`stratage_id` INT(10) NOT NULL,
	`product_code` VARCHAR(255) NOT NULL COLLATE 'gb2312_chinese_ci',
	`stratage_time` DATETIME NOT NULL,
	`num` INT(10) NULL DEFAULT NULL,
	`stg` INT(10) NULL DEFAULT NULL,
	`close` DECIMAL(30,15) NULL DEFAULT NULL,
	`rate` FLOAT NULL DEFAULT NULL,
	`winner` FLOAT NULL DEFAULT NULL,
	`top` FLOAT NULL DEFAULT NULL,
	`mid` FLOAT NULL DEFAULT NULL,
	`bot` FLOAT NULL DEFAULT NULL,
	`total_profit` DECIMAL(20,4) NULL DEFAULT '10000.0000' COMMENT '累计盈利',
	PRIMARY KEY (`product_code`) USING BTREE
)
COLLATE='gb2312_chinese_ci'
ENGINE=InnoDB
;

```

#### 2.1.3 price_range_20d
价格区间表
```sql
CREATE TABLE `price_range_20d` (
	`id` BIGINT(19) NOT NULL AUTO_INCREMENT,
	`symbol` VARCHAR(50) NOT NULL COLLATE 'gb2312_chinese_ci',
	`high_price_20d` DECIMAL(20,8) NULL DEFAULT NULL,
	`low_price_20d` DECIMAL(20,8) NULL DEFAULT NULL,
	`last_price` DECIMAL(20,8) NULL DEFAULT NULL,
	`amplitude` DECIMAL(10,4) NULL DEFAULT NULL,
	`position_ratio` DECIMAL(10,4) NULL DEFAULT NULL,
	`update_date` DATE NOT NULL,
	`update_time` DATETIME NULL DEFAULT 'CURRENT_TIMESTAMP' ON UPDATE CURRENT_TIMESTAMP,
	`volume_24h` DECIMAL(20,8) NULL DEFAULT '0.00000000' COMMENT '24小时成交量',
	`open_price` DECIMAL(20,8) NOT NULL DEFAULT '0.00000000' COMMENT '今日开盘价',
	`daily_change` DECIMAL(10,4) NOT NULL DEFAULT '0.0000' COMMENT '当日涨幅（%）',
	PRIMARY KEY (`id`) USING BTREE,
	UNIQUE INDEX `uk_symbol_date` (`symbol`, `update_date`) USING BTREE,
	UNIQUE INDEX `uk_symbol` (`symbol`) USING BTREE,
	INDEX `idx_symbol` (`symbol`) USING BTREE
)
COLLATE='gb2312_chinese_ci'
ENGINE=InnoDB
AUTO_INCREMENT=30937
;

```

#### 2.1.4 daily_market_stats
市场每日统计表
```sql
CREATE TABLE `daily_market_stats` (
    `id` INT(10) NOT NULL AUTO_INCREMENT,
    `stat_date` DATE NOT NULL COMMENT '统计日期',
    `up_count` INT(10) NOT NULL DEFAULT 0 COMMENT '上涨品种数',
    `down_count` INT(10) NOT NULL DEFAULT 0 COMMENT '下跌品种数',
    `strong_count` INT(10) NOT NULL DEFAULT 0 COMMENT '强势品种数',
    `weak_count` INT(10) NOT NULL DEFAULT 0 COMMENT '弱势品种数',
    `update_time` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_stat_date` (`stat_date`),
    INDEX `idx_update_time` (`update_time`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='市场每日统计表';
```

### 2.2 系统配置模块 (System)

#### 2.2.1 acct_info
账户信息表
```sql
CREATE TABLE `acct_info` (
	`acct_id` INT(10) NOT NULL AUTO_INCREMENT,
	`acct_name` VARCHAR(255) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
	`acct_date` DATETIME NULL DEFAULT NULL,
	`memo` VARCHAR(255) NULL DEFAULT NULL COMMENT '显示名字' COLLATE 'gb2312_chinese_ci',
	`apikey` VARCHAR(255) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
	`secretkey` VARCHAR(255) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
	`apipass` VARCHAR(255) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
	`state` INT(10) NULL DEFAULT NULL,
	`status` INT(10) NULL DEFAULT NULL,
	`email` VARCHAR(255) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
	`group_id` INT(10) NULL DEFAULT NULL,
	`sendflag` INT(10) NULL DEFAULT NULL,
	PRIMARY KEY (`acct_id`) USING BTREE
)
COLLATE='gb2312_chinese_ci'
ENGINE=InnoDB
AUTO_INCREMENT=10001
;
```

#### 2.2.2 product_groups
策略品种组配置表
```sql
CREATE TABLE `product_groups` (
	`group_id` INT(10) NOT NULL AUTO_INCREMENT,
	`group_name` VARCHAR(50) NOT NULL COLLATE 'gb2312_chinese_ci',
	`symbols` TEXT NOT NULL COLLATE 'gb2312_chinese_ci',
	`description` VARCHAR(200) NULL DEFAULT NULL COLLATE 'gb2312_chinese_ci',
	`created_at` TIMESTAMP NULL DEFAULT NULL,
	`status` TINYINT(3) NULL DEFAULT '1',
	PRIMARY KEY (`group_id`) USING BTREE
)
COLLATE='gb2312_chinese_ci'
ENGINE=InnoDB
AUTO_INCREMENT=5
;

```

#### 2.2.3 account_product_groups
账户策略配置表
```sql
CREATE TABLE `account_product_groups` (
	`id` INT(10) NOT NULL AUTO_INCREMENT,
	`acct_id` INT(10) NOT NULL COMMENT '账户ID',
	`group_id` INT(10) NOT NULL COMMENT '产品组ID',
	`strategy_id` INT(10) NOT NULL COMMENT '策略ID',
	`leverage` DECIMAL(10,2) NULL DEFAULT '1.00' COMMENT '杠杆率',
	`total_value` DECIMAL(20,4) NULL DEFAULT '10000.0000' COMMENT '账户总市值',
	`status` TINYINT(3) NULL DEFAULT '1' COMMENT '状态: 1-启用 0-禁用',
	`created_at` TIMESTAMP NULL DEFAULT NULL,
	`updated_at` TIMESTAMP NULL DEFAULT NULL ON UPDATE CURRENT_TIMESTAMP,
	PRIMARY KEY (`id`) USING BTREE,
	INDEX `group_id` (`group_id`) USING BTREE,
	INDEX `idx_acct_strategy` (`acct_id`, `strategy_id`) USING BTREE,
	INDEX `idx_status` (`status`) USING BTREE,
	CONSTRAINT `account_product_groups_ibfk_1` FOREIGN KEY (`group_id`) REFERENCES `product_groups` (`group_id`) ON UPDATE RESTRICT ON DELETE RESTRICT
)
COMMENT='账户产品组配置表'
COLLATE='utf8mb4_0900_ai_ci'
ENGINE=InnoDB
AUTO_INCREMENT=6
;

```

#### 2.2.4 account_balance
账户实时数据表
```sql
CREATE TABLE `account_balance` (
    `id` INT(10) NOT NULL AUTO_INCREMENT,
    `acct_id` INT(10) NOT NULL COMMENT '关联acct_info的账户ID',
    `equity` DECIMAL(20,4) NOT NULL COMMENT '账户权益',
    `available_balance` DECIMAL(20,4) NOT NULL COMMENT '可用余额',
    `position_value` DECIMAL(20,4) NOT NULL COMMENT '持仓市值',
    `unrealized_pnl` DECIMAL(20,4) NOT NULL COMMENT '未实现盈亏',
    `long_positions_qty` INT(10) NOT NULL DEFAULT 0 COMMENT '多仓合约数量',
    `short_positions_qty` INT(10) NOT NULL DEFAULT 0 COMMENT '空仓合约数量',
    `update_time` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uk_acct_id` (`acct_id`),
    INDEX `idx_update_time` (`update_time`),
    CONSTRAINT `fk_account_balance_acct_info` FOREIGN KEY (`acct_id`) REFERENCES `acct_info` (`acct_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='账户实时数据表';
```

#### 2.2.5 account_positions
账户持仓明细表
```sql
CREATE TABLE `account_positions` (
    `id` INT(10) NOT NULL AUTO_INCREMENT,
    `acct_id` INT(10) NOT NULL COMMENT '关联acct_info的账户ID',
    `symbol` VARCHAR(50) NOT NULL COMMENT '交易对',
    `position_amt` DECIMAL(20,8) NOT NULL COMMENT '持仓数量',
    `entry_price` DECIMAL(20,4) NOT NULL COMMENT '开仓均价',
    `mark_price` DECIMAL(20,4) NOT NULL COMMENT '标记价格',
    `unrealized_profit` DECIMAL(20,4) NOT NULL COMMENT '未实现盈亏',
    `leverage` DECIMAL(10,2) NOT NULL COMMENT '杠杆倍数',
    `position_side` VARCHAR(10) NOT NULL COMMENT '持仓方向:LONG/SHORT',
    `update_time` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    INDEX `idx_acct_symbol` (`acct_id`, `symbol`),
    INDEX `idx_update_time` (`update_time`),
    CONSTRAINT `fk_account_positions_acct_info` FOREIGN KEY (`acct_id`) REFERENCES `acct_info` (`acct_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='账户持仓明细表';
```

## 3. 索引设计

### 3.1 stratage_latest_gate
```sql
CREATE INDEX idx_symbol ON stratage_latest_gate(symbol);
CREATE INDEX idx_product_code ON stratage_latest_gate(product_code);
CREATE INDEX idx_stg ON stratage_latest_gate(stg);
```

### 3.2 price_range_20d
```sql
CREATE INDEX idx_product_code ON price_range_20d(product_code);
CREATE INDEX idx_volume ON price_range_20d(volume_24h);
```

### 3.3 account_product_groups
```sql
CREATE INDEX idx_acct_group ON account_product_groups(acct_id, group_id);
CREATE INDEX idx_strategy ON account_product_groups(strategy_id);
```
### 3.4 contract_comparison
CREATE TABLE `contract_comparison` (
	`id` BIGINT(19) NOT NULL AUTO_INCREMENT,
	`symbol` VARCHAR(20) NOT NULL COLLATE 'gb2312_chinese_ci',
	`binance_listed` TINYINT(1) NULL DEFAULT '0',
	`gate_listed` TINYINT(1) NULL DEFAULT '0',
	`status` ENUM('active','delisted') NULL DEFAULT 'active' COLLATE 'gb2312_chinese_ci',
	`contract_size` FLOAT NULL DEFAULT NULL,
	PRIMARY KEY (`id`) USING BTREE,
	UNIQUE INDEX `unique_symbol` (`symbol`) USING BTREE
)
COLLATE='gb2312_chinese_ci'
ENGINE=InnoDB
AUTO_INCREMENT=565
;

## 3.5 daily_market_stats
CREATE TABLE `daily_market_stats` (
	`id` BIGINT(19) NOT NULL AUTO_INCREMENT,
	`stat_date` DATE NOT NULL,
	`total_volume` DECIMAL(30,8) NULL DEFAULT NULL COMMENT '总成交额（亿）',
	`gainers_count` INT(10) NOT NULL,
	`losers_count` INT(10) NOT NULL,
	`strong_symbols_count` INT(10) NOT NULL,
	`weak_symbols_count` INT(10) NOT NULL,
	`total_symbols` INT(10) NOT NULL,
	`created_at` DATETIME NULL DEFAULT 'CURRENT_TIMESTAMP',
	PRIMARY KEY (`id`) USING BTREE,
	UNIQUE INDEX `uniq_stat_date` (`stat_date`) USING BTREE
)
COLLATE='gb2312_chinese_ci'
ENGINE=InnoDB
AUTO_INCREMENT=53
;

## 4. 注意事项

1. 所有表都应包含 created_at 字段，记录创建时间
2. 需要跟踪修改的表添加 updated_at 字段
3. 重要的外键关系都需要建立约束
4. 考虑添加适当的索引提高查询性能
5. 金额相关字段使用 DECIMAL 类型确保精度
6. 状态字段使用 TINYINT，并设置默认值

## 5. 数据库维护

1. 定期备份数据
2. 监控索引使用情况
3. 优化慢查询
4. 定期清理历史数据
5. 维护表统计信息 