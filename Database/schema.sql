/* =============================================================
   OnlineContract – SCHEMA (DDL + Functions)
   Creates database (if missing), tables, constraints, functions.
   ============================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

/* 0. Create database if missing */
IF DB_ID(N'OnlineContract') IS NULL
BEGIN
    PRINT 'Creating database [OnlineContract]...';
    CREATE DATABASE [OnlineContract];
END
GO

USE [OnlineContract];
GO

/* 1. Functions (must come first) */
CREATE OR ALTER FUNCTION [dbo].[GetLocalTime]()
RETURNS datetime2
AS
BEGIN
    RETURN CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Central European Standard Time' AS datetime2);
END;
GO

/* 2. DDL: create tables in dependency order */

/* 2.1 lookup_set */
IF OBJECT_ID(N'dbo.lookup_set', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON;
    SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[lookup_set](
        [lookup_set_id] [int] IDENTITY(1,1) NOT NULL,
        [set_name] [nvarchar](100) NOT NULL,
        [value] [nvarchar](100) NOT NULL,
        CONSTRAINT [PK_lookup_set] PRIMARY KEY CLUSTERED ([lookup_set_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY];
END
GO

/* 2.2 ax_user */
IF OBJECT_ID(N'dbo.ax_user', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON;
    SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[ax_user](
        [ax_user_id] [int] IDENTITY(1,1) NOT NULL,
        [first_name] [nvarchar](50) NOT NULL,
        [last_name] [nvarchar](50) NOT NULL,
        [code] [nvarchar](50) NOT NULL,
        [password] [nvarchar](255) NOT NULL,
        [is_active] [bit] NOT NULL,
        [is_deleted] [bit] NOT NULL,
        [email] [nvarchar](100) NULL,
        [last_login_dt] [datetime] NULL,
        [stamp] [int] NOT NULL,
        [phone_number] [nvarchar](20) NOT NULL,
        [is_group] [bit] NOT NULL,
        [owner_id] [int] NOT NULL,
        [created_dt] [datetime] NOT NULL,
        [password_dt] [datetime] NOT NULL,
        [city] [nvarchar](100) NULL,
        [street_address] [nvarchar](200) NULL,
        [postal_code] [nvarchar](20) NULL,
        [role_id] [int] NULL,
        [input_user_id] [int] NULL,
        [is_temp_password] [bit] NOT NULL,
        CONSTRAINT [PK_ax_user] PRIMARY KEY CLUSTERED ([ax_user_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY],
        CONSTRAINT [UQ_ax_user_code] UNIQUE NONCLUSTERED ([code] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY];

    ALTER TABLE [dbo].[ax_user] ADD  DEFAULT ((1)) FOR [is_active];
    ALTER TABLE [dbo].[ax_user] ADD  DEFAULT ((0)) FOR [is_deleted];
    ALTER TABLE [dbo].[ax_user] ADD  DEFAULT ((0)) FOR [stamp];
    ALTER TABLE [dbo].[ax_user] ADD  DEFAULT ('+381000000000') FOR [phone_number];
    ALTER TABLE [dbo].[ax_user] ADD  CONSTRAINT [DF_ax_user_is_group]  DEFAULT ((0)) FOR [is_group];
    ALTER TABLE [dbo].[ax_user] ADD  CONSTRAINT [DF_ax_user_created_dt]  DEFAULT ('1900-01-01') FOR [created_dt];
    ALTER TABLE [dbo].[ax_user] ADD  CONSTRAINT [DF_ax_user_password_dt]  DEFAULT ('1900-01-01') FOR [password_dt];
    ALTER TABLE [dbo].[ax_user] ADD  CONSTRAINT [DF_ax_user_input_user_id]  DEFAULT ((2)) FOR [input_user_id];
    ALTER TABLE [dbo].[ax_user] ADD  CONSTRAINT [DF_ax_user_is_temp_password]  DEFAULT ((0)) FOR [is_temp_password];

    ALTER TABLE [dbo].[ax_user]  WITH CHECK ADD  CONSTRAINT [FK_ax_user_input_user]
        FOREIGN KEY([input_user_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[ax_user] CHECK CONSTRAINT [FK_ax_user_input_user];

    ALTER TABLE [dbo].[ax_user]  WITH CHECK ADD  CONSTRAINT [FK_ax_user_lookup_set]
        FOREIGN KEY([role_id]) REFERENCES [dbo].[lookup_set] ([lookup_set_id]);
    ALTER TABLE [dbo].[ax_user] CHECK CONSTRAINT [FK_ax_user_lookup_set];

    ALTER TABLE [dbo].[ax_user]  WITH CHECK ADD  CONSTRAINT [FK_ax_user_owner]
        FOREIGN KEY([owner_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[ax_user] CHECK CONSTRAINT [FK_ax_user_owner];

    ALTER TABLE [dbo].[ax_user]  WITH NOCHECK ADD  CONSTRAINT [CK_ax_user_role_id]
        CHECK (([role_id] = 8 OR [role_id] = 7 OR [role_id] = 6 OR [role_id] = 5 OR [role_id] = 1));
    ALTER TABLE [dbo].[ax_user] CHECK CONSTRAINT [CK_ax_user_role_id];
END
GO

/* 2.3 contract */
IF OBJECT_ID(N'dbo.contract', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[contract](
        [contract_id] [int] IDENTITY(1,1) NOT NULL,
        [input_dt] [datetime2](0) NOT NULL,
        [input_user_id] [int] NOT NULL,
        [contract_state] [int] NOT NULL,
        [last_modified_by_id] [int] NOT NULL,
        [last_updated_dt] [datetime2](0) NOT NULL,
        [stamp] [int] NOT NULL,
        [is_active] [bit] NOT NULL,
        [is_deleted] [bit] NOT NULL,
        [delivered_dt] [datetime2](0) NOT NULL,
        [written_off_dt] [datetime2](0) NOT NULL,
        [rejected_dt] [datetime2](0) NOT NULL,
        [cancelled_dt] [datetime2](0) NOT NULL,
        [amount] [decimal](18, 2) NOT NULL,
        CONSTRAINT [PK_contract] PRIMARY KEY CLUSTERED ([contract_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY];

    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_input_dt]  DEFAULT (dbo.GetLocalTime()) FOR [input_dt];
    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_input_user]  DEFAULT ((0)) FOR [input_user_id];
    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_last_mod_user]  DEFAULT ((0)) FOR [last_modified_by_id];
    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_last_updated_dt]  DEFAULT (dbo.GetLocalTime()) FOR [last_updated_dt];
    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_stamp]  DEFAULT ((0)) FOR [stamp];
    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_is_active]  DEFAULT ((1)) FOR [is_active];
    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_is_deleted]  DEFAULT ((0)) FOR [is_deleted];
    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_delivered_dt]
        DEFAULT (CONVERT([datetime2](0),'1900-01-01T00:00:00')) FOR [delivered_dt];
    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_written_off_dt]
        DEFAULT (CONVERT([datetime2](0),'1900-01-01T00:00:00')) FOR [written_off_dt];
    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_rejected_dt]
        DEFAULT (CONVERT([datetime2](0),'1900-01-01T00:00:00')) FOR [rejected_dt];
    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_cancelled_dt]
        DEFAULT (CONVERT([datetime2](0),'1900-01-01T00:00:00')) FOR [cancelled_dt];
    ALTER TABLE [dbo].[contract] ADD  CONSTRAINT [DF_contract_amount]  DEFAULT ((0)) FOR [amount];

    ALTER TABLE [dbo].[contract]  WITH CHECK ADD  CONSTRAINT [FK_contract_input_user]
        FOREIGN KEY([input_user_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[contract] CHECK CONSTRAINT [FK_contract_input_user];

    ALTER TABLE [dbo].[contract]  WITH CHECK ADD  CONSTRAINT [FK_contract_last_mod_user]
        FOREIGN KEY([last_modified_by_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[contract] CHECK CONSTRAINT [FK_contract_last_mod_user];

    ALTER TABLE [dbo].[contract]  WITH CHECK ADD  CONSTRAINT [FK_contract_state]
        FOREIGN KEY([contract_state]) REFERENCES [dbo].[lookup_set] ([lookup_set_id]);
    ALTER TABLE [dbo].[contract] CHECK CONSTRAINT [FK_contract_state];

    ALTER TABLE [dbo].[contract]  WITH CHECK ADD  CONSTRAINT [CK_contract_active_deleted_exclusive]
        CHECK (NOT ([is_active] = 1 AND [is_deleted] = 1));
    ALTER TABLE [dbo].[contract] CHECK CONSTRAINT [CK_contract_active_deleted_exclusive];

    ALTER TABLE [dbo].[contract]  WITH CHECK ADD  CONSTRAINT [CK_contract_stamp_nonneg]
        CHECK ([stamp] >= 0);
    ALTER TABLE [dbo].[contract] CHECK CONSTRAINT [CK_contract_stamp_nonneg];
END
GO

/* 2.4 contract_state */
IF OBJECT_ID(N'dbo.contract_state', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[contract_state](
        [contract_state_id] [int] IDENTITY(1,1) NOT NULL,
        [lookup_set_id] [int] NOT NULL,
        [is_start_state] [bit] NOT NULL,
        [is_end_state] [bit] NOT NULL,
        CONSTRAINT [PK_contract_state] PRIMARY KEY CLUSTERED ([contract_state_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY],
        CONSTRAINT [UQ_contract_state_lookup_set_id] UNIQUE NONCLUSTERED ([lookup_set_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY];

    ALTER TABLE [dbo].[contract_state] ADD  CONSTRAINT [DF_contract_state_is_start]  DEFAULT ((0)) FOR [is_start_state];
    ALTER TABLE [dbo].[contract_state] ADD  CONSTRAINT [DF_contract_state_is_end]    DEFAULT ((0)) FOR [is_end_state];

    ALTER TABLE [dbo].[contract_state]  WITH CHECK ADD  CONSTRAINT [CK_contract_state_start_end]
        CHECK (NOT ([is_start_state] = 1 AND [is_end_state] = 1));
    ALTER TABLE [dbo].[contract_state] CHECK CONSTRAINT [CK_contract_state_start_end];
END
GO

/* 2.5 contract_state_transition */
IF OBJECT_ID(N'dbo.contract_state_transition', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[contract_state_transition](
        [contract_state_transition_id] [int] IDENTITY(1,1) NOT NULL,
        [from_state_id] [int] NOT NULL,
        [to_state_id] [int] NOT NULL,
        CONSTRAINT [PK_contract_state_transition] PRIMARY KEY CLUSTERED ([contract_state_transition_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY],
        CONSTRAINT [UQ_cst_from_to] UNIQUE NONCLUSTERED ([from_state_id] ASC, [to_state_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY];

    ALTER TABLE [dbo].[contract_state_transition]  WITH CHECK ADD  CONSTRAINT [FK_cst_from]
        FOREIGN KEY([from_state_id]) REFERENCES [dbo].[lookup_set] ([lookup_set_id]);
    ALTER TABLE [dbo].[contract_state_transition] CHECK CONSTRAINT [FK_cst_from];

    ALTER TABLE [dbo].[contract_state_transition]  WITH CHECK ADD  CONSTRAINT [FK_cst_to]
        FOREIGN KEY([to_state_id]) REFERENCES [dbo].[lookup_set] ([lookup_set_id]);
    ALTER TABLE [dbo].[contract_state_transition] CHECK CONSTRAINT [FK_cst_to];

    ALTER TABLE [dbo].[contract_state_transition]  WITH CHECK ADD  CONSTRAINT [CK_cst_not_same]
        CHECK ([from_state_id] <> [to_state_id]);
    ALTER TABLE [dbo].[contract_state_transition] CHECK CONSTRAINT [CK_cst_not_same];
END
GO

/* 2.6 event_log */
IF OBJECT_ID(N'dbo.event_log', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[event_log](
        [event_log_id] [int] IDENTITY(1,1) NOT NULL,
        [event_type] [int] NOT NULL,
        [input_dt] [datetime] NOT NULL,
        [description] [nvarchar](max) NOT NULL,
        [stack_trace] [nvarchar](max) NULL,
        [user_id] [int] NOT NULL,
        [stamp] [int] NOT NULL,
        CONSTRAINT [PK_event_log] PRIMARY KEY CLUSTERED ([event_log_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

    ALTER TABLE [dbo].[event_log] ADD  DEFAULT (CAST(dbo.GetLocalTime() AS datetime)) FOR [input_dt];
    ALTER TABLE [dbo].[event_log] ADD  DEFAULT ((0)) FOR [stamp];

    ALTER TABLE [dbo].[event_log]  WITH CHECK ADD  CONSTRAINT [FK_event_log_lookup_set]
        FOREIGN KEY([event_type]) REFERENCES [dbo].[lookup_set] ([lookup_set_id]);
    ALTER TABLE [dbo].[event_log] CHECK CONSTRAINT [FK_event_log_lookup_set];

    ALTER TABLE [dbo].[event_log]  WITH CHECK ADD  CONSTRAINT [FK_event_log_user]
        FOREIGN KEY([user_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[event_log] CHECK CONSTRAINT [FK_event_log_user];
END
GO

/* 2.7 product */
IF OBJECT_ID(N'dbo.product', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[product](
        [product_id] [int] IDENTITY(1,1) NOT NULL,
        [name] [nvarchar](200) NULL,
        [input_dt] [datetime2](0) NOT NULL,
        [input_user_id] [int] NOT NULL,
        [last_modified_by_id] [int] NOT NULL,
        [last_updated_dt] [datetime2](0) NOT NULL,
        [is_active] [bit] NOT NULL,
        [is_deleted] [bit] NOT NULL,
        [stamp] [int] NOT NULL,
        CONSTRAINT [PK_product] PRIMARY KEY CLUSTERED ([product_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY];

    ALTER TABLE [dbo].[product] ADD  CONSTRAINT [DF_product_input_dt]  DEFAULT (dbo.GetLocalTime()) FOR [input_dt];
    ALTER TABLE [dbo].[product] ADD  CONSTRAINT [DF_product_input_user]  DEFAULT ((0)) FOR [input_user_id];
    ALTER TABLE [dbo].[product] ADD  CONSTRAINT [DF_product_last_mod_user]  DEFAULT ((0)) FOR [last_modified_by_id];
    ALTER TABLE [dbo].[product] ADD  CONSTRAINT [DF_product_last_upd]     DEFAULT (dbo.GetLocalTime()) FOR [last_updated_dt];
    ALTER TABLE [dbo].[product] ADD  CONSTRAINT [DF_product_is_active]    DEFAULT ((1)) FOR [is_active];
    ALTER TABLE [dbo].[product] ADD  CONSTRAINT [DF_product_is_deleted]   DEFAULT ((0)) FOR [is_deleted];
    ALTER TABLE [dbo].[product] ADD  CONSTRAINT [DF_product_stamp]        DEFAULT ((0)) FOR [stamp];

    ALTER TABLE [dbo].[product]  WITH CHECK ADD  CONSTRAINT [FK_product_input_user]
        FOREIGN KEY([input_user_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[product] CHECK CONSTRAINT [FK_product_input_user];

    ALTER TABLE [dbo].[product]  WITH CHECK ADD  CONSTRAINT [FK_product_last_mod_user]
        FOREIGN KEY([last_modified_by_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[product] CHECK CONSTRAINT [FK_product_last_mod_user];

    ALTER TABLE [dbo].[product]  WITH CHECK ADD  CONSTRAINT [CK_product_active_deleted_exclusive]
        CHECK (NOT ([is_active] = 1 AND [is_deleted] = 1));
    ALTER TABLE [dbo].[product] CHECK CONSTRAINT [CK_product_active_deleted_exclusive];
END
GO

/* 2.8 product_variant */
IF OBJECT_ID(N'dbo.product_variant', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[product_variant](
        [product_variant_id] [int] IDENTITY(1,1) NOT NULL,
        [product_id] [int] NOT NULL,
        [size] [nvarchar](20) NULL,
        [color] [nvarchar](30) NULL,
        [amount] [decimal](18, 2) NOT NULL,
        [input_dt] [datetime2](0) NOT NULL,
        [input_user_id] [int] NOT NULL,
        [last_modified_by_id] [int] NOT NULL,
        [last_updated_dt] [datetime2](0) NOT NULL,
        [is_active] [bit] NOT NULL,
        [is_deleted] [bit] NOT NULL,
        [stamp] [int] NOT NULL,
        [size_key]  AS (isnull([size],N'')) PERSISTED NOT NULL,
        [color_key] AS (isnull([color],N'')) PERSISTED NOT NULL,
        [photo_file_name] [nvarchar](180) NULL,
        CONSTRAINT [PK_product_variant] PRIMARY KEY CLUSTERED ([product_variant_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY],
        CONSTRAINT [UQ_variant] UNIQUE NONCLUSTERED ([product_id] ASC, [size_key] ASC, [color_key] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY];

    ALTER TABLE [dbo].[product_variant] ADD  CONSTRAINT [DF_variant_input_dt]  DEFAULT (dbo.GetLocalTime()) FOR [input_dt];
    ALTER TABLE [dbo].[product_variant] ADD  CONSTRAINT [DF_variant_last_upd]   DEFAULT (dbo.GetLocalTime()) FOR [last_updated_dt];
    ALTER TABLE [dbo].[product_variant] ADD  CONSTRAINT [DF_variant_is_active]  DEFAULT ((1)) FOR [is_active];
    ALTER TABLE [dbo].[product_variant] ADD  CONSTRAINT [DF_variant_is_deleted] DEFAULT ((0)) FOR [is_deleted];
    ALTER TABLE [dbo].[product_variant] ADD  CONSTRAINT [DF_variant_stamp]      DEFAULT ((0)) FOR [stamp];

    ALTER TABLE [dbo].[product_variant]  WITH CHECK ADD  CONSTRAINT [FK_variant_input_user]
        FOREIGN KEY([input_user_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[product_variant] CHECK CONSTRAINT [FK_variant_input_user];

    ALTER TABLE [dbo].[product_variant]  WITH CHECK ADD  CONSTRAINT [FK_variant_last_mod_user]
        FOREIGN KEY([last_modified_by_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[product_variant] CHECK CONSTRAINT [FK_variant_last_mod_user];

    ALTER TABLE [dbo].[product_variant]  WITH CHECK ADD  CONSTRAINT [FK_variant_product]
        FOREIGN KEY([product_id]) REFERENCES [dbo].[product] ([product_id]);
    ALTER TABLE [dbo].[product_variant] CHECK CONSTRAINT [FK_variant_product];

    ALTER TABLE [dbo].[product_variant]  WITH CHECK ADD  CONSTRAINT [CK_variant_active_deleted_exclusive]
        CHECK (NOT ([is_active] = 1 AND [is_deleted] = 1));
    ALTER TABLE [dbo].[product_variant] CHECK CONSTRAINT [CK_variant_active_deleted_exclusive];

    ALTER TABLE [dbo].[product_variant]  WITH CHECK ADD  CONSTRAINT [CK_variant_photo_ext]
        CHECK ([photo_file_name] IS NULL OR
               [photo_file_name] LIKE '%.jpg' OR [photo_file_name] LIKE '%.jpeg' OR
               [photo_file_name] LIKE '%.png' OR [photo_file_name] LIKE '%.webp');
    ALTER TABLE [dbo].[product_variant] CHECK CONSTRAINT [CK_variant_photo_ext];
END
GO

/* 2.9 store */
IF OBJECT_ID(N'dbo.store', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[store](
        [store_id] [int] IDENTITY(1,1) NOT NULL,
        [name] [nvarchar](100) NOT NULL,
        [address] [nvarchar](255) NOT NULL,
        [phone_number] [nvarchar](20) NOT NULL,
        [email] [nvarchar](100) NULL,
        [working_hours] [nvarchar](255) NULL,
        [created_dt] [datetime2](0) NOT NULL,
        [updated_dt] [datetime2](0) NOT NULL,
        [last_modified_user_id] [int] NOT NULL,
        CONSTRAINT [PK_store] PRIMARY KEY CLUSTERED ([store_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY];

    ALTER TABLE [dbo].[store] ADD DEFAULT (dbo.GetLocalTime()) FOR [created_dt];
    ALTER TABLE [dbo].[store] ADD DEFAULT (dbo.GetLocalTime()) FOR [updated_dt];

    ALTER TABLE [dbo].[store]  WITH CHECK ADD  CONSTRAINT [FK_store_ax_user]
        FOREIGN KEY([last_modified_user_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]) ON UPDATE CASCADE;
    ALTER TABLE [dbo].[store] CHECK CONSTRAINT [FK_store_ax_user];
END
GO

/* 2.10 product_inventory */
IF OBJECT_ID(N'dbo.product_inventory', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[product_inventory](
        [product_inventory_id] [int] IDENTITY(1,1) NOT NULL,
        [product_variant_id] [int] NOT NULL,
        [store_id] [int] NOT NULL,
        [qty_on_hand] [int] NOT NULL,
        [input_dt] [datetime2](0) NOT NULL,
        [input_user_id] [int] NOT NULL,
        [last_modified_by_id] [int] NOT NULL,
        [last_updated_dt] [datetime2](0) NOT NULL,
        [is_active] [bit] NOT NULL,
        [is_deleted] [bit] NOT NULL,
        [stamp] [int] NOT NULL,
        CONSTRAINT [PK_product_inventory] PRIMARY KEY CLUSTERED ([product_inventory_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY],
        CONSTRAINT [UQ_inventory] UNIQUE NONCLUSTERED ([store_id] ASC, [product_variant_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY];

    ALTER TABLE [dbo].[product_inventory] ADD  CONSTRAINT [DF_inventory_qoh]        DEFAULT ((0)) FOR [qty_on_hand];
    ALTER TABLE [dbo].[product_inventory] ADD  CONSTRAINT [DF_inventory_input_dt]   DEFAULT (dbo.GetLocalTime()) FOR [input_dt];
    ALTER TABLE [dbo].[product_inventory] ADD  CONSTRAINT [DF_inventory_input_user] DEFAULT ((0)) FOR [input_user_id];
    ALTER TABLE [dbo].[product_inventory] ADD  CONSTRAINT [DF_inventory_last_mod_user] DEFAULT ((0)) FOR [last_modified_by_id];
    ALTER TABLE [dbo].[product_inventory] ADD  CONSTRAINT [DF_inventory_last_upd]   DEFAULT (dbo.GetLocalTime()) FOR [last_updated_dt];
    ALTER TABLE [dbo].[product_inventory] ADD  CONSTRAINT [DF_inventory_is_active]  DEFAULT ((1)) FOR [is_active];
    ALTER TABLE [dbo].[product_inventory] ADD  CONSTRAINT [DF_inventory_is_deleted] DEFAULT ((0)) FOR [is_deleted];
    ALTER TABLE [dbo].[product_inventory] ADD  CONSTRAINT [DF_inventory_stamp]      DEFAULT ((0)) FOR [stamp];

    ALTER TABLE [dbo].[product_inventory]  WITH CHECK ADD  CONSTRAINT [FK_inventory_input_user]
        FOREIGN KEY([input_user_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[product_inventory] CHECK CONSTRAINT [FK_inventory_input_user];

    ALTER TABLE [dbo].[product_inventory]  WITH CHECK ADD  CONSTRAINT [FK_inventory_last_mod_user]
        FOREIGN KEY([last_modified_by_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[product_inventory] CHECK CONSTRAINT [FK_inventory_last_mod_user];

    ALTER TABLE [dbo].[product_inventory]  WITH CHECK ADD  CONSTRAINT [FK_inventory_store]
        FOREIGN KEY([store_id]) REFERENCES [dbo].[store] ([store_id]);
    ALTER TABLE [dbo].[product_inventory] CHECK CONSTRAINT [FK_inventory_store];

    ALTER TABLE [dbo].[product_inventory]  WITH CHECK ADD  CONSTRAINT [FK_inventory_variant]
        FOREIGN KEY([product_variant_id]) REFERENCES [dbo].[product_variant] ([product_variant_id]);
    ALTER TABLE [dbo].[product_inventory] CHECK CONSTRAINT [FK_inventory_variant];

    ALTER TABLE [dbo].[product_inventory]  WITH CHECK ADD  CONSTRAINT [CK_inventory_active_deleted_exclusive]
        CHECK (NOT ([is_active] = 1 AND [is_deleted] = 1));
    ALTER TABLE [dbo].[product_inventory] CHECK CONSTRAINT [CK_inventory_active_deleted_exclusive];

    ALTER TABLE [dbo].[product_inventory]  WITH CHECK ADD  CONSTRAINT [CK_inventory_qoh]
        CHECK ([qty_on_hand] >= 0);
    ALTER TABLE [dbo].[product_inventory] CHECK CONSTRAINT [CK_inventory_qoh];
END
GO

/* 2.11 note */
IF OBJECT_ID(N'dbo.note', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[note](
        [note_id] [int] IDENTITY(1,1) NOT NULL,
        [contract_id] [int] NULL,
        [product_id] [int] NULL,
        [subject] [nvarchar](200) NOT NULL,
        [is_main] [bit] NOT NULL,
        [input_user_id] [int] NOT NULL,
        [last_modified_by_id] [int] NOT NULL,
        [input_dt] [datetime2](0) NOT NULL,
        [last_updated_dt] [datetime2](0) NOT NULL,
        [stamp] [int] NOT NULL,
        [is_deleted] [bit] NOT NULL,
        [comment] [nvarchar](max) NOT NULL,
        [is_active] [bit] NOT NULL,
        CONSTRAINT [PK_note] PRIMARY KEY CLUSTERED ([note_id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];

    ALTER TABLE [dbo].[note] ADD  CONSTRAINT [DF_note_is_main]           DEFAULT ((0)) FOR [is_main];
    ALTER TABLE [dbo].[note] ADD  CONSTRAINT [DF_note_input_dt]          DEFAULT (dbo.GetLocalTime()) FOR [input_dt];
    ALTER TABLE [dbo].[note] ADD  CONSTRAINT [DF_note_last_update_dt]    DEFAULT (dbo.GetLocalTime()) FOR [last_updated_dt];
    ALTER TABLE [dbo].[note] ADD  CONSTRAINT [DF_note_stamp]             DEFAULT ((0)) FOR [stamp];
    ALTER TABLE [dbo].[note] ADD  CONSTRAINT [DF_note_is_deleted]        DEFAULT ((0)) FOR [is_deleted];
    ALTER TABLE [dbo].[note] ADD  CONSTRAINT [DF_note_comment]           DEFAULT ('') FOR [comment];
    ALTER TABLE [dbo].[note] ADD  CONSTRAINT [DF_note_is_active]         DEFAULT ((1)) FOR [is_active];

    ALTER TABLE [dbo].[note]  WITH CHECK ADD  CONSTRAINT [FK_note_AxUser_Input]
        FOREIGN KEY([input_user_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[note] CHECK CONSTRAINT [FK_note_AxUser_Input];

    ALTER TABLE [dbo].[note]  WITH CHECK ADD  CONSTRAINT [FK_note_AxUser_Mod]
        FOREIGN KEY([last_modified_by_id]) REFERENCES [dbo].[ax_user] ([ax_user_id]);
    ALTER TABLE [dbo].[note] CHECK CONSTRAINT [FK_note_AxUser_Mod];

    ALTER TABLE [dbo].[note]  WITH CHECK ADD  CONSTRAINT [FK_note_Contract]
        FOREIGN KEY([contract_id]) REFERENCES [dbo].[contract] ([contract_id]);
    ALTER TABLE [dbo].[note] CHECK CONSTRAINT [FK_note_Contract];

    ALTER TABLE [dbo].[note]  WITH CHECK ADD  CONSTRAINT [FK_note_Product]
        FOREIGN KEY([product_id]) REFERENCES [dbo].[product] ([product_id]);
    ALTER TABLE [dbo].[note] CHECK CONSTRAINT [FK_note_Product];

    ALTER TABLE [dbo].[note]  WITH CHECK ADD  CONSTRAINT [CK_note_Target]
        CHECK ([contract_id] IS NOT NULL OR [product_id] IS NOT NULL);
    ALTER TABLE [dbo].[note] CHECK CONSTRAINT [CK_note_Target];
END
GO

/* 2.12 password_reset_token */
IF OBJECT_ID(N'dbo.password_reset_token', N'U') IS NULL
BEGIN
    SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON;
    CREATE TABLE [dbo].[password_reset_token](
        [password_reset_token_id] [int] IDENTITY(1,1) NOT NULL,
        [token] [nvarchar](200) NOT NULL,
        [user_id] [int] NULL,
        [code] [nvarchar](50) NULL,
        [email] [nvarchar](150) NULL,
        [created_dt] [datetime2](7) NOT NULL,
        [expires_dt] [datetime2](7) NOT NULL,
        [is_used] [bit] NOT NULL,
        [used_dt] [datetime2](7) NULL,
        CONSTRAINT [PK_password_reset_token] PRIMARY KEY CLUSTERED ([id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF,
                  ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF)
            ON [PRIMARY]
    ) ON [PRIMARY];
END
GO

/* 3. Functions */
CREATE OR ALTER FUNCTION [dbo].[fn_get_lookup_value] (@lookup_set_id INT)
RETURNS NVARCHAR(100)
AS
BEGIN
    DECLARE @result NVARCHAR(100);
    SELECT @result = value FROM dbo.lookup_set WHERE lookup_set_id = @lookup_set_id;
    RETURN ISNULL(@result, N'Unknown');
END;
GO

CREATE OR ALTER FUNCTION [dbo].[fn_event_logs]
(
    @EventTypeId INT,
    @FromDt DATETIME,
    @ToDt DATETIME
)
RETURNS TABLE
AS
RETURN
(
    SELECT
        el.event_log_id  AS EventLogId,
        el.event_type    AS EventTypeId,
        ls.value         AS EventTypeText,
        el.input_dt      AS InputDt,
        el.description   AS Description,
        el.stack_trace   AS StackTrace,
        el.user_id       AS UserId,
        u.code           AS UserCode
    FROM dbo.event_log el
    LEFT JOIN dbo.ax_user u ON u.ax_user_id = el.user_id
    LEFT JOIN dbo.lookup_set ls ON ls.lookup_set_id = el.event_type AND ls.set_name = 'EventType'
    WHERE (@EventTypeId = 0 OR el.event_type = @EventTypeId)
      AND el.input_dt BETWEEN @FromDt AND @ToDt
);
GO

CREATE OR ALTER FUNCTION [dbo].[ufn_contract_next_state_ids]
(
    @from_state_id INT
)
RETURNS TABLE
AS
RETURN
SELECT
    next_state_id = cst.to_state_id
FROM dbo.contract_state_transition AS cst
WHERE cst.from_state_id = @from_state_id;
GO

CREATE OR ALTER FUNCTION [dbo].[ufn_product_inventory_by_product]
(
    @product_id INT
)
RETURNS TABLE
AS
RETURN
SELECT
    pv.product_variant_id,
    pv.size,
    pv.color,
    s.store_id,
    inv.qty_on_hand
FROM dbo.product_variant   AS pv
JOIN dbo.product_inventory AS inv ON inv.product_variant_id = pv.product_variant_id
JOIN dbo.store             AS s   ON s.store_id = inv.store_id
WHERE pv.product_id = @product_id
  AND pv.is_active = 1 AND pv.is_deleted = 0
  AND inv.is_active = 1 AND inv.is_deleted = 0
  AND inv.qty_on_hand > 0;
GO

CREATE OR ALTER FUNCTION dbo.ufn_contract_next_state_names
(
    @from_state_id INT
)
RETURNS TABLE
AS
RETURN
(
    SELECT
        ns.next_state_id,
        ls.value AS next_state_name
    FROM dbo.ufn_contract_next_state_ids(@from_state_id) AS ns
    JOIN dbo.lookup_set AS ls
        ON ls.lookup_set_id = ns.next_state_id
       AND ls.set_name = N'ContractState'
);
GO

/* 4. Triggers */
IF OBJECT_ID(N'dbo.trg_stores_touch', N'TR') IS NOT NULL
    DROP TRIGGER dbo.trg_stores_touch;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE TRIGGER [dbo].[trg_stores_touch]
ON [dbo].[store]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE s
    SET s.updated_dt = dbo.GetLocalTime()
    FROM dbo.store AS s
    INNER JOIN inserted AS i ON s.store_id = i.store_id;
END;
GO

IF OBJECT_ID(N'dbo.tr_note_SetLastUpdate', N'TR') IS NOT NULL
    DROP TRIGGER dbo.tr_note_SetLastUpdate;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE TRIGGER [dbo].[tr_note_SetLastUpdate]
ON [dbo].[note]
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE n
       SET last_updated_dt = dbo.GetLocalTime(),
           stamp = n.stamp + 1
    FROM dbo.note AS n
    INNER JOIN inserted AS i ON i.note_id = n.note_id;
END;
GO