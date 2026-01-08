/* =============================================================
   OnlineContract – SEED (base data)
   Inserts/merges baseline data (idempotent where applicable).
   ============================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

USE [OnlineContract];
GO

BEGIN TRAN;

/* lookup_set seed (explicit IDs) */

DECLARE @is_identity_lookup_set BIT =
    CASE WHEN COLUMNPROPERTY(OBJECT_ID('dbo.lookup_set'), 'lookup_set_id', 'IsIdentity') = 1 THEN 1 ELSE 0 END;

IF @is_identity_lookup_set = 1 SET IDENTITY_INSERT dbo.lookup_set ON;

MERGE dbo.lookup_set AS tgt
USING (
    VALUES
    ( 1 , N'None',          N'None'),
    ( 2 , N'EventType',     N'Information'),
    ( 3 , N'EventType',     N'Warning'),
    ( 4 , N'EventType',     N'Error'),
    ( 5 , N'UserRole',      N'Customer'),
    ( 6 , N'UserRole',      N'Worker'),
    ( 7 , N'UserRole',      N'Manager'),
    ( 8 , N'UserRole',      N'Administrator'),
    ( 9 , N'ContractState', N'Draft'),
    (10 , N'ContractState', N'Submitted'),
    (11 , N'ContractState', N'Accepted'),
    (12 , N'ContractState', N'Partially Accepted'),
    (13 , N'ContractState', N'Rejected'),
    (14 , N'ContractState', N'In Progress'),
    (15 , N'ContractState', N'Completed'),
    (16 , N'ContractState', N'Dispatched'),
    (17 , N'ContractState', N'Delivered'),
    (18 , N'ContractState', N'Returned'),
    (19 , N'ContractState', N'Cancelled'),
    (20 , N'ContractState', N'Written Off'),
    (21 , N'ProductStateInOrder', N'Draft'),
    (22 , N'ProductStateInOrder', N'Submitted'),
    (23 , N'ProductStateInOrder', N'Accepted'),
    (24 , N'ProductStateInOrder', N'Rejected')
) AS src(lookup_set_id, set_name, value)
ON (tgt.lookup_set_id = src.lookup_set_id)

WHEN NOT MATCHED THEN
    INSERT (lookup_set_id, set_name, value)
    VALUES (src.lookup_set_id, src.set_name, src.value)

WHEN MATCHED AND (tgt.set_name <> src.set_name OR tgt.value <> src.value)
THEN
    UPDATE SET set_name = src.set_name, value = src.value;

IF @is_identity_lookup_set = 1 SET IDENTITY_INSERT dbo.lookup_set OFF;

/* contract_state seed */
DECLARE @is_identity_contract_state BIT =
    CASE WHEN COLUMNPROPERTY(OBJECT_ID('dbo.contract_state'), 'contract_state_id', 'IsIdentity') = 1 THEN 1 ELSE 0 END;
IF @is_identity_contract_state = 1 SET IDENTITY_INSERT dbo.contract_state ON;

MERGE dbo.contract_state AS tgt
USING (
    VALUES
    (1 , 9 , 0, 1),
    (2 , 10, 1, 0),
    (3 , 11, 0, 0),
    (4 , 12, 0, 0),
    (5 , 13, 0, 0),
    (6 , 14, 0, 0),
    (7 , 15, 0, 0),
    (8 , 16, 0, 0),
    (9 , 17, 0, 1),
    (10, 18, 0, 1),
    (11, 19, 0, 1),
    (12, 20, 0, 1)
) AS src(contract_state_id, lookup_set_id, is_start_state, is_end_state)
ON (tgt.contract_state_id = src.contract_state_id)
WHEN NOT MATCHED THEN
    INSERT (contract_state_id, lookup_set_id, is_start_state, is_end_state)
    VALUES (src.contract_state_id, src.lookup_set_id, src.is_start_state, src.is_end_state)
WHEN MATCHED AND (tgt.lookup_set_id <> src.lookup_set_id OR
                  tgt.is_start_state <> src.is_start_state OR
                  tgt.is_end_state   <> src.is_end_state)
THEN UPDATE SET lookup_set_id = src.lookup_set_id,
                is_start_state = src.is_start_state,
                is_end_state   = src.is_end_state;

IF @is_identity_contract_state = 1 SET IDENTITY_INSERT dbo.contract_state OFF;

/* contract_state_transition seed */
DECLARE @is_identity_cst BIT =
    CASE WHEN COLUMNPROPERTY(OBJECT_ID('dbo.contract_state_transition'), 'contract_state_transition_id', 'IsIdentity') = 1 THEN 1 ELSE 0 END;
IF @is_identity_cst = 1 SET IDENTITY_INSERT dbo.contract_state_transition ON;

MERGE dbo.contract_state_transition AS tgt
USING (
    VALUES
    (1 , 10, 11),
    (2 , 10, 12),
    (3 , 10, 13),
    (4 , 11, 14),
    (5 , 12, 14),
    (6 , 12, 19),
    (7 , 14, 15),
    (8 , 14, 19),
    (9 , 15, 16),
    (10, 15, 19),
    (11, 16, 17),
    (12, 16, 18),
    (13, 18, 16),
    (14, 18, 20)
) AS src(contract_state_transition_id, from_state_id, to_state_id)
ON (tgt.contract_state_transition_id = src.contract_state_transition_id)
WHEN NOT MATCHED THEN
    INSERT (contract_state_transition_id, from_state_id, to_state_id)
    VALUES (src.contract_state_transition_id, src.from_state_id, src.to_state_id)
WHEN MATCHED AND (tgt.from_state_id <> src.from_state_id OR tgt.to_state_id <> src.to_state_id)
THEN UPDATE SET from_state_id = src.from_state_id, to_state_id = src.to_state_id;

IF @is_identity_cst = 1 SET IDENTITY_INSERT dbo.contract_state_transition OFF;

/* ax_user seed: id=0 default, id=2 System, id=1 Administrator */
DECLARE @is_identity_ax_user BIT =
    CASE WHEN COLUMNPROPERTY(OBJECT_ID('dbo.ax_user'), 'ax_user_id', 'IsIdentity') = 1 THEN 1 ELSE 0 END;
IF @is_identity_ax_user = 1 SET IDENTITY_INSERT dbo.ax_user ON;

-- 0: None
IF NOT EXISTS (SELECT 1 FROM dbo.ax_user WHERE ax_user_id = 0)
BEGIN
    INSERT dbo.ax_user (
        ax_user_id, first_name, last_name, code, password, is_active, is_deleted,
        email, last_login_dt, stamp, phone_number, is_group, owner_id, created_dt,
        password_dt, city, street_address, postal_code, role_id, input_user_id,
        is_temp_password)
    VALUES (
        0, N'None', N'None', N'None', N'6848B9F3B0149DED451EEE25EADA1AAED02351F7',
        0, 0, N'', CAST('1900-01-01T00:00:00' AS DATETIME), 0, N'+381000000000', 0,
        0, CAST('1900-01-01T00:00:00' AS DATETIME), CAST('1900-01-01T00:00:00' AS DATETIME),
        NULL, NULL, NULL,
        NULL, 0, 0);
END

-- 2: System
IF NOT EXISTS (SELECT 1 FROM dbo.ax_user WHERE ax_user_id = 2)
BEGIN
    INSERT dbo.ax_user (
        ax_user_id, first_name, last_name, code, password, is_active, is_deleted,
        email, last_login_dt, stamp, phone_number, is_group, owner_id, created_dt,
        password_dt, city, street_address, postal_code, role_id, input_user_id,
        is_temp_password)
    VALUES (
        2, N'System', N'System', N'System', N'jw4vduIrQ+KFUYmHfn3B4efZjCJsldskfNHVR5KDNKk=',
        1, 0, NULL, CAST('1900-01-01T00:00:00' AS DATETIME), 0, N'+381000000000', 0,
        0, CAST('2025-11-25T00:00:00' AS DATETIME), CAST('1900-01-01T00:00:00' AS DATETIME),
        NULL, NULL, NULL,
        8, 0, 0);
END

-- 3: Administrators group (input_user_id = 0)
IF NOT EXISTS (SELECT 1 FROM dbo.ax_user WHERE ax_user_id = 3)
BEGIN
    INSERT dbo.ax_user (
        ax_user_id, first_name, last_name, code, password, is_active, is_deleted,
        email, last_login_dt, stamp, phone_number, is_group, owner_id, created_dt,
        password_dt, city, street_address, postal_code, role_id, input_user_id,
        is_temp_password)
    VALUES (
        3, N'System', N'Administrators', N'Admins', N'',
        1, 0, N'all.admins@gmail.com', NULL, 0,
        N'', 1, 0, CAST('2025-11-25T00:00:00' AS DATETIME), CAST('1900-01-01T00:00:00' AS DATETIME),
        NULL, NULL, NULL,
        8, 0, 0);
END

-- 1: Administrator (owner_id set to 3)
IF NOT EXISTS (SELECT 1 FROM dbo.ax_user WHERE ax_user_id = 1)
BEGIN
    INSERT dbo.ax_user (
        ax_user_id, first_name, last_name, code, password, is_active, is_deleted,
        email, last_login_dt, stamp, phone_number, is_group, owner_id, created_dt,
        password_dt, city, street_address, postal_code, role_id, input_user_id,
        is_temp_password)
    VALUES (
        1, N'Administrator', N'', N'Admin', N'jw4vduIrQ+KFUYmHfn3B4efZjCJsldskfNHVR5KDNKk=',
        1, 0, N'zoranmilinkovic95@gmail.com', CAST('1900-01-01T00:00:00' AS DATETIME), 0,
        N'+381643863857', 0, 3, CAST('2025-11-25T00:00:00' AS DATETIME), CAST('2025-11-25T00:00:00' AS DATETIME),
        NULL, NULL, NULL,
        8, 0, 0);
END

IF @is_identity_ax_user = 1 SET IDENTITY_INSERT dbo.ax_user OFF;

/* product default row id = 0 */
DECLARE @is_identity_product BIT =
    CASE WHEN COLUMNPROPERTY(OBJECT_ID('dbo.product'), 'product_id', 'IsIdentity') = 1 THEN 1 ELSE 0 END;
IF @is_identity_product = 1 SET IDENTITY_INSERT dbo.product ON;

IF NOT EXISTS (SELECT 1 FROM dbo.product WHERE product_id = 0)
BEGIN
    INSERT dbo.product (product_id, name, input_dt, input_user_id, last_modified_by_id, last_updated_dt, is_active, is_deleted, stamp)
    VALUES (0, N'None', CAST('1900-01-01T00:00:00' AS DATETIME2(0)), 0, 0, CAST('1900-01-01T00:00:00' AS DATETIME2(0)), 0, 0, 0);
END

IF @is_identity_product = 1 SET IDENTITY_INSERT dbo.product OFF;

/* contract default row id = 0 (state set to Draft = 9) */
DECLARE @is_identity_contract BIT =
    CASE WHEN COLUMNPROPERTY(OBJECT_ID('dbo.contract'), 'contract_id', 'IsIdentity') = 1 THEN 1 ELSE 0 END;
IF @is_identity_contract = 1 SET IDENTITY_INSERT dbo.contract ON;

IF NOT EXISTS (SELECT 1 FROM dbo.contract WHERE contract_id = 0)
BEGIN
    INSERT dbo.contract (
        contract_id, input_dt, input_user_id, contract_state, last_modified_by_id, last_updated_dt,
        stamp, is_active, is_deleted, delivered_dt, written_off_dt, rejected_dt, cancelled_dt, amount, amt_matched)
    VALUES (
        0, CAST('1900-01-01T00:00:00' AS DATETIME2(0)), 0, 9, 0, CAST('1900-01-01T00:00:00' AS DATETIME2(0)),
        0, 0, 0,
        CAST('1900-01-01T00:00:00' AS DATETIME2(0)), CAST('1900-01-01T00:00:00' AS DATETIME2(0)),
        CAST('1900-01-01T00:00:00' AS DATETIME2(0)), CAST('1900-01-01T00:00:00' AS DATETIME2(0)),
        CAST('0.00' AS DECIMAL(18,2)), CAST('0.00' AS DECIMAL(18,2))
    );
END

IF @is_identity_contract = 1 SET IDENTITY_INSERT dbo.contract OFF;

/* store seed (last_modified_user_id = 2 to satisfy FK) */
DECLARE @is_identity_store BIT =
    CASE WHEN COLUMNPROPERTY(OBJECT_ID('dbo.store'), 'store_id', 'IsIdentity') = 1 THEN 1 ELSE 0 END;
IF @is_identity_store = 1 SET IDENTITY_INSERT dbo.store ON;

MERGE dbo.store AS tgt
USING (
    VALUES
    (1, N'Knez Mihajlova', N'Knez Mihailova 12, 11000 Beograd', N'+381 62 123 4567', N'beograd.kidstyle@gmail.com',
        N'Mon-Fri 09:00-19:00; Sat 10:30-15:30; Sun Closed',
        CAST('2025-12-13T18:37:12' AS DATETIME2(0)), CAST('2025-12-28T22:12:27' AS DATETIME2(0)), 0),
    (2, N'Novi Sad Promenada', N'Bulevar Oslobođenja 119, 21000 Novi Sad', N'+381 64 123 4567', N'novisad.kidstyle@gmail.com',
        N'Mon-Sun 10:00-22:00',
        CAST('2025-12-13T18:37:12' AS DATETIME2(0)), CAST('2025-12-28T22:05:12' AS DATETIME2(0)), 0)
) AS src(store_id, name, address, phone_number, email, working_hours, created_dt, updated_dt, last_modified_user_id)
ON (tgt.store_id = src.store_id)
WHEN NOT MATCHED THEN
    INSERT (store_id, name, address, phone_number, email, working_hours, created_dt, updated_dt, last_modified_user_id)
    VALUES (src.store_id, src.name, src.address, src.phone_number, src.email, src.working_hours, src.created_dt, src.updated_dt, src.last_modified_user_id)
WHEN MATCHED AND (
    tgt.name <> src.name OR tgt.address <> src.address OR tgt.phone_number <> src.phone_number OR
    tgt.email <> src.email OR tgt.working_hours <> src.working_hours OR
    tgt.updated_dt <> src.updated_dt OR ISNULL(tgt.last_modified_user_id, -1) <> src.last_modified_user_id
) THEN UPDATE SET
    name = src.name,
    address = src.address,
    phone_number = src.phone_number,
    email = src.email,
    working_hours = src.working_hours,
    updated_dt = src.updated_dt,
    last_modified_user_id = src.last_modified_user_id;

IF @is_identity_store = 1 SET IDENTITY_INSERT dbo.store OFF;

COMMIT TRAN;

PRINT 'Base data seeded (lookup_set, contract_state, transitions, ax_user; product/contract defaults; store).';
GO
