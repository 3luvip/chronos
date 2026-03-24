use game_sever

CREATE TABLE IF NOT EXISTS account (
    -- Khoá chính tự tăng
    id              INT          NOT NULL AUTO_INCREMENT,
 
    -- Thông tin đăng nhập
    username        VARCHAR(32)  NOT NULL,
    -- Lưu PHC string của argon2id, ví dụ:
    -- $argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>
    -- Độ dài tối đa 255 ký tự là đủ cho tất cả phiên bản argon2.
    password_hash   VARCHAR(255) NOT NULL,
 
    -- Phân quyền và trạng thái
    is_admin        TINYINT(1)   NOT NULL DEFAULT 0
                        COMMENT '1 = admin, 0 = người chơi thường',
    active          TINYINT(1)   NOT NULL DEFAULT 1
                        COMMENT '1 = tài khoản hoạt động, 0 = đã vô hiệu hoá',
    ban             TINYINT(1)   NOT NULL DEFAULT 0
                        COMMENT '1 = đã bị khoá, 0 = bình thường',
 
    -- Server mà tài khoản được phân về (server_login trong game)
    server_login    INT          NOT NULL DEFAULT 1,
 
    -- Tài sản trong game
    gold       INT          NOT NULL DEFAULT 0
                        COMMENT 'Số thỏi vàng',
    vnd             INT          NOT NULL DEFAULT 0
                        COMMENT 'Số VND (tiền nạp quy đổi)',
    total_recharge         INT          NOT NULL DEFAULT 0
                        COMMENT 'Tổng số tiền đã nạp (dùng để tính rank)',
 
    -- Phần thưởng / item đặc biệt (JSON string hoặc CSV item id)
    reward          TEXT         DEFAULT NULL
                        COMMENT 'Danh sách phần thưởng chờ nhận',
 
    -- Thời gian hoạt động (NULL nếu chưa từng login/logout)
    last_time_login  DATETIME    DEFAULT NULL,
    last_time_logout DATETIME    DEFAULT NULL,
 
    -- Audit timestamps
    created_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at      DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP
                        ON UPDATE CURRENT_TIMESTAMP,
 
    PRIMARY KEY (id),
 
    -- username phải duy nhất — ngăn đăng ký trùng tên
    UNIQUE KEY uq_username (username)
)
ENGINE = InnoDB
DEFAULT CHARSET = utf8mb4
COLLATE = utf8mb4_unicode_ci
COMMENT = 'Tài khoản người chơi';

-- Index cho query server sync (lọc theo server_login)
CREATE INDEX idx_account_server ON account (server_login);
 
-- Index hỗ trợ tìm tài khoản bị ban hoặc inactive (admin tools)
CREATE INDEX idx_account_status  ON account (active, ban);


-- 4. SEED DATA — tài khoản test (CHỈ dùng trong môi trường dev)
-- -----------------------------------------------------------------------------
-- Password hash dưới đây tương ứng với password "password123"
-- được hash bằng argon2id với params: m=19456, t=2, p=1.
--
-- KHÔNG dùng seed data này trong production.
-- Xoá hoặc comment toàn bộ phần này trước khi deploy.
 
INSERT INTO account (
    username, password_hash,
    is_admin, active, ban,
    server_login,
    gold, vnd, total_recharge,
    reward,
    last_time_login, last_time_logout
) VALUES
-- Tài khoản admin test
(
    'admin',
    '$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHRzb21lc2FsdA$RoB4lCBEYHNXpAE2SVLo0DqJQUNBdZ5bMiCqBlw8Dxo',
    1, 1, 0,
    1,
    9999, 9999, 9999,
    NULL,
    NULL, NULL
),
-- Tài khoản người chơi test
(
    'player1',
    '$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHRzb21lc2FsdA$RoB4lCBEYHNXpAE2SVLo0DqJQUNBdZ5bMiCqBlw8Dxo',
    0, 1, 0,
    1,
    100, 0, 0,
    NULL,
    NULL, NULL
),
-- Tài khoản bị ban để test
(
    'banned_user',
    '$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHRzb21lc2FsdA$RoB4lCBEYHNXpAE2SVLo0DqJQUNBdZ5bMiCqBlw8Dxo',
    0, 1, 1,
    1,
    0, 0, 0,
    NULL,
    NULL, NULL
),
-- Tài khoản trên server 2 để test server_login check
(
    'sv2_player',
    '$argon2id$v=19$m=19456,t=2,p=1$c29tZXNhbHRzb21lc2FsdA$RoB4lCBEYHNXpAE2SVLo0DqJQUNBdZ5bMiCqBlw8Dxo',
    0, 1, 0,
    2,
    50, 0, 0,
    NULL,
    NULL, NULL
);
 

